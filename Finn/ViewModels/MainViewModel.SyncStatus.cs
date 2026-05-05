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
            /// file paths against the stored baseline. Runs disk I/O on a
            /// background thread so the UI stays responsive.
            /// Populates <see cref="PendingSyncFolders"/> for the toolbar indicator.
            /// </summary>
            public async Task CheckFolderSyncOnStartupAsync()
            {
                if (!UI.FolderWatchEnabled) return;

                // Snapshot folder list on the UI thread: baseline paths, current app paths,
                // and legacy counts. The background thread must only do disk I/O.
                var pairs = new List<(
                    string ProjectName,
                    FolderData Folder,
                    HashSet<string>? BaselinePaths,
                    HashSet<string>? CurrentAppPaths,
                    int LegacyAppCount)>();

                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var folder in project.Folders)
                    {
                        if (string.IsNullOrEmpty(folder.Path))
                            continue;

                        HashSet<string>? baselinePaths = folder.SyncedPaths.Count > 0
                            ? new HashSet<string>(folder.SyncedPaths, StringComparer.OrdinalIgnoreCase)
                            : null;

                        HashSet<string>? currentAppPaths = baselinePaths != null
                            ? new HashSet<string>(CollectSyncedPaths(folder, project), StringComparer.OrdinalIgnoreCase)
                            : null;

                        int legacyCount = baselinePaths == null
                            ? CountAppFiles(folder, project)
                            : -1;

                        pairs.Add((project.Namn, folder, baselinePaths, currentAppPaths, legacyCount));
                    }
                }

                // Run disk checks off the UI thread
                var outOfSync = await Task.Run(() =>
                {
                    var results = new List<(string ProjectName, FolderData Folder, string Summary)>();
                    foreach (var (projectName, folder, baselinePaths, currentAppPaths, legacyAppCount) in pairs)
                    {
                        try
                        {
                            if (!folder.ExistsOnDisk())
                                continue;

                            if (baselinePaths != null && currentAppPaths != null)
                            {
                                // 1. App-side check: has the user removed files from the app
                                //    since the last sync? (disk untouched, so count check would miss this)
                                bool appChanged = currentAppPaths.Count != baselinePaths.Count
                                    || currentAppPaths.Any(p => !baselinePaths.Contains(p));

                                if (appChanged)
                                {
                                    int diff = currentAppPaths.Count - baselinePaths.Count;
                                    string appSummary = diff < 0
                                        ? $"{-diff} file(s) removed from app"
                                        : "Files changed in app";
                                    results.Add((projectName, folder, appSummary));
                                    continue;
                                }

                                // 2. Disk-side check: fast timestamp pre-check, then count.
                                // VersionDelivery is skipped here: CountDiskFiles counts all PDFs
                                // in the folder, but SyncedPaths only tracks imported ones —
                                // unmatched PDFs would cause a permanent false positive.
                                // The app-side check above is sufficient for version folders.
                                if (folder.Mode == SyncFolderMode.VersionDelivery)
                                    continue;

                                if (folder.LastSyncedUtc is { } ts
                                    && Directory.GetLastWriteTimeUtc(folder.Path) <= ts)
                                    continue; // Disk untouched — in sync

                                int diskCount = CountDiskFiles(folder);
                                if (diskCount == baselinePaths.Count)
                                    continue; // Count still matches baseline — in sync

                                int diskDiff = diskCount - baselinePaths.Count;
                                string diskSummary = diskDiff switch
                                {
                                    > 0 => $"+{diskDiff} new file(s) on disk",
                                    < 0 => $"{-diskDiff} file(s) removed from disk",
                                    _   => "Files changed"
                                };
                                results.Add((projectName, folder, diskSummary));
                            }
                            else
                            {
                                // Legacy path: no stored path set — fall back to count comparison.
                                // VersionDelivery folders without a path baseline are skipped:
                                // CountDiskFiles counts all PDFs, not just imported ones.
                                if (folder.SyncedFileCount <= 0 || folder.Mode == SyncFolderMode.VersionDelivery)
                                    continue; // Never synced or version folder without reliable baseline

                                if (legacyAppCount >= 0 && legacyAppCount < folder.SyncedFileCount)
                                {
                                    int diff = legacyAppCount - folder.SyncedFileCount;
                                    results.Add((projectName, folder, $"{diff} file(s) removed from app"));
                                    continue;
                                }

                                if (folder.LastSyncedUtc is { } ts
                                    && Directory.GetLastWriteTimeUtc(folder.Path) <= ts)
                                    continue;

                                int diskCount = CountDiskFiles(folder);
                                if (diskCount == folder.SyncedFileCount)
                                    continue;

                                int diskDiff = diskCount - folder.SyncedFileCount;
                                string summary = diskDiff switch
                                {
                                    > 0 => $"+{diskDiff} new file(s)",
                                    < 0 => $"{diskDiff} file(s)",
                                    _ => "Files changed"
                                };
                                results.Add((projectName, folder, summary));
                            }
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
            /// Compares current app-tracked paths and disk state against the stored
            /// baseline when available; falls back to file-count comparison for
            /// folders synced before the path-set baseline was introduced.
            /// Heals stale baselines for in-sync folders.
            /// Runs disk I/O on a background thread and updates the UI when done.
            /// </summary>
            public async Task CheckAllFoldersAsync()
            {
                // Snapshot project/folder pairs on the UI thread.
                // Collect baseline path sets and current app paths now so the
                // background thread only does disk I/O and not app-state reads.
                var pairs = new List<(
                    string ProjectName,
                    FolderData Folder,
                    Model.ProjectData Project,
                    HashSet<string>? BaselinePaths,
                    HashSet<string>? CurrentAppPaths,
                    int LegacyAppCount)>();

                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var folder in project.Folders)
                    {
                        if (string.IsNullOrEmpty(folder.Path))
                            continue;

                        HashSet<string>? baselinePaths = folder.SyncedPaths.Count > 0
                            ? new HashSet<string>(folder.SyncedPaths, StringComparer.OrdinalIgnoreCase)
                            : null;

                        HashSet<string>? currentAppPaths = baselinePaths != null
                            ? new HashSet<string>(CollectSyncedPaths(folder, project), StringComparer.OrdinalIgnoreCase)
                            : null;

                        int legacyCount = baselinePaths == null ? CountAppFiles(folder, project) : -1;

                        pairs.Add((project.Namn, folder, project, baselinePaths, currentAppPaths, legacyCount));
                    }
                }

                if (pairs.Count == 0) return;

                PreviewVM.BackgroundTaskActive = true;
                PreviewVM.BackgroundTaskMessage = $"Checking 0/{pairs.Count} folders…";
                PreviewVM.BackgroundTaskProgress = 0;

                bool baselineUpdated = false;
                int checked_ = 0;
                var progress = new Progress<int>(i =>
                {
                    PreviewVM.BackgroundTaskMessage = $"Checking {i}/{pairs.Count} folders…";
                    PreviewVM.BackgroundTaskProgress = (int)(100.0 * i / pairs.Count);
                });

                // Collect healed baselines to apply back on the UI thread.
                var healedBaselines = new List<(FolderData Folder, Model.ProjectData Project)>();

                var outOfSync = await Task.Run(() =>
                {
                    var results = new List<(string ProjectName, FolderData Folder, string Summary)>();
                    foreach (var (projectName, folder, project, baselinePaths, currentAppPaths, legacyAppCount) in pairs)
                    {
                        try
                        {
                            if (!folder.ExistsOnDisk())
                            {
                                checked_++;
                                ((IProgress<int>)progress).Report(checked_);
                                continue;
                            }

                            if (baselinePaths != null && currentAppPaths != null)
                            {
                                // 1. App-side check: detect files removed from the app since the last sync.
                                bool appChanged = currentAppPaths.Count != baselinePaths.Count
                                    || currentAppPaths.Any(p => !baselinePaths.Contains(p));

                                if (appChanged)
                                {
                                    int diff = currentAppPaths.Count - baselinePaths.Count;
                                    string appSummary = diff < 0
                                        ? $"{-diff} file(s) removed from app"
                                        : "Files changed in app";
                                    results.Add((projectName, folder, appSummary));
                                    checked_++;
                                    ((IProgress<int>)progress).Report(checked_);
                                    continue;
                                }

                                // 2. Disk-side check: timestamp pre-check then count.
                                // VersionDelivery is skipped: CountDiskFiles counts all PDFs
                                // in the folder, but SyncedPaths only tracks imported ones —
                                // unmatched PDFs would cause a permanent false positive.
                                if (folder.Mode != SyncFolderMode.VersionDelivery)
                                {

                                bool timestampClean = folder.LastSyncedUtc is { } ts
                                    && Directory.GetLastWriteTimeUtc(folder.Path) <= ts;

                                int diskCount = CountDiskFiles(folder);

                                if (timestampClean && diskCount == baselinePaths.Count)
                                {
                                    // Everything matches — no change needed.
                                }
                                else if (diskCount != baselinePaths.Count)
                                {
                                    int diff = diskCount - baselinePaths.Count;
                                    string summary = diff switch
                                    {
                                        > 0 => $"+{diff} new file(s) on disk",
                                        < 0 => $"{-diff} file(s) removed from disk",
                                        _   => "Files changed"
                                    };
                                    results.Add((projectName, folder, summary));
                                }
                                else
                                {
                                    // Timestamp changed but disk count matches — heal timestamp.
                                    healedBaselines.Add((folder, project));
                                    baselineUpdated = true;
                                }
                                } // end if (folder.Mode != SyncFolderMode.VersionDelivery)
                            }
                            else
                            {
                                // Legacy: no path set — fall back to count heuristic.
                                bool hasBaseline = folder.SyncedFileCount > 0;

                                if (hasBaseline && legacyAppCount >= 0)
                                {
                                    int baseline = folder.Mode == SyncFolderMode.VersionDelivery
                                        ? folder.TrackedVersionCount
                                        : folder.SyncedFileCount;
                                    if (legacyAppCount < baseline)
                                    {
                                        int diff = legacyAppCount - baseline;
                                        results.Add((projectName, folder, $"{diff} file(s) removed from app"));
                                        checked_++;
                                        ((IProgress<int>)progress).Report(checked_);
                                        continue;
                                    }
                                }

                                if (hasBaseline
                                    && folder.LastSyncedUtc is { } ts
                                    && Directory.GetLastWriteTimeUtc(folder.Path) <= ts)
                                {
                                    // Root directory unchanged.
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
                                    else if (folder.SyncedFileCount != diskCount || folder.LastSyncedUtc == null)
                                    {
                                        // Heal legacy baseline counts + timestamp.
                                        folder.SyncedFileCount = diskCount;
                                        folder.LastSyncedUtc = Directory.GetLastWriteTimeUtc(folder.Path);
                                        baselineUpdated = true;
                                    }
                                }
                            }
                        }
                        catch { /* inaccessible folder — skip */ }

                        checked_++;
                        ((IProgress<int>)progress).Report(checked_);
                    }
                    return results;
                });

                // Apply healed baselines for path-set folders (must be on UI thread
                // because CollectSyncedPaths reads app collections).
                foreach (var (folder, project) in healedBaselines)
                {
                    folder.SyncedPaths = CollectSyncedPaths(folder, project);
                    folder.LastSyncedUtc = Directory.GetLastWriteTimeUtc(folder.Path);
                }

                // Rebuild PendingSyncFolders from scratch so stale entries are removed
                PendingSyncFolders.Clear();
                foreach (var (projectName, folder, summary) in outOfSync)
                    PendingSyncFolders.Add(SyncStatusEntry.FromFolder(folder, projectName, summary));
                RaiseSyncStatusChanged();

                if (baselineUpdated)
                    MarkDirty();

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
            /// Counts the actual files on disk for the given folder, applying the
            /// same exclusion filter used during import so the count is directly
            /// comparable to the tracked path set.
            /// Returns <c>-1</c> when the folder no longer exists so callers
            /// can distinguish "missing" from "empty".
            /// </summary>
            private static int CountDiskFiles(FolderData folder)
            {
                if (!folder.ExistsOnDisk()) return -1;

                var (pattern, search) = GetFileFilter(folder);
                bool isVersionFolder = folder.Mode == SyncFolderMode.VersionDelivery;

                return Directory.EnumerateFiles(folder.Path, pattern, search)
                    .Count(p => isVersionFolder
                        ? !folder.IsExcludedPath(p)
                        : !folder.IsExcluded(System.IO.Path.GetFileNameWithoutExtension(p)));
            }

            /// <summary>
            /// Records the current app state as the sync baseline so that
            /// <see cref="CheckFolderSyncOnStartupAsync"/> and <see cref="CheckAllFoldersAsync"/>
            /// won't produce false positives. Stores the set of tracked paths (app state),
            /// the disk file count (for watcher fast-checks), and the directory's last-write timestamp.
            /// Runs all disk I/O on a background thread to avoid blocking the UI.
            /// </summary>
            private async Task RecordSyncBaselineAsync(FolderData folder)
            {
                if (!folder.ExistsOnDisk())
                {
                    folder.SyncedPaths = [];
                    folder.SyncedFileCount = -1;
                    folder.LastSyncedUtc = DateTime.UtcNow;
                    return;
                }

                // Collect app-state paths on the UI thread, then do all disk I/O off it.
                var project = CurrentProject;
                var appPaths = CollectSyncedPaths(folder, project);

                var (diskCount, dirWriteUtc) = await Task.Run(() =>
                    (CountDiskFiles(folder), Directory.GetLastWriteTimeUtc(folder.Path)));

                folder.SyncedPaths = appPaths;
                folder.SyncedFileCount = diskCount;
                folder.LastSyncedUtc = dirWriteUtc;

                // Keep legacy count fields consistent for older data consumers / display fallback.
                if (folder.Mode == SyncFolderMode.VersionDelivery)
                    folder.TrackedVersionCount = appPaths.Count;
            }

            /// <summary>
            /// Counts the number of files currently tracked in the app for the given
            /// folder, using the same folder-path match that was recorded at import
            /// time. This allows detecting files removed from app tracking even when
            /// the disk hasn't changed (so a disk count comparison would miss it).
            /// </summary>
            private int CountAppFiles(FolderData folder, Model.ProjectData project)
            {
                string folderPath = folder.Path;
                return folder.Mode switch
                {
                    SyncFolderMode.ProjectFiles =>
                        project.StoredFiles.Count(f =>
                            f.IsFromFolder
                            && string.Equals(f.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase)),

                    SyncFolderMode.AttachedFiles =>
                        project.StoredFiles.Count(f =>
                            f.IsAppendedFile && f.IsFromFolder
                            && string.Equals(f.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase)),

                    SyncFolderMode.OtherFiles =>
                        project.StoredFiles
                            .SelectMany(f => f.OtherFiles)
                            .Count(o => o.IsFromFolder
                                && string.Equals(o.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase)),

                    SyncFolderMode.VersionDelivery =>
                        project.StoredFiles
                            .SelectMany(f => f.Versions)
                            .Count(v => v.Sökväg.StartsWith(
                                folderPath.EndsWith(Path.DirectorySeparatorChar) ? folderPath : folderPath + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase)),

                    _ => -1
                };
            }

            /// <summary>
            /// Collects the current app-tracked paths for <paramref name="folder"/>
            /// so they can be stored as the sync baseline. The semantics are the
            /// same as <see cref="CountAppFiles"/> but returns the individual paths
            /// instead of a count, enabling exact set-based change detection.
            /// </summary>
            private static List<string> CollectSyncedPaths(FolderData folder, Model.ProjectData project)
            {
                string folderPath = folder.Path;
                return folder.Mode switch
                {
                    SyncFolderMode.ProjectFiles =>
                        project.StoredFiles
                            .Where(f => f.IsFromFolder
                                && string.Equals(f.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase))
                            .Select(f => f.Sökväg)
                            .ToList(),

                    SyncFolderMode.AttachedFiles =>
                        project.StoredFiles
                            .Where(f => f.IsAppendedFile && f.IsFromFolder
                                && string.Equals(f.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase))
                            .Select(f => f.Sökväg)
                            .ToList(),

                    SyncFolderMode.OtherFiles =>
                        project.StoredFiles
                            .SelectMany(f => f.OtherFiles)
                            .Where(o => o.IsFromFolder
                                && string.Equals(o.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase))
                            .Select(o => o.Filepath)
                            .ToList(),

                    SyncFolderMode.VersionDelivery =>
                        project.StoredFiles
                            .SelectMany(f => f.Versions)
                            .Where(v => v.Sökväg.StartsWith(
                                folderPath.EndsWith(Path.DirectorySeparatorChar)
                                    ? folderPath
                                    : folderPath + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase))
                            .Select(v => v.Sökväg)
                            .ToList(),

                    _ => []
                };
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

                // Compute new states on this background thread (pure filesystem reads).
                // Apply them on the UI thread together with the status-message/tree update
                // to avoid blocking a thread-pool callback on the UI dispatcher.
                var computed = affected
                    .Select(p => (Project: p, State: ComputeSharedSyncState(p)))
                    .ToList();

                foreach (var (project, state) in computed)
                    System.Diagnostics.Debug.WriteLine($"[SharedSync] Computed: '{project.Namn}' → {state} (LastPushed={project.LastPushedUtc:O})");

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    foreach (var (project, state) in computed)
                    {
                        project.SharedSyncStatus = state;
                        if (state == SharedSyncState.ServerAhead)
                            PreviewVM.StatusMessage = $"\"{project.Namn}\" has updates on the server";
                        else if (state == SharedSyncState.Conflicted)
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
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => project.SharedSyncStatus = newState);
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
