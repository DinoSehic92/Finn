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
                if (OperatingSystem.IsWindows())
                {
                    // Prefer C:\Finn for easy discoverability, but fall back to
                    // %AppData%\Finn when C:\ is not writable (locked-down machines,
                    // standard user accounts, etc.).
                    const string preferredPath = @"C:\Finn";
                    try
                    {
                        Directory.CreateDirectory(preferredPath);
                        // Verify we can actually write there (CreateDirectory succeeds
                        // even if the directory already exists but is read-only).
                        string probe = Path.Combine(preferredPath, ".write_probe");
                        File.WriteAllText(probe, string.Empty);
                        File.Delete(probe);
                        return preferredPath;
                    }
                    catch
                    {
                        // Fall through to AppData fallback.
                    }
                }

                string fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Finn");
                Directory.CreateDirectory(fallback);
                return fallback;
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
            private DataViewModel _data = null!;
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
            private CollectionsViewModel _collections = null!;
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

            /// <summary>Splash window shown during startup; closed by InitStartup when ready.</summary>
            public Window? SplashWindow { get; set; }

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

            // Calendar viewmodel extracted to keep calendar logic separate
            private CalendarViewModel _calendar = null!;
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

            private ProjectData? currentProject;
            public ProjectData? CurrentProject
            {
                get { return currentProject; }
                set { currentProject = value; InvalidateAvailableParentsCache(); OnPropertyChanged(nameof(CurrentProject)); OnPropertyChanged(nameof(IsSearchResult)); ScheduleFilterUpdate(); }
            }

            private string type = string.Empty;
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

            private IList<FileData> currentFiles = [];
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
                    OnPropertyChanged(nameof(SelectedFileIsDesignatedParent));
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

            public FileData? CurrentFile => CurrentFiles?.LastOrDefault();
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
            /// True when the current selection is a child file (inside a group or attached)
            /// AND is not managed by an AttachedFiles sync folder (those cannot be individually detached).
            /// Shows the "Detach" context menu item.
            /// </summary>
            public bool SelectedFileIsChild =>
                CurrentFile?.IsChild == true && !IsAttachedFolderFile(CurrentFile);

            /// <summary>
            /// True when the current selection is a group header.
            /// Shows the "Rename Group" context menu item.
            /// </summary>
            public bool SelectedFileIsGroup =>
                CurrentFile != null && CurrentFile.IsGroup;

            /// <summary>
            /// True when the current selection is a regular top-level file that has been
            /// explicitly marked as a parent slot via <see cref="FileData.IsDesignatedParent"/>.
            /// Used to toggle the "Mark as Parent" / "Unmark as Parent" menu item.
            /// </summary>
            public bool SelectedFileIsDesignatedParent =>
                CurrentFile?.IsDesignatedParent == true;

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
            /// Blocked when any selected file is:
            /// - a child (appended) file
            /// - from a sync folder (would break baseline tracking in the source project)
            /// - a group (groups are project-scoped; their children would lose sync folder tracking)
            /// - a designated parent or regular parent that has synced children
            ///   (the children would move silently and orphan the source folder's baseline)
            /// </summary>
            public bool CanMoveSelectedFiles =>
                CurrentFiles != null && CurrentFiles.Count > 0
                && CurrentFiles.All(f =>
                    f.IsTopLevel
                    && !f.IsFromFolder
                    && !f.IsGroup
                    && !HasSyncedChildren(f));

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


            #region Error Helpers

            /// <summary>
            /// Logs the exception to disk and surfaces a short message in the
            /// status bar. Use for user-initiated operations (sync, push, import, export)
            /// where silent failure leaves the user wondering what happened.
            /// </summary>
            internal void LogAndNotify(Exception ex, string context, string? userMessage = null)
            {
                Utils.ErrorLogger.Log(ex, context);
                PreviewVM.StatusMessage = userMessage ?? $"{context} failed: {ex.Message}";
            }

            #endregion

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
