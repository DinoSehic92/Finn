using Avalonia.Controls;
using Finn.Model;
using Finn.Services;
using Finn.Storage;
using Finn.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Finn.ViewModels
    {
        /// <summary>
        /// Main view model for the application, handles core logic and state.
        /// </summary>
        public partial class MainViewModel : ViewModelBase
        {
            // Constants for magic strings
            private const string ALL_TYPES = "All Types";
            private const string NEW_TYPE = "New";
            private const string SEARCH_CATEGORY = "Search";
            private const string PROJECT_CATEGORY = "Project";
            private const string TOTAL_PROJECT = "Total";
            private const string PDF_TYPE = "PDF";
            private const string OTHER_FILES_TYPE = "Other Files";
            private const string VERSIONS_TYPE = "Versions";
            private const string DRAWING_TYPE = "Drawing";
            private const string DOCUMENT_TYPE = "Document";
            public static string SavePath { get; } = ResolveSavePath();

            private static string ResolveSavePath()
            {
                const string defaultPath = @"C:\Finn";
                if (OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(defaultPath);
                    return defaultPath;
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Finn");
            }

            public MainViewModel()
            {
                NewProject("New Project");
                SetProjectlist();
                SetProject("New Project");
                SetDefaultType();

                Calendar = new CalendarViewModel(() => UI);
                Calendar.DataChanged += () => MarkDirty();
                Collections = new CollectionsViewModel(
                    () => Storage,
                    () => PreviewVM,
                    () => CurrentFiles,
                    () => CurrentFile,
                    MarkDirty);
                Data = new DataViewModel(
                    () => Storage,
                    () => CurrentFiles,
                    () => FilteredFiles,
                    SavePath,
                    MarkDirty);
            }

            private PreviewViewModel _previewVM = new();
            /// <summary>
            /// Gets or sets the preview view model.
            /// </summary>
            public PreviewViewModel PreviewVM
            {
                get => _previewVM;
                set { _previewVM = value; OnPropertyChanged(nameof(PreviewVM)); }
            }

            // CalendarStorage moved into CalendarViewModel

            // Data operations (content indexing, thumbnails, metadata) managed by DataViewModel
            private DataViewModel _data;
            public DataViewModel Data
            {
                get => _data;
                set { _data = value; OnPropertyChanged(nameof(Data)); }
            }

            private ObservableCollection<string> favorites = new() { "Default" };
            public ObservableCollection<string> Favorites
            {
                get { return favorites; }
                set { favorites = value; OnPropertyChanged(nameof(Favorites)); }
            }

            // Collections, bookmarks, and favorites are managed by CollectionsViewModel
            private CollectionsViewModel _collections;
            public CollectionsViewModel Collections
            {
                get => _collections;
                set { _collections = value; OnPropertyChanged(nameof(Collections)); }
            }

            private Window? _previewWindow;
            public Window? PreviewWindow
            {
                get => _previewWindow;
                set => _previewWindow = value;
            }

            private ObservableCollection<string> groups = new();
            public ObservableCollection<string> Groups
            {
                get { return groups; }
                set { groups = value; OnPropertyChanged(nameof(Groups)); }
            }

            public string ProjectMessage { get; set; } = "";
            public bool Confirmed { get; set; }

            private ProjectStorage storage = new();
            public ProjectStorage Storage
            {
                get { return storage; }
                set { storage = value; OnPropertyChanged(nameof(Storage)); }
            }

            // Folder watcher service for detecting file changes in sync folders
            private readonly FolderWatcherService _folderWatcher = new();
            private readonly Services.SharedFileWatcherService _sharedWatcher = new();
            private readonly object _folderSnapshotLock = new();

            /// <summary>
            /// Set to true while we are actively writing the server file (push).
            /// The shared-file watcher checks this to ignore self-triggered events.
            /// </summary>
            private volatile bool _suppressSharedWatcher;

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
                    catch { continue; }

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
                System.Diagnostics.Debug.WriteLine($"[SharedSync] OnSharedFileChanged: {changedFiles.Count} files, suppress={_suppressSharedWatcher}");
                foreach (var f in changedFiles)
                    System.Diagnostics.Debug.WriteLine($"[SharedSync]   changed: {f}");

                // Ignore events triggered by our own push
                if (_suppressSharedWatcher) return;

                // Snapshot projects on this thread-pool thread
                List<ProjectData> affected;
                lock (_folderSnapshotLock)
                {
                    affected = [];
                    foreach (var project in Storage.StoredProjects)
                    {
                        bool hasPath = !string.IsNullOrEmpty(project.SharedPath);
                        bool match = hasPath && changedFiles.Contains(project.SharedPath);
                        System.Diagnostics.Debug.WriteLine($"[SharedSync]   project '{project.Namn}' path='{project.SharedPath}' match={match}");
                        if (match)
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

            /// <summary>
            /// Computes the sync state of a shared project by comparing
            /// <see cref="ProjectData.LastPushedUtc"/> against the server file timestamp.
            /// Preserves <see cref="SharedSyncState.LocalAhead"/> by upgrading to
            /// <see cref="SharedSyncState.Conflicted"/> when the server also changed.
            /// Safe to call from any thread (only reads filesystem + project fields).
            /// </summary>
            public static void UpdateSharedSyncStatus(ProjectData project)
            {
                if (string.IsNullOrEmpty(project.SharedPath))
                {
                    project.SharedSyncStatus = SharedSyncState.Unknown;
                    return;
                }

                try
                {
                    if (!File.Exists(project.SharedPath))
                    {
                        project.SharedSyncStatus = SharedSyncState.ServerMissing;
                        return;
                    }

                    if (project.LastPushedUtc == null)
                    {
                        // Never pushed — server file exists from someone else
                        project.SharedSyncStatus = SharedSyncState.ServerAhead;
                        return;
                    }

                    var serverModified = File.GetLastWriteTimeUtc(project.SharedPath);
                    bool serverChanged = serverModified > project.LastPushedUtc.Value.AddSeconds(5);

                    // Viewers can only pull, so they're either InSync or ServerAhead.
                    if (project.IsViewer)
                    {
                        project.SharedSyncStatus = serverChanged
                            ? SharedSyncState.ServerAhead
                            : SharedSyncState.InSync;
                        return;
                    }

                    bool localDirty = project.SharedSyncStatus is SharedSyncState.LocalAhead
                                                                or SharedSyncState.Conflicted;

                    if (serverChanged && localDirty)
                        project.SharedSyncStatus = SharedSyncState.Conflicted;
                    else if (serverChanged)
                        project.SharedSyncStatus = SharedSyncState.ServerAhead;
                    else if (localDirty)
                        project.SharedSyncStatus = SharedSyncState.LocalAhead;
                    else
                        project.SharedSyncStatus = SharedSyncState.InSync;
                }
                catch
                {
                    project.SharedSyncStatus = SharedSyncState.Unknown;
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

                await Task.Run(() =>
                {
                    foreach (var project in sharedProjects)
                        UpdateSharedSyncStatus(project);
                });

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

            // Calendar viewmodel extracted to keep calendar logic separate
            private CalendarViewModel _calendar;
            public CalendarViewModel Calendar
            {
                get => _calendar;
                set { _calendar = value; OnPropertyChanged(nameof(Calendar)); }
            }

            // Track the path of the currently loaded/saved Projects file so comparisons use the
            // correct file when the user loads or saves to a custom location.
            private string? _currentProjectsFilePath;
            public string? CurrentProjectsFilePath
            {
                get => _currentProjectsFilePath;
                set { _currentProjectsFilePath = value; OnPropertyChanged(nameof(CurrentProjectsFilePath)); }
            }

            // Runtime UI settings viewmodel (separate from persisted DTO)
            private UISettingsViewModel _ui = new UISettingsViewModel();
            public UISettingsViewModel UI
            {
                get => _ui;
                set
                {
                    _ui = value;
                    OnPropertyChanged(nameof(UI));
                }
            }

            private List<string> projectList = new();
            public List<string> ProjectList
            {
                get { return projectList; }
                set { projectList = value; OnPropertyChanged(nameof(ProjectList)); }
            }

            private ProjectData currentProject;
            public ProjectData CurrentProject
            {
                get { return currentProject; }
                set { currentProject = value; OnPropertyChanged(nameof(CurrentProject)); OnPropertyChanged(nameof(IsSearchResult)); ScheduleFilterUpdate(); }
            }

            private string type = null;
            public string Type
            {
                get { return type; }
                set { type = value; OnPropertyChanged(nameof(Type)); ScheduleFilterUpdate(); }
            }

            private BulkObservableCollection<FileData> filteredFiles = new();
            public BulkObservableCollection<FileData> FilteredFiles
            {
                get { return filteredFiles; }
                set { filteredFiles = value; OnPropertyChanged(nameof(FilteredFiles)); OnPropertyChanged(nameof(NrFilteredFiles)); }
            }

            public int NrFilteredFiles => FilteredFiles?.Count(f => f.IsRegularFile) ?? 0;
            public int NrSelectedFiles => CurrentFiles?.Count ?? 0;

            private IList<FileData> currentFiles = null;
            public IList<FileData> CurrentFiles
            {
                get { return currentFiles; }
                set
                {
                    currentFiles = value;
                    ClearSelectedVersion();
                    OnPropertyChanged(nameof(CurrentFiles));
                    OnPropertyChanged(nameof(CurrentFile));
                    OnPropertyChanged(nameof(OtherFilesOwner));
                    OnPropertyChanged(nameof(NrSelectedFiles));
                    OnPropertyChanged(nameof(FileSelected));
                    OnPropertyChanged(nameof(AllSelectedFilesHaveVersions));
                    OnPropertyChanged(nameof(SelectedFileIsTopLevel));
                    OnPropertyChanged(nameof(SelectedFileIsChild));
                    OnPropertyChanged(nameof(SelectedFileIsGroup));
                    OnPropertyChanged(nameof(HasAvailableGroups));
                    OnPropertyChanged(nameof(CanMoveSelectedFiles));
                    OnPropertyChanged(nameof(SelectedFileIsLocal));
                    OnPropertyChanged(nameof(CanReplaceSelectedFiles));
                    OnPropertyChanged(nameof(CanCacheSelectedFiles));
                    OnPropertyChanged(nameof(CanCategorizeSelectedFiles));
                    OnPropertyChanged(nameof(HasAvailableParents));
                    OnPropertyChanged(nameof(SelectedFileIsNotSketch));
                }
            }

            public FileData CurrentFile => CurrentFiles?.LastOrDefault();
            public bool FileSelected => CurrentFile != null;

            /// <summary>
            /// The file whose OtherFiles should be displayed in the tray.
            /// When an appended child is selected, this returns its parent so
            /// the parent's other-file attachments remain visible.
            /// </summary>
            public FileData? OtherFilesOwner =>
                CurrentFile is { IsChild: true, ParentFile: { } parent } ? parent : CurrentFile;

            public bool AllSelectedFilesHaveVersions =>
                CurrentFiles != null && CurrentFiles.Count > 0 && CurrentFiles.All(f => f.HasVersions);

            /// <summary>
            /// True when the current selection is a top-level file (not an appended child).
            /// Used to hide context menu items that don't apply to appended files.
            /// </summary>
            public bool SelectedFileIsTopLevel =>
                CurrentFile?.IsTopLevel == true;

            /// <summary>
            /// True when the current selection is a child file (inside a group or attached).
            /// Shows the "Detach" context menu item.
            /// </summary>
            public bool SelectedFileIsChild =>
                CurrentFile?.IsChild == true;

            /// <summary>
            /// True when the current selection is a group header.
            /// Shows the "Rename Group" context menu item.
            /// </summary>
            public bool SelectedFileIsGroup =>
                CurrentFile != null && CurrentFile.IsGroup;

            /// <summary>
            /// True when there are parent targets in the project to move files into.
            /// </summary>
            public bool HasAvailableGroups => HasAvailableParents;

            /// <summary>
            /// True when the Category menu should be shown.
            /// Hidden for appended files and sketches (sketches have a
            /// fixed category that should not be changed).
            /// </summary>
            public bool CanCategorizeSelectedFiles =>
                CurrentFile != null && CurrentFile.IsTopLevel && !CurrentFile.IsSketch;

            /// <summary>
            /// True when the selected file is a real file (not a sketch).
            /// Used to hide Open, Clipboard, Data, Watermark for sketches
            /// since they reference a shared blank PDF with no real file path.
            /// </summary>
            public bool SelectedFileIsNotSketch =>
                CurrentFile != null && !CurrentFile.IsSketch;

            /// <summary>
            /// True when the selected files can be moved to another project.
            /// Synced files (from a sync folder) cannot be moved because it would
            /// break the folder's tracked file count.
            /// </summary>
            public bool CanMoveSelectedFiles =>
                CurrentFiles != null && CurrentFiles.Count > 0
                && CurrentFiles.All(f => f.IsTopLevel && !f.IsFromFolder);

            /// <summary>
            /// True when the selected file is local (path starts with C:).
            /// Used to hide Rename for non-local files.
            /// </summary>
            public bool SelectedFileIsLocal =>
                CurrentFile != null && CurrentFile.IsLocal();

            /// <summary>
            /// True when Replace makes sense for the selected files.
            /// Synced files should not be replaced because the sync would
            /// revert them on the next run. Sketches cannot be replaced
            /// because they share a single blank PDF canvas.
            /// </summary>
            public bool CanReplaceSelectedFiles =>
                CurrentFiles != null && CurrentFiles.Count > 0
                && CurrentFiles.All(f => !f.IsFromFolder && !f.IsSketch);

            /// <summary>
            /// True when caching makes sense for the selected files.
            /// Sketches are local files and should not be cached.
            /// </summary>
            public bool CanCacheSelectedFiles =>
                CurrentFiles != null && CurrentFiles.Count > 0
                && CurrentFiles.All(f => !f.IsSketch);

            private FileVersionData? selectedVersion;
            /// <summary>
            /// The version currently being previewed, or null when viewing the main file.
            /// </summary>
            public FileVersionData? SelectedVersion
            {
                get => selectedVersion;
                set
                {
                    if (selectedVersion == value) return;
                    selectedVersion = value;
                    OnPropertyChanged(nameof(SelectedVersion));
                    OnPropertyChanged(nameof(IsViewingVersion));
                    if (value != null)
                        SelectedVersionLabel = value.Label;
                }
            }

            /// <summary>
            /// True when a specific version is selected for preview.
            /// </summary>
            public bool IsViewingVersion => selectedVersion != null;

            public void ClearSelectedVersion()
            {
                SelectedVersion = null;
                PreviewVM.VersionSourceFile = null;
            }

            /// <summary>
            /// Predefined revision labels available for version tagging.
            /// </summary>
            public static IReadOnlyList<string> VersionLabels => Finn.Model.FileVersionData.VersionLabels;

            private string _selectedVersionLabel = "NEW";
            /// <summary>
            /// The label currently shown in the version ComboBox.
            /// When a version is selected, changing this also updates that version's label.
            /// </summary>
            public string SelectedVersionLabel
            {
                get => _selectedVersionLabel;
                set
                {
                    if (_selectedVersionLabel == value) return;
                    _selectedVersionLabel = value;
                    OnPropertyChanged(nameof(SelectedVersionLabel));
                    if (selectedVersion != null && !string.IsNullOrEmpty(value))
                        CurrentFile?.SetVersionLabel(selectedVersion, value);
                }
            }

            private bool previewWindowOpen = false;
            public bool PreviewWindowOpen
            {
                get { return previewWindowOpen; }
                set
                {
                    if (previewWindowOpen == value) return;
                    previewWindowOpen = value;
                    OnPropertyChanged(nameof(PreviewWindowOpen));
                    if (previewWindowOpen) UI.PreviewEmbeddedOpen = false;
                }
            }

            private bool previewEmbeddedOpen = false;
            public bool PreviewEmbeddedOpen
            {
                get => previewEmbeddedOpen;
                set { previewEmbeddedOpen = value; OnPropertyChanged(nameof(PreviewEmbeddedOpen)); }
            }

            private string searchText = string.Empty;
            public string SearchText
            {
                get { return searchText; }
                set { searchText = value; OnPropertyChanged(nameof(SearchText)); }
            }

            /// <summary>
            /// True when the current view shows search results rather than a real stored project.
            /// </summary>
            public bool IsSearchResult => CurrentProject?.Category == SEARCH_CATEGORY;

            private bool indexedSearch = false;
            public bool IndexedSearch
            {
                get { return indexedSearch; }
                set { indexedSearch = value; OnPropertyChanged(nameof(IndexedSearch)); }
            }

            public void ResetPreviewer()
            {
                PreviewVM.FileWorkerBusy = false;
            }

            public void SyncPreviewRegionColor()
            {
                // Use UI viewmodel for theme colors
                var color = UI.DarkMode ? UI.Color1 : UI.Color3;
                PreviewVM.UpdateThemeRegionColor(color);
            }


            #region Dirty Tracking

            private bool _isDirty;
            public bool IsDirty
            {
                get => _isDirty;
                set { _isDirty = value; OnPropertyChanged(nameof(IsDirty)); }
            }

            public void MarkDirty()
            {
                IsDirty = true;
                // Flag the current shared project as locally ahead so
                // the tree icon hints that a push may be needed.
                // Only triggers a tree rebuild on the InSync→LocalAhead
                // transition, not on every subsequent MarkDirty call.
                // Viewers never push, so don't flag them as locally ahead.
                if (currentProject is { IsShared: true, IsViewer: false, SharedSyncStatus: SharedSyncState.InSync })
                {
                    currentProject.SharedSyncStatus = SharedSyncState.LocalAhead;
                    BuildTreeData();
                }
            }

            public void ClearDirty()
            {
                IsDirty = false;
            }

            // ── Passive edit tracking ─────────────────────────────────────
            // Properties edited via two-way data binding (Note, Meta_*, etc.)
            // bypass explicit MarkDirty calls. We subscribe to PropertyChanged
            // on the active file/project and forward relevant changes.

            private FileData? _trackedFile;
            private ProjectData? _trackedProject;

            /// <summary>
            /// Properties on <see cref="FileData"/> whose changes should mark the
            /// project as dirty and potentially out of sync with the server.
            /// Covers the file notepad, DataGrid inline edits, and property changes
            /// from context-menu actions that don't already call MarkDirty.
            /// </summary>
            private static readonly HashSet<string> TrackedFileProperties =
            [
                nameof(FileData.Note),
                nameof(FileData.Tagg),
                nameof(FileData.Färg),
                nameof(FileData.Filtyp),
                nameof(FileData.DefaultPage),
                nameof(FileData.IsCached),
                nameof(FileData.Handling),
                nameof(FileData.Status),
                nameof(FileData.Datum),
                nameof(FileData.Ritningstyp),
                nameof(FileData.Beskrivning1),
                nameof(FileData.Beskrivning2),
                nameof(FileData.Beskrivning3),
                nameof(FileData.Beskrivning4),
                nameof(FileData.Revidering),
            ];

            /// <summary>
            /// Properties on <see cref="ProjectData"/> whose changes should mark
            /// the project as dirty. Column visibility toggles (Meta_*) are the
            /// primary case.
            /// </summary>
            private static bool IsTrackedProjectProperty(string name)
                => name.StartsWith("Meta_", StringComparison.Ordinal)
                || name == nameof(ProjectData.ReviewFolder);

            /// <summary>
            /// Call from the view's <c>InitStartup</c> after <c>_ctx</c> is set
            /// to begin tracking passive edits on the current file and project.
            /// </summary>
            public void StartPassiveEditTracking()
            {
                PropertyChanged += OnSelfPropertyChanged;
                SubscribeTrackedProject(currentProject);
                SubscribeTrackedFile(CurrentFile);
            }

            private void OnSelfPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName == nameof(CurrentFile))
                    SubscribeTrackedFile(CurrentFile);
                else if (e.PropertyName == nameof(CurrentProject))
                    SubscribeTrackedProject(currentProject);
            }

            private void SubscribeTrackedFile(FileData? file)
            {
                if (_trackedFile != null)
                    _trackedFile.PropertyChanged -= OnTrackedFilePropertyChanged;
                _trackedFile = file;
                if (_trackedFile != null)
                    _trackedFile.PropertyChanged += OnTrackedFilePropertyChanged;
            }

            private void SubscribeTrackedProject(ProjectData? project)
            {
                if (_trackedProject != null)
                    _trackedProject.PropertyChanged -= OnTrackedProjectPropertyChanged;
                _trackedProject = project;
                if (_trackedProject != null)
                    _trackedProject.PropertyChanged += OnTrackedProjectPropertyChanged;
            }

            private void OnTrackedFilePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName != null && TrackedFileProperties.Contains(e.PropertyName))
                    MarkDirty();
            }

            private void OnTrackedProjectPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            {
                if (e.PropertyName != null && IsTrackedProjectProperty(e.PropertyName))
                    MarkDirty();
            }

            #endregion

            #region View Signals

            /// <summary>Raised when DataGrid columns need to be refreshed (project switch, search, type change).</summary>
            public event Action? ColumnsChanged;
            internal void SignalColumnsChanged() => ColumnsChanged?.Invoke();

            /// <summary>Raised when the tree view needs to be rebuilt.</summary>
            public event Action? TreeViewUpdateRequested;
            internal void SignalTreeViewUpdate() => TreeViewUpdateRequested?.Invoke();

            /// <summary>Raised when the font family or size changes.</summary>
            public event Action? FontChanged;
            internal void SignalFontChanged() => FontChanged?.Invoke();

            #endregion

            #region Keyboard Shortcuts

            public static IReadOnlyList<(string Key, string Description)> KeyboardShortcuts { get; } = new[]
            {
                ("Ctrl+S", "Save"),
                ("Ctrl+Q", "Toggle Treeview"),
                ("Ctrl+E", "Toggle Tray"),
                ("Ctrl+L", "Toggle Folders"),
                ("Ctrl+T", "Toggle Thumbnails"),
                ("Ctrl+W", "Toggle Preview Window"),
                ("Ctrl+P", "Toggle Embedded Preview"),
                ("Ctrl+I", "Toggle Icons"),
                ("Ctrl+M", "Toggle Dark/Light Mode"),
                ("Ctrl+K", "Toggle Calendar"),
                ("Enter",  "Search (in search box)"),
            };

            private bool _shortcutsOpen;
            public bool ShortcutsOpen
            {
                get => _shortcutsOpen;
                set { _shortcutsOpen = value; OnPropertyChanged(nameof(ShortcutsOpen)); }
            }

            #endregion
        }
    }
