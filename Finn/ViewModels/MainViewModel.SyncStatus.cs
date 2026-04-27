using Avalonia.Controls;
using Finn.Model;
using Finn.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            // Folder watcher service for detecting file changes in sync folders
            private readonly FolderWatcherService _folderWatcher = new();
            private readonly Services.SharedFileWatcherService _sharedWatcher = new();
            private readonly object _folderSnapshotLock = new();

            /// <summary>
            /// Suppressed shared-file writes keyed by server path.  After a local
            /// push we record the server file's observed write timestamp here so the
            /// shared watcher can ignore exactly that self-originated change instead
            /// of relying on a timer-based global suppression flag.
            /// </summary>
            private readonly Dictionary<string, DateTime> _suppressedSharedWrites = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>
            /// Folders that have been flagged as needing a sync, across all projects.
            /// The dropdown in the toolbar binds to this collection.
            /// </summary>
            public ObservableCollection<SyncStatusEntry> PendingSyncFolders { get; } = new();

            /// <summary>
            /// True when at least one folder has pending changes.
            /// </summary>
            public bool HasPendingSyncFolders => PendingSyncFolders.Count > 0;

            /// <summary>
            /// Short status text for the sync indicator button.
            /// </summary>
            public string SyncStatusText => PendingSyncFolders.Count switch
            {
                0 => "All folders up to date",
                1 => "Detected changes in 1 folder",
                _ => $"Detected changes in {PendingSyncFolders.Count} folders"
            };

            private void RaiseSyncStatusChanged()
            {
                OnPropertyChanged(nameof(HasPendingSyncFolders));
                OnPropertyChanged(nameof(SyncStatusText));
            }

            /// <summary>
            /// Refreshes the set of watched folders based on the current projects.
            /// Call after loading/saving projects or adding/removing folders.
            /// Only activates watchers when <see cref="UISettingsViewModel.FolderWatchEnabled"/> is on.
            /// </summary>
            public void RefreshFolderWatchers()
            {
                if (UI.FolderWatchEnabled)
                {
                    _folderWatcher.FolderChanged -= OnFolderWatcherChanged;
                    _folderWatcher.FolderChanged += OnFolderWatcherChanged;
                    _folderWatcher.Refresh(Storage.StoredProjects);
                }
                else
                {
                    _folderWatcher.FolderChanged -= OnFolderWatcherChanged;
                    _folderWatcher.StopAll();
                    PendingSyncFolders.Clear();
                    RaiseSyncStatusChanged();
                }

                // Shared project watcher runs independently of the folder
                // watch setting so viewers always detect server updates.
                _sharedWatcher.ServerFileChanged -= OnSharedFileChanged;
                _sharedWatcher.ServerFileChanged += OnSharedFileChanged;
                _sharedWatcher.Refresh(Storage.StoredProjects);
            }

            /// <summary>Stops all folder and shared-file watchers (e.g. on shutdown).</summary>
            public void StopFolderWatchers()
            {
                _folderWatcher.Dispose();
                _sharedWatcher.Dispose();
            }

            /// <summary>Dismisses the sync notification without syncing.</summary>
            public void DismissFolderSyncNotification()
            {
                PendingSyncFolders.Clear();
                RaiseSyncStatusChanged();
            }

            /// <summary>
            /// Immediately flags the sync folders at the given paths as pending
            /// sync. Called when synced files are removed from the app so the
            /// user sees the folder is out of sync without waiting for a check.
            /// </summary>
            private void FlagSyncFoldersAsPending(HashSet<string> folderPaths)
            {
                string projectName = CurrentProject?.Namn ?? string.Empty;

                foreach (var folder in CurrentProject?.Folders ?? [])
                {
                    if (string.IsNullOrEmpty(folder.Path) || !folderPaths.Contains(folder.Path))
                        continue;

                    TryAddPendingSyncEntry(SyncStatusEntry.FromFolder(folder, projectName, "File(s) removed"));
                }

                RaiseSyncStatusChanged();
            }

            /// <summary>
            /// Adds a sync entry to <see cref="PendingSyncFolders"/> if no entry
            /// with the same folder path and project name already exists.
            /// Must be called on the UI thread.
            /// </summary>
            private bool TryAddPendingSyncEntry(SyncStatusEntry entry)
            {
                foreach (var existing in PendingSyncFolders)
                {
                    if (string.Equals(existing.FolderPath, entry.FolderPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.ProjectName, entry.ProjectName, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                PendingSyncFolders.Add(entry);
                return true;
            }

            /// <summary>
            /// Removes pending entries whose folder path matches any of the
            /// supplied paths (current project only). Used by batch-sync so
            /// only folders that actually completed are cleared.
            /// </summary>
            private void ClearSyncEntriesByPath(List<string> folderPaths)
            {
                var projectName = CurrentProject?.Namn;
                if (string.IsNullOrEmpty(projectName)) return;

                var pathSet = new HashSet<string>(folderPaths, StringComparer.OrdinalIgnoreCase);
                for (int i = PendingSyncFolders.Count - 1; i >= 0; i--)
                {
                    var entry = PendingSyncFolders[i];
                    if (string.Equals(entry.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)
                        && pathSet.Contains(entry.FolderPath))
                    {
                        PendingSyncFolders.RemoveAt(i);
                    }
                }
                RaiseSyncStatusChanged();
            }

            /// <summary>
            /// Syncs a single pending folder entry. Switches to the owning project,
            /// runs the sync (which shows the import dialog), and removes the entry
            /// only when the user confirms the import. On cancel the entry stays so
            /// the folder remains visibly unsynced.
            /// </summary>
            public async Task<bool> SyncSingleEntryAsync(SyncStatusEntry entry, Window? mainWindow)
            {
                if (entry == null || mainWindow == null || IsSyncing) return false;
                IsSyncing = true;
                try
                {

                // Find the project and folder
                var project = Storage.StoredProjects.FirstOrDefault(
                    p => string.Equals(p.Namn, entry.ProjectName, StringComparison.OrdinalIgnoreCase));
                if (project == null) return false;

                var folder = project.Folders.FirstOrDefault(
                    f => string.Equals(f.Path, entry.FolderPath, StringComparison.OrdinalIgnoreCase));
                if (folder == null) return false;

                // Switch to the project so the sync operates on the right context
                SetProject(entry.ProjectName);
                OnPropertyChanged(nameof(CurrentProject));
                SignalColumnsChanged();
                BuildTreeData();

                // Snapshot pre-sync state for OtherFiles folders so we can report changes.
                // AttachedFiles, VersionDelivery, and ProjectFiles show their own dialogs.
                int preCount = 0;
                bool showNotification = folder.Mode is SyncFolderMode.OtherFiles;
                if (showNotification)
                {
                    preCount = CurrentProject.StoredFiles.FirstOrDefault(
                                x => string.Equals(x.Namn, folder.AttachToFile, StringComparison.OrdinalIgnoreCase))
                            ?.OtherFiles.Count(x => string.Equals(x.SyncFolder, folder.Path, StringComparison.OrdinalIgnoreCase)) ?? 0;
                }

                // Run the sync for this single folder
                bool confirmed = await SyncFolderAsync(folder, mainWindow);

                // Refresh the grid/tree for non-project-level folders.
                // SyncProjectFolderAsync already calls these internally.
                if (folder.Mode != SyncFolderMode.ProjectFiles)
                {
                    UpdateFilter();
                    BuildTreeData();

                    if (confirmed && showNotification && mainWindow != null)
                    {
                        int postCount = CurrentProject.StoredFiles.FirstOrDefault(
                                    x => string.Equals(x.Namn, folder.AttachToFile, StringComparison.OrdinalIgnoreCase))
                                ?.OtherFiles.Count(x => string.Equals(x.SyncFolder, folder.Path, StringComparison.OrdinalIgnoreCase)) ?? 0;

                        string folderName = new System.IO.DirectoryInfo(folder.Path).Name;
                        string message = postCount == preCount
                            ? $"Folder \"{folderName}\" is already up to date.\nBaseline refreshed."
                            : $"Folder \"{folderName}\" synced.\n{postCount} file(s) (was {preCount}).";

                        var msgDia = new Dialogs.xMessageDia
                        {
                            RequestedThemeVariant = mainWindow.ActualThemeVariant
                        };
                        msgDia.SetMessage(message);
                        await msgDia.ShowDialog(mainWindow);
                    }
                }

                if (confirmed)
                {
                    // Remove the entry only when the user accepted the import
                    PendingSyncFolders.Remove(entry);
                    RaiseSyncStatusChanged();
                    MarkDirty();
                }

                return confirmed;
                }
                finally
                {
                    IsSyncing = false;
                }
            }

            /// <summary>
            /// Performs a lightweight comparison of every sync folder's tracked
            /// file count and timestamp against the stored baseline. Runs disk
            /// I/O on a background thread so the UI stays responsive.
            /// Populates <see cref="PendingSyncFolders"/> for the toolbar indicator.
            /// </summary>
            public async Task CheckFolderSyncOnStartupAsync()
            {
                if (!UI.FolderWatchEnabled) return;

                // Snapshot the folder list on the UI thread.
                // Don't call IsValid() here — it does Directory.Exists.
                var pairs = new List<(string ProjectName, FolderData Folder)>();
                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var folder in project.Folders)
                    {
                        if (!string.IsNullOrEmpty(folder.Path))
                            pairs.Add((project.Namn, folder));
                    }
                }

                // Run checks off the UI thread
                var outOfSync = await Task.Run(() =>
                {
                    var results = new List<(string ProjectName, FolderData Folder, string Summary)>();
                    foreach (var (projectName, folder) in pairs)
                    {
                        try
                        {
                            if (!folder.ExistsOnDisk())
                                continue;

                            if (folder.SyncedFileCount <= 0)
                                continue; // Never synced

                            // Fast path: root-dir timestamp.
                            // Catches all changes for TopDirectoryOnly folders
                            // and new delivery subfolders for VersionDelivery.
                            if (folder.LastSyncedUtc is { } ts
                                && Directory.GetLastWriteTimeUtc(folder.Path) <= ts)
                                continue; // Nothing changed

                            // Count files (the authoritative check).
                            int diskCount = CountDiskFiles(folder);
                            if (diskCount == folder.SyncedFileCount)
                                continue; // Same count — in sync

                            int diff = diskCount - folder.SyncedFileCount;
                            string summary = diff switch
                            {
                                > 0 => $"+{diff} new file(s)",
                                < 0 => $"{diff} file(s)",
                                _ => "Files changed"
                            };
                            results.Add((projectName, folder, summary));
                        }
                        catch { /* inaccessible folder — skip */ }
                    }
                    return results;
                });

                foreach (var (projectName, folder, summary) in outOfSync)
                    TryAddPendingSyncEntry(SyncStatusEntry.FromFolder(folder, projectName, summary));
                RaiseSyncStatusChanged();
            }

            /// <summary>
            /// Manually re-checks every sync folder across all projects.
            /// Compares file count and directory timestamp against the stored
            /// baseline. Heals stale baselines for folders that are in sync.
            /// Runs disk I/O on a background thread and updates the UI when done.
            /// </summary>
            public async Task CheckAllFoldersAsync()
            {
                // Snapshot project/folder pairs on the UI thread.
                // Don't call IsValid() here — it does Directory.Exists
                // which blocks on network shares.
                var pairs = new List<(string ProjectName, FolderData Folder)>();
                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var folder in project.Folders)
                    {
                        if (string.IsNullOrEmpty(folder.Path))
                            continue;

                        pairs.Add((project.Namn, folder));
                    }
                }

                if (pairs.Count == 0) return;

                // Show progress via the existing background-task indicator
                PreviewVM.BackgroundTaskActive = true;
                PreviewVM.BackgroundTaskMessage = $"Checking 0/{pairs.Count} folders…";
                PreviewVM.BackgroundTaskProgress = 0;

                // Run disk I/O on a background thread.
                bool baselineUpdated = false;
                int checked_ = 0;
                var progress = new Progress<int>(i =>
                {
                    PreviewVM.BackgroundTaskMessage = $"Checking {i}/{pairs.Count} folders…";
                    PreviewVM.BackgroundTaskProgress = (int)(100.0 * i / pairs.Count);
                });

                var outOfSync = await Task.Run(() =>
                {
                    var results = new List<(string ProjectName, FolderData Folder, string Summary)>();
                    foreach (var (projectName, folder) in pairs)
                    {
                        try
                        {
                            if (!folder.ExistsOnDisk())
                            {
                                checked_++;
                                ((IProgress<int>)progress).Report(checked_);
                                continue;
                            }

                            bool hasBaseline = folder.SyncedFileCount > 0;

                            // Fast path: one root-dir timestamp read.
                            // For TopDirectoryOnly this catches all changes.
                            // For AllDirectories (version folders) this catches
                            // new delivery subfolders being added, which is the
                            // primary change vector. Changes inside existing
                            // subfolders are handled by the FileSystemWatcher.
                            if (hasBaseline
                                && folder.LastSyncedUtc is { } ts
                                && Directory.GetLastWriteTimeUtc(folder.Path) <= ts)
                            {
                                // Root directory unchanged — skip.
                            }
                            else
                            {
                                int diskCount = CountDiskFiles(folder);

                                if (hasBaseline && diskCount != folder.SyncedFileCount)
                                {
                                    int diff = diskCount - folder.SyncedFileCount;
                                    string summary = diff switch
                                    {
                                        > 0 => $"+{diff} new file(s)",
                                        < 0 => $"{diff} file(s)",
                                        _ => "Files changed"
                                    };
                                    results.Add((projectName, folder, summary));
                                }
                                else if (folder.SyncedFileCount != diskCount
                                         || folder.LastSyncedUtc == null)
                                {
                                    // Heal baseline
                                    folder.SyncedFileCount = diskCount;
                                    folder.LastSyncedUtc = Directory.GetLastWriteTimeUtc(folder.Path);
                                    baselineUpdated = true;
                                }
                            }
                        }
                        catch { /* inaccessible folder — skip */ }

                        checked_++;
                        ((IProgress<int>)progress).Report(checked_);
                    }
                    return results;
                });

                // Rebuild PendingSyncFolders from scratch so stale entries are removed
                PendingSyncFolders.Clear();
                foreach (var (projectName, folder, summary) in outOfSync)
                {
                    PendingSyncFolders.Add(SyncStatusEntry.FromFolder(folder, projectName, summary));
                }
                RaiseSyncStatusChanged();

                if (baselineUpdated)
                    MarkDirty();

                // Show result briefly, then hide
                PreviewVM.BackgroundTaskMessage = outOfSync.Count == 0
                    ? $"All {pairs.Count} folders up to date"
                    : $"Found {outOfSync.Count} folder(s) with changes";
                PreviewVM.BackgroundTaskProgress = 100;
                await Task.Delay(2000);
                PreviewVM.BackgroundTaskActive = false;
            }

            /// <summary>
            /// Checks whether a folder's disk state differs from the stored
            /// baseline. For <see cref="SearchOption.TopDirectoryOnly"/> folders
            /// the root-dir timestamp provides a cheap fast exit. For
            /// <see cref="SearchOption.AllDirectories"/> (version folders) only
            /// the file count is compared — subdirectory timestamp walks are too
            /// expensive on network shares.
            /// Returns false for folders that have never been synced.
            /// </summary>
            private static bool IsFolderOutOfSync(FolderData folder)
            {
                if (folder.SyncedFileCount <= 0 || !folder.ExistsOnDisk())
                    return false; // Never synced, invalid, or missing

                // Fast path: root-dir timestamp covers direct children
                // (TopDirectoryOnly) and new delivery subfolders
                // (VersionDelivery). Changes inside existing subdirectories
                // are caught by the FileSystemWatcher instead.
                if (folder.LastSyncedUtc is { } baseline
                    && Directory.GetLastWriteTimeUtc(folder.Path) <= baseline)
                    return false;

                // Authoritative check: file count.
                return CountDiskFiles(folder) != folder.SyncedFileCount;
            }

            /// <summary>
            /// Returns the search pattern
            /// for the folder's <see cref="FolderData.Mode"/>.
            /// </summary>
            private static (string Pattern, SearchOption Search) GetFileFilter(FolderData folder)
            {
                return folder.Mode switch
                {
                    SyncFolderMode.OtherFiles => ("*", SearchOption.TopDirectoryOnly),
                    SyncFolderMode.VersionDelivery => ("*.pdf", SearchOption.AllDirectories),
                    SyncFolderMode.ProjectFiles => ("*.pdf", SearchOption.TopDirectoryOnly),
                    SyncFolderMode.AttachedFiles => ("*.pdf", SearchOption.TopDirectoryOnly),
                    _ => ("*", SearchOption.TopDirectoryOnly)
                };
            }

            /// <summary>
            /// Counts the actual files on disk for the given folder.
            /// Uses the same file-type filter as the sync logic for each mode.
            /// Returns <c>-1</c> when the folder no longer exists so callers
            /// can distinguish "missing" from "empty".
            /// </summary>
            private static int CountDiskFiles(FolderData folder)
            {
                if (!folder.ExistsOnDisk()) return -1;

                var (pattern, search) = GetFileFilter(folder);
                return Directory.EnumerateFiles(folder.Path, pattern, search).Count();
            }

            /// <summary>
            /// Records the current disk state as the sync baseline so that
            /// <see cref="IsFolderOutOfSync"/> won't produce false positives.
            /// Stores the file count and the directory's own write timestamp.
            /// For version folders, also records the tracked version count for display.
            /// Runs the file count on a background thread to avoid blocking the UI.
            /// </summary>
            private async Task RecordSyncBaselineAsync(FolderData folder)
            {
                if (!folder.ExistsOnDisk())
                {
                    folder.SyncedFileCount = -1;
                    folder.LastSyncedUtc = DateTime.UtcNow;
                    return;
                }

                int count = await Task.Run(() => CountDiskFiles(folder));
                folder.SyncedFileCount = count;
                folder.LastSyncedUtc = Directory.GetLastWriteTimeUtc(folder.Path);

                if (folder.Mode == SyncFolderMode.VersionDelivery)
                    folder.TrackedVersionCount = CountVersionsUnderPath(folder.Path);
            }

            private void OnFolderWatcherChanged(IReadOnlySet<string> changedPaths)
            {
                // Snapshot StoredProjects so we don't read the collection on this
                // thread-pool thread while the UI thread may be modifying it.
                List<(string ProjectName, FolderData Folder)> candidates;
                lock (_folderSnapshotLock)
                {
                    candidates = [];
                    foreach (var project in Storage.StoredProjects)
                    {
                        foreach (var folder in project.Folders)
                        {
                            if (!string.IsNullOrEmpty(folder.Path) && changedPaths.Contains(folder.Path))
                                candidates.Add((project.Namn, folder));
                        }
                    }
                }

                var newEntries = new List<SyncStatusEntry>();
                foreach (var (projectName, folder) in candidates)
                {
                    try
                    {
                        // For TopDirectoryOnly folders, verify with file count
                        // to filter out false positives from temp files.
                        // For AllDirectories (version folders), skip the
                        // expensive recursive count — the watcher already
                        // confirmed a filesystem event, and CountDiskFiles
                        // with AllDirectories is too slow for a callback.
                        var (_, search) = GetFileFilter(folder);
                        if (search == SearchOption.TopDirectoryOnly
                            && !IsFolderOutOfSync(folder))
                            continue;
                    }
                    catch (Exception ex)
                    {
                        Utils.ErrorLogger.Log(ex, $"OnFolderWatcherChanged({folder.Path})");
                        continue;
                    }

                    newEntries.Add(SyncStatusEntry.FromFolder(folder, projectName));
                }

                if (newEntries.Count > 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var entry in newEntries)
                            TryAddPendingSyncEntry(entry);
                        RaiseSyncStatusChanged();
                    });
                }
            }

            /// <summary>
            /// Called by the shared file watcher when a server JSON file is modified.
            /// Updates sync status for affected shared projects and notifies the UI.
            /// </summary>
            private void OnSharedFileChanged(IReadOnlySet<string> changedFiles)
            {
                System.Diagnostics.Debug.WriteLine($"[SharedSync] OnSharedFileChanged: {changedFiles.Count} files");
                foreach (var f in changedFiles)
                    System.Diagnostics.Debug.WriteLine($"[SharedSync]   changed: {f}");

                // Snapshot projects on this thread-pool thread
                List<ProjectData> affected;
                lock (_folderSnapshotLock)
                {
                    affected = [];
                    foreach (var project in Storage.StoredProjects)
                    {
                        bool hasPath = !string.IsNullOrEmpty(project.SharedPath);
                        bool match = hasPath && changedFiles.Contains(project.SharedPath);
                        bool suppressSelfPush = match && ShouldSuppressSharedFileChange(project);
                        System.Diagnostics.Debug.WriteLine($"[SharedSync]   project '{project.Namn}' path='{project.SharedPath}' match={match} suppressSelfPush={suppressSelfPush}");
                        if (match && !suppressSelfPush)
                            affected.Add(project);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[SharedSync] Affected projects: {affected.Count}");
                if (affected.Count == 0) return;

                foreach (var project in affected)
                {
                    UpdateSharedSyncStatus(project);
                    System.Diagnostics.Debug.WriteLine($"[SharedSync] After UpdateStatus: '{project.Namn}' → {project.SharedSyncStatus} (LastPushed={project.LastPushedUtc:O})");
                }

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var project in affected)
                    {
                        if (project.SharedSyncStatus == SharedSyncState.ServerAhead)
                            PreviewVM.StatusMessage = $"\"{project.Namn}\" has updates on the server";
                        else if (project.SharedSyncStatus == SharedSyncState.Conflicted)
                            PreviewVM.StatusMessage = $"\"{project.Namn}\" has server updates and local changes — pull recommended";
                    }
                    BuildTreeData();
                });
            }

            private void RegisterSuppressedSharedWrite(string sharedPath, DateTime writeTimeUtc)
            {
                if (string.IsNullOrWhiteSpace(sharedPath))
                    return;

                lock (_folderSnapshotLock)
                    _suppressedSharedWrites[sharedPath] = writeTimeUtc;
            }

            private bool ShouldSuppressSharedFileChange(ProjectData project)
            {
                if (string.IsNullOrEmpty(project.SharedPath))
                    return false;

                DateTime expectedWriteUtc;
                lock (_folderSnapshotLock)
                {
                    if (!_suppressedSharedWrites.TryGetValue(project.SharedPath, out expectedWriteUtc))
                        return false;
                }

                try
                {
                    var serverModified = File.GetLastWriteTimeUtc(project.SharedPath);

                    // FileSystemWatcher timestamps can be coarse (e.g. 1-second
                    // resolution on some shares/filesystems), so accept small drift
                    // around the exact write time we just observed locally.
                    bool suppress = serverModified <= expectedWriteUtc.AddSeconds(1);

                    // Once a newer external write is observed, or our own write has
                    // been consumed, drop the suppression entry so future events are
                    // evaluated normally.
                    if (suppress || serverModified > expectedWriteUtc.AddSeconds(1))
                    {
                        lock (_folderSnapshotLock)
                            _suppressedSharedWrites.Remove(project.SharedPath);
                    }

                    return suppress;
                }
                catch (Exception ex)
                {
                    Utils.ErrorLogger.Log(ex, $"ShouldSuppressSharedFileChange({project.SharedPath})");
                    lock (_folderSnapshotLock)
                        _suppressedSharedWrites.Remove(project.SharedPath);
                    return false;
                }
            }

            /// <summary>
            /// Computes the sync state of a shared project by comparing the
            /// current server file timestamp against <see cref="ProjectData.LastPushedUtc"/>,
            /// which acts as the local sync baseline (not strictly only a local push time).
            /// Preserves <see cref="SharedSyncState.LocalAhead"/> by upgrading to
            /// <see cref="SharedSyncState.Conflicted"/> when the server also changed.
            /// Safe to call from any thread (only reads filesystem + project fields).
            /// </summary>
            public static void UpdateSharedSyncStatus(ProjectData project)
            {
                ApplySharedSyncStatus(project, ComputeSharedSyncState(project));
            }

            private static void ApplySharedSyncStatus(ProjectData project, SharedSyncState newState)
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    project.SharedSyncStatus = newState;
                else
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => project.SharedSyncStatus = newState)
                        .GetTask().GetAwaiter().GetResult();
            }

            private static SharedSyncState ComputeSharedSyncState(ProjectData project)
            {
                if (string.IsNullOrEmpty(project.SharedPath))
                    return SharedSyncState.Unknown;

                try
                {
                    if (!File.Exists(project.SharedPath))
                        return SharedSyncState.ServerMissing;

                    if (project.LastPushedUtc == null)
                        return SharedSyncState.ServerAhead;

                    var serverModified = File.GetLastWriteTimeUtc(project.SharedPath);
                    bool serverChanged = serverModified > project.LastPushedUtc.Value.AddSeconds(5);

                    if (project.IsViewer)
                        return serverChanged ? SharedSyncState.ServerAhead : SharedSyncState.InSync;

                    bool localDirty = project.SharedSyncStatus is SharedSyncState.LocalAhead
                                                               or SharedSyncState.Conflicted;

                    if (serverChanged && localDirty) return SharedSyncState.Conflicted;
                    if (serverChanged)               return SharedSyncState.ServerAhead;
                    if (localDirty)                  return SharedSyncState.LocalAhead;
                    return SharedSyncState.InSync;
                }
                catch
                {
                    return SharedSyncState.Unknown;
                }
            }

            /// <summary>
            /// Checks shared project sync status on startup (similar to folder sync check).
            /// Runs filesystem checks on a background thread.
            /// </summary>
            public async Task CheckSharedSyncOnStartupAsync()
            {
                var sharedProjects = Storage.StoredProjects
                    .Where(p => p.IsShared)
                    .ToList();

                if (sharedProjects.Count == 0) return;

                var computedStates = await Task.Run(() =>
                    sharedProjects
                        .Select(project => (Project: project, State: ComputeSharedSyncState(project)))
                        .ToList());

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var item in computedStates)
                        item.Project.SharedSyncStatus = item.State;
                }).GetTask().ConfigureAwait(false);

                // Notify on server-ahead or conflicted projects
                var needAttention = sharedProjects
                    .Where(p => p.SharedSyncStatus is SharedSyncState.ServerAhead or SharedSyncState.Conflicted)
                    .ToList();

                if (needAttention.Count > 0)
                {
                    int conflicts = needAttention.Count(p => p.SharedSyncStatus == SharedSyncState.Conflicted);
                    int serverAhead = needAttention.Count - conflicts;

                    if (conflicts > 0 && serverAhead > 0)
                        PreviewVM.StatusMessage = $"{serverAhead} project(s) have server updates, {conflicts} have conflicts";
                    else if (conflicts > 0)
                        PreviewVM.StatusMessage = $"{conflicts} shared project(s) have both local and server changes";
                    else
                    {
                        string names = string.Join(", ", needAttention.Select(p => $"\"{p.Namn}\""));
                        PreviewVM.StatusMessage = needAttention.Count == 1
                            ? $"{names} has updates on the server"
                            : $"{needAttention.Count} shared projects have server updates";
                    }
                }

                BuildTreeData();
            }

            // CalendarStorage moved into CalendarViewModel

            
        }
    }
