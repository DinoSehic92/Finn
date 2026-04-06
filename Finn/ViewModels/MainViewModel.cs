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
                const string legacyPath = @"C:\Finn";
                if (OperatingSystem.IsWindows() && Directory.Exists(legacyPath))
                    return legacyPath;

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
            private readonly object _folderSnapshotLock = new();

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
                    // Subscribe before Refresh so events raised during the
                    // refresh window aren't silently dropped.  The -= / += is
                    // idempotent when the handler is already attached.
                    _folderWatcher.FolderChanged -= OnFolderWatcherChanged;
                    _folderWatcher.FolderChanged += OnFolderWatcherChanged;
                    _folderWatcher.Refresh(Storage.StoredProjects);
                }
                else
                {
                    _folderWatcher.FolderChanged -= OnFolderWatcherChanged;
                    _folderWatcher.StopAll();
                }
            }

            /// <summary>Stops all folder watchers (e.g. on shutdown).</summary>
            public void StopFolderWatchers() => _folderWatcher.Dispose();

            /// <summary>Dismisses the sync notification without syncing.</summary>
            public void DismissFolderSyncNotification()
            {
                PendingSyncFolders.Clear();
                RaiseSyncStatusChanged();
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
                if (entry == null || mainWindow == null) return false;

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

                // Run the sync for this single folder
                bool confirmed = await SyncFolderAsync(folder, mainWindow);

                // Refresh the grid/tree — project-level SyncFolderAsync does this
                // internally, but attached/other-files folders don't, so always
                // call it here to ensure the UI reflects the changes.
                UpdateFilter();
                BuildTreeData();

                if (confirmed)
                {
                    // Remove the entry only when the user accepted the import
                    PendingSyncFolders.Remove(entry);
                    RaiseSyncStatusChanged();
                    MarkDirty();
                }

                return confirmed;
            }

            /// <summary>
            /// Performs a one-time comparison of every sync folder's tracked files
            /// against the actual disk contents. Detects additions and removals
            /// that happened while the app was closed and populates
            /// <see cref="PendingSyncFolders"/> so the toolbar indicator can show them.
            /// </summary>
            public void CheckFolderSyncOnStartup()
            {
                if (!UI.FolderWatchEnabled) return;
                CheckAllFoldersCore();
            }

            /// <summary>
            /// Manually re-checks every sync folder across all projects.
            /// Runs disk I/O on a background thread and updates the UI when done.
            /// <para>
            /// This is a <b>deep check</b>: for project-level and attached-file
            /// folders it compares the actual set of disk files against the
            /// tracked set — not just a count — so it catches additions and
            /// removals even when the net count stays the same.
            /// For folders that are genuinely in sync it heals stale baselines.
            /// </para>
            /// </summary>
            public async Task CheckAllFoldersAsync()
            {
                // Snapshot project/folder pairs on the UI thread.
                // For ProjectFiles / AttachedFiles we also snapshot the tracked
                // file paths so we can do a set comparison on the background thread.
                var pairs = new List<(string ProjectName, FolderData Folder, HashSet<string>? TrackedPaths)>();
                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var folder in project.Folders)
                    {
                        if (!folder.IsValid() || string.IsNullOrEmpty(folder.Path))
                            continue;

                        HashSet<string>? tracked = null;
                        if (folder.Mode is SyncFolderMode.ProjectFiles or SyncFolderMode.AttachedFiles)
                        {
                            tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            string folderPrefix = folder.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar)
                                                + System.IO.Path.DirectorySeparatorChar;
                            foreach (var f in project.StoredFiles)
                            {
                                if (string.IsNullOrEmpty(f.Sökväg))
                                    continue;

                                bool belongsToFolder =
                                    string.Equals(f.SyncFolder, folder.Path, StringComparison.OrdinalIgnoreCase);

                                // Adopt orphaned files whose path lives inside
                                // this folder but were imported before the sync
                                // folder was created (SyncFolder not set).
                                if (!belongsToFolder
                                    && string.IsNullOrEmpty(f.SyncFolder)
                                    && f.Sökväg.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
                                {
                                    AdoptFileIntoFolder(f, folder);
                                    belongsToFolder = true;
                                }

                                if (belongsToFolder)
                                    tracked.Add(f.Sökväg);
                            }
                        }

                        pairs.Add((project.Namn, folder, tracked));
                    }
                }

                // Run the expensive disk I/O on a background thread
                bool baselineUpdated = false;
                var outOfSync = await Task.Run(() =>
                {
                    var results = new List<(string ProjectName, FolderData Folder)>();
                    foreach (var (projectName, folder, tracked) in pairs)
                    {
                        try
                        {
                            bool isOutOfSync;

                            if (tracked != null)
                            {
                                // Deep set-based comparison for ProjectFiles /
                                // AttachedFiles — catches additions and removals
                                // even when the net count stays the same.
                                var (pattern, search) = GetFileFilter(folder);
                                var diskFiles = new HashSet<string>(
                                    Directory.EnumerateFiles(folder.Path, pattern, search),
                                    StringComparer.OrdinalIgnoreCase);

                                isOutOfSync = !diskFiles.SetEquals(tracked);
                            }
                            else
                            {
                                // OtherFiles / VersionDelivery: count + timestamp
                                isOutOfSync = IsFolderOutOfSync(folder);
                            }

                            if (isOutOfSync)
                            {
                                results.Add((projectName, folder));
                            }
                            else
                            {
                                // Folder is genuinely in sync — heal stale baselines
                                // so future startup / watcher checks don't false-positive.
                                int diskCount = CountDiskFiles(folder);
                                DateTime latestFile = GetLatestFileWriteTimeUtc(folder);
                                bool needsHeal = folder.LastSyncedUtc == null
                                    || folder.SyncedFileCount != diskCount
                                    || latestFile > folder.LastSyncedUtc.Value;

                                if (needsHeal)
                                {
                                    folder.SyncedFileCount = diskCount;
                                    folder.LastSyncedUtc = latestFile;
                                    baselineUpdated = true;
                                }
                            }
                        }
                        catch { /* inaccessible folder — skip */ }
                    }
                    return results;
                });

                // Rebuild PendingSyncFolders from scratch so stale entries are removed
                PendingSyncFolders.Clear();
                foreach (var (projectName, folder) in outOfSync)
                {
                    PendingSyncFolders.Add(SyncStatusEntry.FromFolder(folder, projectName));
                }
                RaiseSyncStatusChanged();

                if (baselineUpdated)
                    MarkDirty();
            }

            /// <summary>
            /// Core logic shared by startup check — runs synchronously on the
            /// calling thread (startup is fine since it's before the UI is interactive).
            /// </summary>
            private void CheckAllFoldersCore()
            {
                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var folder in project.Folders)
                    {
                        if (!folder.IsValid() || string.IsNullOrEmpty(folder.Path))
                            continue;

                        // Skip if already tracked (avoids duplicates on double-call)
                        bool alreadyTracked = false;
                        foreach (var existing in PendingSyncFolders)
                        {
                            if (string.Equals(existing.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(existing.ProjectName, project.Namn, StringComparison.OrdinalIgnoreCase))
                            {
                                alreadyTracked = true;
                                break;
                            }
                        }
                        if (alreadyTracked) continue;

                        try
                        {
                            if (IsFolderOutOfSync(folder))
                                PendingSyncFolders.Add(SyncStatusEntry.FromFolder(folder, project.Namn));
                        }
                        catch
                        {
                            // Folder may be inaccessible (network share offline, etc.)
                        }
                    }
                }

                RaiseSyncStatusChanged();
            }

            /// <summary>
            /// Checks whether a single folder is out of sync by comparing the
            /// current disk file count against the count stored at last sync.
            /// Falls back to a file-timestamp check for content modifications
            /// that don't change the file count.
            /// Returns false for folders that have never been synced.
            /// </summary>
            private static bool IsFolderOutOfSync(FolderData folder)
            {
                if (folder.LastSyncedUtc == null && folder.SyncedFileCount == 0)
                    return false; // Never synced

                // Primary check: did the number of files on disk change?
                int diskCount = CountDiskFiles(folder);
                if (diskCount != folder.SyncedFileCount)
                    return true;

                // Secondary check: did any file get modified/replaced without
                // affecting the count?  Compare the newest file write-time
                // against the snapshot taken at last sync.
                if (folder.LastSyncedUtc is { } lastSync)
                {
                    DateTime latestFile = GetLatestFileWriteTimeUtc(folder);
                    return latestFile > lastSync;
                }

                return false;
            }

            /// <summary>
            /// Returns the search pattern and <see cref="SearchOption"/> appropriate
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
            /// Returns the most recent <see cref="File.GetLastWriteTimeUtc"/>
            /// across the files in <paramref name="folder"/> that match the
            /// folder's sync filter (e.g. *.pdf for ProjectFiles).
            /// Only inspects the same files that <see cref="CountDiskFiles"/> counts
            /// so the baseline comparison is consistent.
            /// </summary>
            private static DateTime GetLatestFileWriteTimeUtc(FolderData folder)
            {
                var (pattern, search) = GetFileFilter(folder);
                var latest = DateTime.MinValue;
                foreach (var file in Directory.EnumerateFiles(folder.Path, pattern, search))
                {
                    try
                    {
                        var dt = File.GetLastWriteTimeUtc(file);
                        if (dt > latest) latest = dt;
                    }
                    catch { /* inaccessible file — skip */ }
                }
                return latest;
            }

            /// <summary>
            /// Counts the actual files on disk for the given folder.
            /// Uses the same file-type filter as the sync logic for each mode.
            /// Returns <c>-1</c> when the folder no longer exists so callers
            /// can distinguish "missing" from "empty".
            /// </summary>
            private static int CountDiskFiles(FolderData folder)
            {
                if (!folder.IsValid()) return -1;

                var (pattern, search) = GetFileFilter(folder);
                return Directory.EnumerateFiles(folder.Path, pattern, search).Count();
            }

            /// <summary>
            /// Records the current disk state as the sync baseline so that
            /// <see cref="IsFolderOutOfSync"/> won't produce false positives.
            /// Uses the actual latest file write-time from disk rather than
            /// <see cref="DateTime.UtcNow"/> to avoid clock/granularity races.
            /// </summary>
            private static void RecordSyncBaseline(FolderData folder)
            {
                folder.SyncedFileCount = CountDiskFiles(folder);
                folder.LastSyncedUtc = folder.IsValid()
                    ? GetLatestFileWriteTimeUtc(folder)
                    : DateTime.UtcNow;
            }

            private void OnFolderWatcherChanged(IReadOnlySet<string> changedPaths)
            {
                // Match changed watcher paths to folder names across all projects.
                // Only flag folders that are genuinely out of sync — avoids false
                // positives from temp files, metadata writes, etc.
                //
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
                    // Skip folders that aren't actually out of sync
                    try { if (!IsFolderOutOfSync(folder)) continue; }
                    catch { continue; }

                    newEntries.Add(SyncStatusEntry.FromFolder(folder, projectName));
                }

                if (newEntries.Count > 0)
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var entry in newEntries)
                        {
                            // Avoid duplicates — checked on the UI thread where
                            // PendingSyncFolders is safely accessible.
                            bool alreadyTracked = false;
                            foreach (var existing in PendingSyncFolders)
                            {
                                if (string.Equals(existing.FolderPath, entry.FolderPath, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(existing.ProjectName, entry.ProjectName, StringComparison.OrdinalIgnoreCase))
                                {
                                    alreadyTracked = true;
                                    break;
                                }
                            }
                            if (!alreadyTracked)
                                PendingSyncFolders.Add(entry);
                        }
                        RaiseSyncStatusChanged();
                    });
                }
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

            public int NrFilteredFiles => FilteredFiles?.Count(f => !f.IsAppendedFile) ?? 0;
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
                    OnPropertyChanged(nameof(CanMoveSelectedFiles));
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
                CurrentFile is { IsAppendedFile: true, ParentFile: { } parent } ? parent : CurrentFile;

            public bool AllSelectedFilesHaveVersions =>
                CurrentFiles != null && CurrentFiles.Count > 0 && CurrentFiles.All(f => f.HasVersions);

            /// <summary>
            /// True when the current selection is a top-level file (not an appended child).
            /// Used to hide context menu items that don't apply to appended files.
            /// </summary>
            public bool SelectedFileIsTopLevel =>
                CurrentFile != null && !CurrentFile.IsAppendedFile;

            /// <summary>
            /// True when the selected files can be moved to another project.
            /// Synced files (from a sync folder) cannot be moved because it would
            /// break the folder's tracked file count.
            /// </summary>
            public bool CanMoveSelectedFiles =>
                CurrentFiles != null && CurrentFiles.Count > 0
                && CurrentFiles.All(f => !f.IsAppendedFile && !f.IsFromFolder);

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
            }

            public void ClearDirty()
            {
                IsDirty = false;
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
