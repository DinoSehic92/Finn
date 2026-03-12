using Avalonia.Controls;
using Finn.Model;
using Finn.Storage;
using Finn.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Finn.ViewModels
    {
        /// <summary>
        /// Main view model for the application, handles core logic and state.
        /// </summary>
        public partial class MainViewModel : ViewModelBase, INotifyPropertyChanged
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
                set { currentProject = value; OnPropertyChanged(nameof(CurrentProject)); OnPropertyChanged(nameof(IsSearchResult)); UpdateFilter(); }
            }

            private string type = null;
            public string Type
            {
                get { return type; }
                set { type = value; OnPropertyChanged(nameof(Type)); UpdateFilter(); }
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
                    OnPropertyChanged("FiletypesTree");
                    OnPropertyChanged(nameof(CurrentFiles));
                    OnPropertyChanged(nameof(CurrentFile));
                    OnPropertyChanged(nameof(OtherFilesOwner));
                    OnPropertyChanged(nameof(NrSelectedFiles));
                    OnPropertyChanged(nameof(FileSelected));
                    OnPropertyChanged(nameof(AllSelectedFilesHaveVersions));
                    OnPropertyChanged(nameof(SelectedFileIsTopLevel));
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


            private FileData diffFileA;
            public FileData DiffFileA
            {
                get => diffFileA;
                set
                {
                    diffFileA = value;
                    OnPropertyChanged(nameof(DiffFileA));
                    OnPropertyChanged(nameof(DiffFileAName));
                    OnPropertyChanged(nameof(DiffReady));
                }
            }

            private FileData diffFileB;
            public FileData DiffFileB
            {
                get => diffFileB;
                set
                {
                    diffFileB = value;
                    OnPropertyChanged(nameof(DiffFileB));
                    OnPropertyChanged(nameof(DiffFileBName));
                    OnPropertyChanged(nameof(DiffReady));
                }
            }

            public string DiffFileAName => DiffFileA?.Namn ?? "—";
            public string DiffFileBName => DiffFileB?.Namn ?? "—";
            public bool DiffReady => DiffFileA?.IsValidPdf() == true && DiffFileB?.IsValidPdf() == true;

            public void SetDiffFileA()
            {
                if (CurrentFile != null)
                    DiffFileA = CurrentFile;
            }

            public void SetDiffFileB()
            {
                if (CurrentFile != null)
                    DiffFileB = CurrentFile;
            }

            public void ClearDiffFileA() => DiffFileA = null;
            public void ClearDiffFileB() => DiffFileB = null;

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
