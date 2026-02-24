using Finn.Model;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia.Controls;
using System.IO;
using System.Globalization;
using Finn.Dialog;
using Avalonia.Styling;
using Finn.Views;
using Finn.Storage;
using MuPDFCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Avalonia;
using Avalonia.Platform.Storage;
using Avalonia.Themes.Fluent;
using System.Text;
using System.Diagnostics;
using iText.Kernel.Pdf;
using iText.Kernel.Font;
using iText.Layout;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Pdf.Canvas;
using iText.Layout.Element;
using iText.Kernel.Colors;
using iText.Layout.Properties;

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
            private const string DRAWING_TYPE = "Drawing";
            private const string DOCUMENT_TYPE = "Document";
            public const string SavePath = @"C:\Finn";

            public MainViewModel()
            {
                NewProject("New Project");
                SetProjectlist();
                SetProject("New Project");
                SetDefaultType();

                Calendar = new CalendarViewModel(() => UI);
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

            public List<string[]> MetaStore = new();
            public List<string> PathStore = new();

            private ObservableCollection<string> favorites = new() { "Default" };
            public ObservableCollection<string> Favorites
            {
                get { return favorites; }
                set { favorites = value; OnPropertyChanged(nameof(Favorites)); }
            }

            private string currentCollection = string.Empty;
            public string CurrentCollection
            {
                get { return currentCollection; }
                set { currentCollection = value; OnPropertyChanged(nameof(CurrentCollection)); SetCollectionContent(); }
            }

            private ObservableCollection<FileData> collectionContent = new();
            public ObservableCollection<FileData> CollectionContent
            {
                get { return collectionContent; }
                set { collectionContent = value; OnPropertyChanged(nameof(CollectionContent)); }
            }

            public Window PreviewWindow;

            private ObservableCollection<string> groups = new();
            public ObservableCollection<string> Groups
            {
                get { return groups; }
                set { groups = value; OnPropertyChanged(nameof(Groups)); }
            }

            private PageData favPage;
            public PageData FavPage
            {
                get { return favPage; }
                set { favPage = value; OnPropertyChanged(nameof(FavPage)); TrySetPage(); }
            }

            public string ProjectMessage { get; set; } = "";
            public bool Confirmed = false;

            private bool attachedView = false;
            public bool AttachedView
            {
                get { return attachedView; }
                set { attachedView = value; OnPropertyChanged(nameof(AttachedView)); }
            }

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
                set { _ui = value; OnPropertyChanged(nameof(UI)); }
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
                set { currentProject = value; OnPropertyChanged(nameof(CurrentProject)); UpdateFilter(); }
            }

            private string type = null;
            public string Type
            {
                get { return type; }
                set { type = value; OnPropertyChanged(nameof(Type)); UpdateFilter(); }
            }

            private ObservableCollection<FileData> filteredFiles = new();
            public ObservableCollection<FileData> FilteredFiles
            {
                get { return filteredFiles; }
                set { filteredFiles = value; OnPropertyChanged(nameof(FilteredFiles)); OnPropertyChanged(nameof(NrFilteredFiles)); }
            }

            private ObservableCollection<ContentData> textContent = null;
            public ObservableCollection<ContentData> TextContent
            {
                get { return textContent; }
                set { textContent = value; OnPropertyChanged(nameof(TextContent)); }
            }

            private ContentData selectedTextContent = null;
            public ContentData SelectedTextContent
            {
                get { return selectedTextContent; }
                set { selectedTextContent = value; OnPropertyChanged(nameof(SelectedTextContent)); }
            }

            public int NrFilteredFiles => FilteredFiles?.Count ?? 0;
            public int NrSelectedFiles => CurrentFiles?.Count ?? 0;

            private IList<FileData> currentFiles = null;
            public IList<FileData> CurrentFiles
            {
                get { return currentFiles; }
                set
                {
                    currentFiles = value;
                    OnPropertyChanged("FiletypesTree");
                    OnPropertyChanged(nameof(CurrentFiles));
                    OnPropertyChanged(nameof(CurrentFile));
                    OnPropertyChanged(nameof(NrSelectedFiles));
                    OnPropertyChanged(nameof(FileSelected));
                }
            }

            public FileData CurrentFile => CurrentFiles?.LastOrDefault();
            public bool FileSelected => CurrentFile != null;

            private bool previewWindowOpen = false;
            public bool PreviewWindowOpen
            {
                get { return previewWindowOpen; }
                set { previewWindowOpen = value; OnPropertyChanged(nameof(PreviewWindowOpen)); if (PreviewWindowOpen) { UI.PreviewEmbeddedOpen = false; }; }
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

            private bool indexedSearch = false;
            public bool IndexedSearch
            {
                get { return indexedSearch; }
                set { indexedSearch = value; OnPropertyChanged(nameof(IndexedSearch)); }
            }

            private FolderData currentFolder;
            public FolderData CurrentFolder
            {
                get { return currentFolder; }
                set { currentFolder = value; OnPropertyChanged(nameof(CurrentFolder)); }
            }

            // Helper method for common window setup
            private void ConfigureWindow(Window window, Window mainWindow)
            {
                window.DataContext = this;
                window.FontFamily = mainWindow.FontFamily;
                window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
                window.Focusable = true;
            }

            public void NewFolder()
            {
                CurrentProject.Folders.Add(new FolderData() { Name = "New Folder" });
            }

            public void NewFileFolder()
            {
                if (CurrentFile != null)
                {
                    CurrentProject.Folders.Add(new FolderData() { Name = "New file folder", AttachToFile = CurrentFile.Namn, AttachToFilePath = CurrentFile.Sökväg });
                }
            }

            public void RemoveFolder()
            {
                if (CurrentFolder != null)
                {
                    if (CurrentFolder.AttachToFile == null)
                    {
                        foreach (FileData file in CurrentProject.StoredFiles.Where(x => x.IsFromFolder).Where(x => x.SyncFolder == CurrentFolder.Path).ToList())
                        {
                            CurrentProject.StoredFiles.Remove(file);
                        }

                        UpdateFilter();
                        CurrentProject.Folders.Remove(CurrentFolder);
                        OnPropertyChanged("TreeViewUpdate");
                    }
                    else
                    {
                        FileData file = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == CurrentFolder.AttachToFile);

                        if (file != null)
                        {
                            if (CurrentFolder.Types == PDF_TYPE)
                            {
                                foreach (FileData fileToRemove in file.AppendedFiles.Where(x => x.SyncFolder == CurrentFolder.Path).ToList())
                                {
                                    file.AppendedFiles.Remove(fileToRemove);
                                }
                                SortAttachedFilesDirect(file);
                            }

                            if (CurrentFolder.Types == OTHER_FILES_TYPE)
                            {
                                foreach (OtherData fileToRemove in file.OtherFiles.Where(x => x.SyncFolder == CurrentFolder.Path).ToList())
                                {
                                    file.OtherFiles.Remove(fileToRemove);
                                }
                                SortOtherFilesDirect(file);
                            }
                        }

                        CurrentProject.Folders.Remove(CurrentFolder);
                    }
                }
            }

            public void SyncAllFolders()
            {
                foreach (FolderData folder in CurrentProject.Folders)
                {
                    SyncFolder(folder);
                }
            }

            public void SyncSelectedFolder()
            {
                SyncFolder(CurrentFolder);
            }

            public void SyncFile()
            {
                if (CurrentFile != null)
                {
                    foreach (FolderData folder in CurrentProject.Folders.Where(x => x.AttachToFilePath == CurrentFile.Sökväg))
                    {
                        SyncFolder(folder);
                    }
                }
            }

            public void SyncFolder(FolderData folder)
            {
                if (folder?.IsValid() != true || folder.Path == null)
                {
                    return;
                }

                if (folder.AttachToFile != null)
                {
                    FileData file = CurrentProject.StoredFiles.FirstOrDefault(x => x.Sökväg == folder.AttachToFilePath);
                    CurrentFiles = CurrentProject.StoredFiles.Where(x => x.Sökväg == folder.AttachToFilePath).ToList();

                    if (file != null)
                    {
                        if (folder.Types == PDF_TYPE)
                        {
                            foreach (FileData fileToRemove in file.AppendedFiles.Where(x => x.SyncFolder == folder.Path).ToList())
                            {
                                CurrentFile.AppendedFiles.Remove(fileToRemove);
                            }

                            foreach (FileData fileToAdd in GetFilesFromFolder(folder))
                            {
                                CurrentFile.AppendedFiles.Add(fileToAdd);
                            }

                            SortAttachedFiles();
                        }

                        if (folder.Types == OTHER_FILES_TYPE)
                        {
                            foreach (OtherData fileToRemove in file.OtherFiles.Where(x => x.SyncFolder == folder.Path).ToList())
                            {
                                CurrentFile.OtherFiles.Remove(fileToRemove);
                            }

                            foreach (OtherData fileToAdd in GetOtherFilesFromFolder(folder))
                            {
                                CurrentFile.OtherFiles.Add(fileToAdd);
                            }

                            SortOtherFiles();
                        }
                    }
                }
                else
                {
                    List<FileData> files = GetFilesFromFolder(folder);

                    IEnumerable<FileData> existingFiles = CurrentProject.StoredFiles.Where(x => x.IsFromFolder).Where(x => x.SyncFolder == folder.Path);
                    IEnumerable<FileData> filesToRemove = existingFiles.Where(p => !files.Any(p2 => p2.Sökväg == p.Sökväg)).ToList();
                    IEnumerable<FileData> filesToAdd = files.Where(p => !existingFiles.Any(p2 => p2.Sökväg == p.Sökväg)).ToList();

                    foreach (FileData file in filesToRemove)
                    {
                        CurrentProject.StoredFiles.Remove(file);
                    }
                    foreach (FileData file in filesToAdd)
                    {
                        CurrentProject.StoredFiles.Add(file);
                    }

                    SetDefaultType();
                    OnPropertyChanged("TreeViewUpdate");
                }
            }

            private List<FileData> GetFilesFromFolder(FolderData folder)
            {
                List<FileData> files = new();

                if (folder.IsValid())
                {
                    foreach (string path in Directory.GetFiles(folder.Path))
                    {
                        if (Path.GetExtension(path) == ".pdf")
                        {
                            files.Add(new FileData()
                            {
                                Namn = System.IO.Path.GetFileNameWithoutExtension(path),
                                Sökväg = path,
                                Uppdrag = CurrentProject.Namn,
                                Filtyp = NEW_TYPE,
                                SyncFolder = folder.Path,
                                IsFromFolder = true
                            });
                        }
                    }
                }

                return files;
            }

            private List<OtherData> GetOtherFilesFromFolder(FolderData folder)
            {
                List<OtherData> files = new();

                if (folder.IsValid())
                {
                    foreach (string path in Directory.GetFiles(folder.Path))
                    {
                        OtherData newFile = new()
                        {
                            Name = System.IO.Path.GetFileNameWithoutExtension(path),
                            Filepath = path,
                            SyncFolder = folder.Path,
                            IsFromFolder = true
                        };

                        newFile.SetFile();
                        files.Add(newFile);
                    }
                }

                return files;
            }

            public void AddPlaceholderFile(string name)
            {
                FileData newfile = new()
                {
                    Namn = name,
                    Filtyp = NEW_TYPE,
                    Uppdrag = CurrentProject.Namn,
                    Sökväg = string.Empty
                };

                CurrentProject.StoredFiles.Add(newfile);
                UpdateFilter();
                OnPropertyChanged("TreeViewUpdate");
            }

            public void ResetPreviewer()
            {
                PreviewVM.FileWorkerBusy = false;
            }

            public void OpenWhiteboard(Window mainWindow)
            {
                var window = new xPaintDia();
                ConfigureWindow(window, mainWindow);
                window.Show();
            }

            public void OpenReinforcementCalculator(Window mainWindow)
            {
                var window = new xReinDia();
                ConfigureWindow(window, mainWindow);
                window.Show();
            }

            public void OpenPreviewWindow(ThemeVariant theme)
            {
                PreviewWindow = new PreWindow()
                {
                    DataContext = this,
                    RequestedThemeVariant = theme
                };
                PreviewWindow.Show();
            }

            public void OpenInfoDia(Window mainWindow)
            {
                var window = new xProgDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OpenColorDia(Window mainWindow)
            {
                var window = new xColorDia();
                ConfigureWindow(window, mainWindow);
                window.FontCombo.SelectionChanged += SignalFontChanged;
                window.FontSizeCombo.SelectionChanged += SignalFontChanged;
                window.ShowDialog(mainWindow);
            }

            private void SignalFontChanged(object sender, SelectionChangedEventArgs e)
            {
                OnPropertyChanged("FontChanged");
            }

            public void OpenMetaEditDia(Window mainWindow)
            {
                var window = new xMetaDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OpenProjectEditDia(Window mainWindow)
            {
                var window = new xEditDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OpenProjectNewDia(Window mainWindow)
            {
                var window = new xNewDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
                window.ProjectName.Focus();
            }

            public void OpenTagDia(Window mainWindow)
            {
                var window = new xTagDia();
                ConfigureWindow(window, mainWindow);
                window.TagMenuInput.Text = CurrentFile.Tagg;
                window.TagMenuInput.CaretIndex = window.TagMenuInput.Text.Length;
                window.ShowDialog(mainWindow);
                window.TagMenuInput.Focus();
            }

            public void OpenNewPlaceholderFile(Window mainWindow)
            {
                var window = new xPlaceholderDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
                window.NewFileName.Focus();
            }

            public void TryOpenRenameDia(Window mainWindow)
            {
                if (!CurrentFile.IsLocal())
                {
                    OpenMessageDia(mainWindow);
                }
                else
                {
                    OpenRenameDia(mainWindow);
                }
            }

            public void OpenRenameDia(Window mainWindow)
            {
                var window = new xRenameDia();
                ConfigureWindow(window, mainWindow);
                window.SetCurrentName(CurrentFile.Namn);
                window.NewNameInput.CaretIndex = window.NewNameInput.Text.Length;
                window.ShowDialog(mainWindow);
                window.NewNameInput.Focus();
            }

            public async Task OpenMessageDia(Window mainWindow)
            {
                var window = new xMessageDia();
                ConfigureWindow(window, mainWindow);
                window.SetMessage("Only available for files stored on C:\\");
                await window.ShowDialog(mainWindow);
            }

            public async Task ConfirmDeleteDia(Window mainWindow)
            {
                var window = new xDeleteDia();
                ConfigureWindow(window, mainWindow);
                await window.ShowDialog(mainWindow);
            }

            public void OnInfoDia(Window mainWindow)
            {
                var window = new xInfoDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            public void OnIndexDia(Window mainWindow)
            {
                // Load existing indexed content from disk so the inspect dialog
                // shows previously indexed entries immediately.
                try
                {
                    string indexPath = $"{SavePath}\\Content.json";
                    if (System.IO.File.Exists(indexPath))
                    {
                        LoadIndexFile(indexPath);
                    }
                    else
                    {
                        TextContent ??= new ObservableCollection<ContentData>();
                    }
                }
                catch
                {
                    // If loading fails, ensure collection is non-null so the dialog can bind to it.
                    TextContent ??= new ObservableCollection<ContentData>();
                }

                var window = new xContentDia();
                ConfigureWindow(window, mainWindow);
                window.ShowDialog(mainWindow);
            }

            private void SyncPreviewRegionColor()
            {
                // Use UI viewmodel for theme colors
                var color = UI.DarkMode ? UI.Color1 : UI.Color3;
                PreviewVM.UpdateThemeRegionColor(color);
            }


            public void TrySetPage()
            {
                if (FavPage != null)
                {
                    PreviewVM.RequestPage1 = FavPage.PageNr;
                }
            }

            public void GetThumbnails()
            {
                string thumbnailPath = $"{SavePath}\\Thumbnails\\";
                if (!Directory.Exists(thumbnailPath))
                {
                    Directory.CreateDirectory(thumbnailPath);
                }

                foreach (FileData file in CurrentFiles)
                {
                    if (file.IsValidPdf())
                    {
                        file.RemoveThumbnail();

                        byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);
                        MuPDFDocument fileDocument = new(new MuPDFContext(), bytes, InputFileTypes.PDF);

                        file.ThumbnailSource = $"{thumbnailPath}{file.Namn}.jpeg";
                        fileDocument.SaveImageAsJPEG(0, 1, file.ThumbnailSource, 20);

                        fileDocument.Dispose();
                    }
                }
            }

            public async Task GetContentAsync(IProgress<int>? progress = null)
            {
                string indexPath = $"{SavePath}\\Content.json";

                if (System.IO.File.Exists(indexPath))
                {
                    LoadIndexFile(indexPath);
                }

                TextContent ??= new ObservableCollection<ContentData>();

                var files = CurrentFiles?.ToList() ?? new List<FileData>();
                var results = new List<ContentData>();

                await Task.Run(() =>
                {
                    int total = files.Count;
                    for (int i = 0; i < total; i++)
                    {
                        var file = files[i];
                        try
                        {
                            if (file.IsValidPdf())
                            {
                                byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);
                                using var fileDocument = new MuPDFDocument(new MuPDFContext(), bytes, InputFileTypes.PDF);
                                var content = new ContentData
                                {
                                    Name = file.Namn,
                                    Filepath = file.Sökväg,
                                    PlainText = fileDocument.ExtractText()
                                };

                                lock (results)
                                {
                                    results.Add(content);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore individual failures and continue indexing other files
                        }

                        int percent = (i + 1) * 100 / Math.Max(1, total);
                        progress?.Report(percent);
                    }
                });

                // Update UI-bound collections on the calling (UI) thread after background processing
                foreach (ContentData content in results)
                {
                    if (content.PlainText != null && content.PlainText != string.Empty)
                    {
                        ContentData? existing = TextContent.FirstOrDefault(x => x.Filepath == content.Filepath);
                        if (existing != null)
                        {
                            TextContent.Remove(existing);
                        }
                        TextContent.Add(content);
                    }
                }

                // Remove any indexed entries that ended up with no extractable content
                foreach (ContentData empty in TextContent.Where(x => string.IsNullOrEmpty(x.PlainText)).ToList())
                {
                    TextContent.Remove(empty);
                }

                // Sync HasPlainText for every file: true only if content was actually extracted
                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {

                        file.HasPlainText = TextContent.Any(x => x.Filepath == file.Sökväg);
                    }
                }

                SaveIndexFile(indexPath);
            }

            public void ClearIndexedContent()
            {
                string indexPath = $"{SavePath}\\IndexedContent.json";

                foreach (FileData file in CurrentFiles)
                {
                    file.HasPlainText = false;

                    if (TextContent?.Any(x => x.Filepath == file.Sökväg) == true)
                    {
                        ContentData contentToRemove = TextContent.FirstOrDefault(x => x.Filepath == file.Sökväg);
                        TextContent.Remove(contentToRemove);
                        SaveIndexFile(indexPath);
                    }
                }
            }

            private void LoadIndexFile(string indexPath)
            {
                using StreamReader streamReader = new(indexPath);
                string fileContent = streamReader.ReadToEnd();
                TextContent = JsonConvert.DeserializeObject<ObservableCollection<ContentData>>(fileContent);

                List<string> indexedFiles = new();

                foreach (ContentData content in TextContent)
                {
                    indexedFiles.Add(content.Filepath);
                }

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        file.HasPlainText = indexedFiles.Contains(file.Sökväg);
                    }
                }
            }

            private void SaveIndexFile(string indexPath)
            {
                if (!Directory.Exists(SavePath))
                {
                    Directory.CreateDirectory(SavePath);
                }

                using StreamWriter streamWriter = new(indexPath);
                var data = JsonConvert.SerializeObject(TextContent);
                streamWriter.WriteLine(data);
            }

            public void ClearThumbnails()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.RemoveThumbnail();
                }
            }

            /// <summary>
            /// Scans the Thumbnails folder and updates ThumbnailSource for every file across
            /// all projects: sets the path when a matching .jpeg exists, clears it when it doesn't.
            /// </summary>
            public void SyncThumbnails()
            {
                string thumbnailDir = Path.Combine(SavePath, "Thumbnails");

                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var file in project.StoredFiles)
                    {
                        string expected = Path.Combine(thumbnailDir, file.Namn + ".jpeg");
                        if (File.Exists(expected))
                            file.ThumbnailSource = expected;
                        else
                            file.ThumbnailSource = string.Empty;
                    }
                }
            }

            /// <summary>
            /// Generate a thumbnail for a single file and save it to the thumbnail directory.
            /// This is extracted so callers can run per-file generation on background threads
            /// and report progress from the UI layer.
            /// </summary>
            public void GenerateThumbnail(FileData file, string thumbnailDir)
            {
                try
                {
                    if (!file.IsValidPdf())
                        return;

                    if (!Directory.Exists(thumbnailDir))
                        Directory.CreateDirectory(thumbnailDir);

                    file.RemoveThumbnail();

                    byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);
                    using var doc = new MuPDFDocument(new MuPDFContext(), bytes, InputFileTypes.PDF);

                    string target = Path.Combine(thumbnailDir, file.Namn + ".jpeg");
                    file.ThumbnailSource = target;
                    doc.SaveImageAsJPEG(0, 1, target, 20);
                }
                catch (Exception ex)
                {
                    // Log and continue — don't let one failure abort the whole batch
                    logger?.LogError(ex, "Failed to generate thumbnail for {Path}", file?.Sökväg);
                }
            }

            public void SetBookmark(PageData page)
            {
                FavPage = page;
            }

            public void AddBookmark(string pageName)
            {
                int pageNr = PreviewVM.CurrentPage1;
                PageData page = new() { PageNr = pageNr, PageName = pageName };

                if (PreviewVM.CurrentFile != null)
                {
                    PreviewVM.CurrentFile.FavPages.Add(page);
                }

                SortBookmarks();
            }

            public void RenameBookmark(string pageName)
            {
                if (FavPage != null)
                {
                    FavPage.PageName = pageName;
                }
            }

            public void RemoveBookmark(PageData page)
            {
                if (PreviewVM.CurrentFile != null)
                {
                    PreviewVM.CurrentFile.FavPages.Remove(page);
                }

                SortBookmarks();
            }

            private void SortBookmarks()
            {
                List<PageData> tempList = PreviewVM.CurrentFile.FavPages.OrderBy(x => x.PageNr).ToList();
                PreviewVM.CurrentFile.FavPages.Clear();
                PreviewVM.CurrentFile.FavPages = new ObservableCollection<PageData>(tempList);
            }

            public void MarkFavorite()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Favorite = !file.Favorite;
                }
            }

            public void NewCollection(string name)
            {
                Storage.Collections.Add(name);
            }

            public void RemoveCollection()
            {
                if (CollectionContent.Count > 0)
                {
                    foreach (FileData file in CollectionContent)
                    {
                        file.PartOfCollections.Remove(CurrentCollection);
                    }
                }
                Storage.Collections.Remove(CurrentCollection);
            }

            public void AddFileToCollection(string collection)
            {
                foreach (FileData file in CurrentFiles.Where(x => !x.PartOfCollections.Contains(collection)))
                {
                    file.PartOfCollections.Add(collection);
                }
                CurrentCollection = collection;
            }

            public void RemoveFileFromCollection()
            {
                CurrentFile.PartOfCollections.Remove(CurrentCollection);
                SetCollectionContent();
            }

            public void SetCollectionContent()
            {
                CollectionContent.Clear();

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles.Where(x => x.PartOfCollections.Contains(CurrentCollection)))
                    {
                        CollectionContent.Add(file);
                    }
                }
            }

            public void RenameCollection(string newName)
            {
                if (!Storage.Collections.Contains(newName))
                {
                    foreach (FileData file in CollectionContent)
                    {
                        file.PartOfCollections.Remove(CurrentCollection);
                        file.PartOfCollections.Add(newName);
                    }

                    int index = Storage.Collections.IndexOf(CurrentCollection);
                    Storage.Collections[index] = newName;

                    CurrentCollection = newName;
                }
            }

            public async Task LoadFile(Visual window)
            {
                var topLevel = TopLevel.GetTopLevel(window);

                var jsonformat = new FilePickerFileType("Json format") { Patterns = new[] { "*.json" } };
                List<FilePickerFileType> formatlist = new() { jsonformat };
                IReadOnlyList<FilePickerFileType> fileformat = formatlist;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Load File",
                    AllowMultiple = false,
                    FileTypeFilter = fileformat
                });

                if (files.Count > 0)
                {
                    // Remember the loaded file path so comparisons target the correct file
                    try { CurrentProjectsFilePath = files[0].Path.LocalPath; } catch { CurrentProjectsFilePath = null; }

                    await using var stream = await files[0].OpenReadAsync();
                    using var streamReader = new StreamReader(stream);
                    string fileContent = await streamReader.ReadToEndAsync();
                    DeserializeLoadFile(fileContent);
                }
            }

            public void LoadFileAuto()
            {
                string path = $"{SavePath}\\Projects.json";
                try { CurrentProjectsFilePath = path; } catch { CurrentProjectsFilePath = null; }

                using StreamReader streamReader = new(path);
                string fileContent = streamReader.ReadToEnd();
                DeserializeLoadFile(fileContent);
            }

            public void DeserializeLoadFile(string fileContent)
            {
                Storage = new ProjectStorage();
                try // Trying reading v.2 save file
                {
                    Storage = JsonConvert.DeserializeObject<ProjectStorage>(fileContent);
                }
                catch // If not, try read as v.1 save file
                {
                    Storage.StoredProjects = JsonConvert.DeserializeObject<ObservableCollection<ProjectData>>(fileContent);
                    RemoveProjects(Storage.StoredProjects.Where(x => x.Category == SEARCH_CATEGORY).ToList());
                    RemoveProjects(Storage.StoredProjects.Where(x => x.Category == "Favorites").ToList());
                }

                // Clear ThumbnailSource for any file whose thumbnail no longer exists on disk
                foreach (var project in Storage.StoredProjects)
                    foreach (var file in project.StoredFiles)
                        if (!string.IsNullOrEmpty(file.ThumbnailSource) && !File.Exists(file.ThumbnailSource))
                            file.ThumbnailSource = string.Empty;

                SetProjectlist();
                SetDefaultSelection();
                GetGroups();
                SyncPreviewRegionColor();
            }

            public async Task SaveFile(Avalonia.Visual window)
            {
                var topLevel = TopLevel.GetTopLevel(window);

                var jsonformat = new FilePickerFileType("Json format") { Patterns = new[] { "*.json" } };
                List<FilePickerFileType> formatlist = new() { jsonformat };
                IReadOnlyList<FilePickerFileType> fileformat = formatlist;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File",
                    FileTypeChoices = fileformat
                });

                if (file is not null)
                {
                    // Remember the path the user chose for future comparisons
                    try { CurrentProjectsFilePath = file.Path.LocalPath; } catch { CurrentProjectsFilePath = null; }

                    await using var stream = await file.OpenWriteAsync();
                    using var streamWriter = new StreamWriter(stream);
                    var data = JsonConvert.SerializeObject(Storage);
                    await streamWriter.WriteLineAsync(data);
                }
            }

            public async Task SaveFileAuto()
            {
                if (!Directory.Exists(SavePath))
                {
                    Directory.CreateDirectory(SavePath);
                }

                string path = $"{SavePath}\\Projects.json";
                try { CurrentProjectsFilePath = path; } catch { CurrentProjectsFilePath = null; }

                using StreamWriter streamWriter = new(path);
                var data = JsonConvert.SerializeObject(Storage);
                await streamWriter.WriteLineAsync(data);
            }

            /// <summary>
            /// Compare the in-memory Storage (Projects) with the on-disk Projects.json file.
            /// Returns true when the two are different (i.e. there are unsaved changes).
            /// </summary>
            public bool IsStorageDifferentFromFile(string? projectsFilePath = null)
            {
                try
                {
                    string path;
                    if (!string.IsNullOrWhiteSpace(projectsFilePath))
                        path = projectsFilePath;
                    else if (!string.IsNullOrWhiteSpace(CurrentProjectsFilePath))
                        path = CurrentProjectsFilePath;
                    else
                        path = Path.Combine(SavePath, "Projects.json");

                    Debug.WriteLine($"Comparing storage to file: '{path}'");
                    if (!File.Exists(path))
                    {
                        // No file on disk -> consider storage different (unsaved)
                        return true;
                    }

                    string fileContent = File.ReadAllText(path);

                    // Parse saved JSON
                    JToken saved = JToken.Parse(fileContent);

                    // Prune transient UI-related fields that may exist in older save files
                    // but are no longer part of StoreData. This avoids false positives when
                    // comparing the in-memory model to an on-disk file from an older format.
                    PruneTransientUiFields(saved);

                    // Serialize current storage using same JsonConvert pipeline as Save to avoid
                    // differences caused by serializer variations (null vs omitted, converters, etc.)
                    string currentJson = JsonConvert.SerializeObject(Storage);
                    var current = JToken.Parse(currentJson);

                    // Prune transient fields from the current representation as well
                    PruneTransientUiFields(current);

                    return !JToken.DeepEquals(saved, current);
                }
                catch
                {
                    // If comparison fails for any reason, assume changed so caller can decide to save.
                    return true;
                }
            }

            // Remove transient UI fields that used to be stored in Projects.json but
            // are now part of UISettings.json / UI viewmodel. This prevents the
            // comparison from treating those legacy fields as meaningful differences.
            private static readonly string[] TransientPropertyNames = new[]
            {
                "ThumbnailSource",
                "HasPlainText",
                "FileStatus",
                "HasThumbnail",
                // Derived / UI-only properties that should not affect storage equality
                "HasNote",
                "HasBookmarks",
                "HasAppendedFiles",
                "FiletypesTree",
            };

            private static void PruneTransientUiFields(JToken? token)
            {
                if (token == null) return;

                // Recursively remove any properties with names considered transient.
                void Recurse(JToken t)
                {
                    if (t.Type == JTokenType.Object)
                    {
                        var obj = (JObject)t;
                        // Collect properties to remove to avoid modifying collection during enumeration
                        var toRemove = obj.Properties().Where(p => TransientPropertyNames.Contains(p.Name)).ToList();
                        foreach (var p in toRemove)
                            p.Remove();

                        // Recurse into remaining properties
                        foreach (var child in obj.Properties())
                            Recurse(child.Value);
                    }
                    else if (t.Type == JTokenType.Array)
                    {
                        foreach (var item in (JArray)t)
                            Recurse(item);
                    }
                }

                Recurse(token);
            }

            public void BackupSaveFile()
            {
                string backupDir = $"{SavePath}\\Backup_{DateTime.Today:d}";

                Directory.CreateDirectory(backupDir);

                System.IO.File.Copy($"{SavePath}\\Projects.json", $"{backupDir}\\Projects.json", true);
            }

            public async Task AddFile(Avalonia.Visual window)
            {
                if (CurrentProject != null)
                {
                    var topLevel = TopLevel.GetTopLevel(window);
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Add File",
                        FileTypeFilter = new[] { FilePickerFileTypes.Pdf },
                        AllowMultiple = true
                    });

                    foreach (var file in files)
                    {
                        string path = file.Path.LocalPath;
                        CurrentProject.Newfile(path);
                        SetDefaultType();
                    }
                }
            }

            // Theme/resource updates handled by UIService

            public void AddFilesDrag(string path)
            {
                CurrentProject.Newfile(path);
                SetDefaultType();
            }

            public void SetCategory(string category)
            {
                SetProjecCategory(category);
            }

            public void SetGroup(string group)
            {
                SetGroups(group);
                GetGroups();
            }

            public void CopyFilenameToClipboard(Avalonia.Visual window)
            {
                string store = string.Empty;

                foreach (FileData file in CurrentFiles)
                {
                    store += file.Namn + Environment.NewLine;
                }

                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(store);
            }

            public void CopyFilepathToClipboard(Avalonia.Visual window)
            {
                string store = string.Empty;

                foreach (FileData file in CurrentFiles)
                {
                    store += file.Sökväg + Environment.NewLine;
                }

                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(store);
            }

            public void CopyListviewToClipboard(Avalonia.Visual window)
            {
                string store = string.Empty;

                foreach (FileData file in CurrentFiles)
                {
                    if (CurrentProject.Meta_1 == true) { store += file.Namn + "\t"; }
                    if (CurrentProject.Meta_2 == true) { store += file.Filtyp + "\t"; }
                    if (CurrentProject.Meta_3 == true) { store += file.Uppdrag + "\t"; }
                    if (CurrentProject.Meta_4 == true) { store += file.Tagg + "\t"; }
                    if (CurrentProject.Meta_5 == true) { store += file.Färg + "\t"; }
                    if (CurrentProject.Meta_6 == true) { store += file.Handling + "\t"; }
                    if (CurrentProject.Meta_7 == true) { store += file.Status + "\t"; }
                    if (CurrentProject.Meta_8 == true) { store += file.Datum + "\t"; }
                    if (CurrentProject.Meta_9 == true) { store += file.Ritningstyp + "\t"; }
                    if (CurrentProject.Meta_10 == true) { store += file.Beskrivning1 + "\t"; }
                    if (CurrentProject.Meta_11 == true) { store += file.Beskrivning2 + "\t"; }
                    if (CurrentProject.Meta_12 == true) { store += file.Beskrivning3 + "\t"; }
                    if (CurrentProject.Meta_13 == true) { store += file.Beskrivning4 + "\t"; }
                    if (CurrentProject.Meta_14 == true) { store += file.Revidering + "\t"; }
                    if (CurrentProject.Meta_15 == true) { store += file.Sökväg + "\t"; }

                    store += Environment.NewLine;
                }
                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(store);
            }

            public void SelectFilesForMetaworker(bool singleMode)
            {
                MetaStore.Clear();
                PathStore.Clear();

                if (singleMode == true)
                {
                    foreach (FileData file in CurrentFiles) { PathStore.Add((file.Sökväg)); }
                }
                if (singleMode == false)
                {
                    foreach (FileData file in FilteredFiles) { PathStore.Add((file.Sökväg)); }
                }
            }

            public int GetNrSelectedFiles()
            {
                return PathStore.Count;
            }

            public void SetMeta()
            {
                int i = 0;
                foreach (string path in PathStore)
                {
                    FileData file = FilteredFiles.FirstOrDefault(x => x.Sökväg == path);

                    string[] md = MetaStore[i];

                    file.Handling = md[0];
                    file.Status = md[1];
                    file.Datum = md[2];
                    file.Ritningstyp = md[3];
                    file.Beskrivning1 = md[4];
                    file.Beskrivning2 = md[5];
                    file.Beskrivning3 = md[6];
                    file.Beskrivning4 = md[7];
                    file.Revidering = md[8];
                    file.Sökväg = path;

                    i++;
                }
            }

            public void GetMetadata(int k)
            {
                string[] tags = ["Handlingstyp = ", "Granskningsstatus = ", "Datum = ", "Ritningstyp = ", "Beskrivning1 = ", "Beskrivning2 = ", "Beskrivning3 = ", "Beskrivning4 = ", "Revidering = "];
                int ntags = tags.Length;

                string path = PathStore[k];
                string[] description = new string[ntags];
                try
                {
                    string[] lines = System.IO.File.ReadAllLines(path + ".md", Encoding.GetEncoding("ISO-8859-1"));

                    int iter = 1;
                    int start = 100;
                    int end = 0;
                    foreach (string line in lines)
                    {
                        if (line == "[Metadata]") { start = iter; }
                        if (line.Trim().Length == 0 || iter > start) { end = iter; }
                        iter++;
                    }

                    for (int i = start; i < end; i++)
                    {
                        string line = lines[i];
                        for (int j = 0; j < ntags; j++)
                        {
                            string tag = tags[j];
                            if (line.StartsWith(tag))
                            {
                                description[j] = line.Replace(tag, "");
                            }
                            if (line.StartsWith(tag.ToUpper()))
                            {
                                description[j] = line.Replace(tag.ToUpper(), "");
                            }
                        }
                    }
                    MetaStore.Add(description);
                }
                catch (Exception)
                {
                    MetaStore.Add(["", "", "", "", "", "", "", "", ""]);
                }
            }

            public void ClearMeta()
            {
                ClearSelectedMetadata();
            }

            public void Search()
            {
                if (IndexedSearch)
                {
                    SearchIndex();
                }
                else
                {
                    SearchFiles();
                }

                OnPropertyChanged("UpdateColumns");
            }

            public void SearchIndex()
            {
                string indexPath = $"{SavePath}//IndexedContent.json";

                if (TextContent == null)
                {
                    if (System.IO.File.Exists(indexPath))
                    {
                        LoadIndexFile(indexPath);
                    }
                    else
                    {
                        TextContent = new ObservableCollection<ContentData>();
                    }
                }

                FilteredFiles.Clear();
                CurrentProject = new ProjectData() { Namn = SearchText, Category = SEARCH_CATEGORY };

                List<string> filepaths = new();

                foreach (ContentData content in TextContent)
                {
                    if (content.PlainText.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        filepaths.Add(content.Filepath);
                    }
                }

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        if (filepaths.Contains(file.Sökväg))
                        {
                            FilteredFiles.Add(file);
                        }
                    }
                }

                OnPropertyChanged(nameof(NrFilteredFiles));
            }

            public void CheckSingleFile()
            {
                if (CurrentFile != null)
                {
                    if (CurrentFile.IsValidPdf())
                    {
                        CurrentFile.FileStatus = "OK";
                    }
                    else
                    {
                        CurrentFile.FileStatus = "Missing";
                    }
                }
            }

            public async Task CheckProjectFiles()
            {
                await Task.Run(() => CheckFileAsync());
            }

            public async Task CheckFileAsync()
            {
                ClearFileStatus();

                int n = CurrentProject.StoredFiles.Count;
                int i = 0;

                foreach (FileData file in CurrentProject.StoredFiles)
                {
                    i++;

                    if (file.IsValidPdf())
                    {
                        file.FileStatus = "OK";
                    }
                    else
                    {
                        file.FileStatus = "Missing";
                    }

                    PreviewVM.Progress = (int)(100 * ((float)i / (float)n));
                }
            }

            public void ClearFileStatus()
            {
                foreach (FileData file in CurrentProject.StoredFiles)
                {
                    file.FileStatus = "";
                }
            }

            public void OpenFile()
            {
                try
                {
                    foreach (FileData file in CurrentFiles)
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = file.Sökväg,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception e) { Debug.WriteLine(e); }
            }

            public void OpenFileDirect(string path)
            {
                try
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = path,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception e) { Debug.WriteLine(e); }
            }

            public void OpenMeta()
            {
                try
                {
                    foreach (FileData file in CurrentFiles)
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = file.Sökväg + ".md",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch { }
            }

            public void OpenDwg()
            {
                if (CurrentFile?.Filtyp == DRAWING_TYPE)
                {
                    string dwgPathOld = CurrentFile.Sökväg.Replace("Ritning", "Ritdef").Replace("pdf", "dwg");
                    string dwgPathNew = CurrentFile.Sökväg.Replace("Drawing", "Drawing Definition").Replace("pdf", "dwg");

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = dwgPathOld,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = dwgPathNew,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                }
            }

            public void OpenDoc()
            {
                if (CurrentFile?.Filtyp == DOCUMENT_TYPE)
                {
                    string docPath = CurrentFile.Sökväg.Replace("pdf", "docx");

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = docPath,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                }
            }

            public void OpenPath()
            {
                try
                {
                    string folderpath = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void OpenPathDirect(string filepath)
            {
                try
                {
                    string folderpath = System.IO.Path.GetDirectoryName(filepath);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void AddColor(string color)
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = color;
                }
            }

            public void ClearAll()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = "";
                    file.Tagg = "";
                }
            }

            public void AddTag(string tag)
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = tag;
                }
            }

            public void ClearTag()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = "";
                }
            }

            public void EditType(string type)
            {
                SetTypeSelected(type);
            }

            public void select_files(IList<FileData> files)
            {
                CurrentFiles = files;
                SetAttachedView();
            }

            private void SetAttachedView()
            {
                if (CurrentFile != null)
                {
                    AttachedView = CurrentFile.HasAppendedFiles;
                }
            }

            public void SelectType(string name)
            {
                string currentType = Type;

                if (currentType != name)
                {
                    Type = name;
                }
                OnPropertyChanged("UpdateColumns");
            }

            public void SelectProject(string name)
            {
                string currentProjectName = CurrentProject.Namn;
                if (currentProjectName != name)
                {
                    SetProject(name);
                }
                OnPropertyChanged("UpdateColumns");
            }

            public void ReselectProject()
            {
                SetProject(CurrentProject.Namn);
                OnPropertyChanged("UpdateColumns");
            }

            public void Renameproject(string newProjectName)
            {
                RenameProject(newProjectName);
                SetProjectlist();
            }

            public void UpdateTreeview()
            {
                OnPropertyChanged("TreeViewUpdate");
            }

            public void NewProject(string name, string group = null, string category = PROJECT_CATEGORY)
            {
                if (!Storage.StoredProjects.Any(x => x.Namn == name))
                {
                    ProjectData newProject = new() { Namn = name, Parent = group, Category = category };

                    Storage.StoredProjects.Add(newProject);
                    CurrentProject = newProject;

                    SetProjectlist();
                    SetDefaultType();
                    SortProjects();
                }
            }

            public void RemoveProject()
            {
                Storage.StoredProjects.Remove(CurrentProject);
                SetProjectlist();
                SetDefaultSelection();
                SortProjects();
            }

            public void RemoveProjects(List<ProjectData> list)
            {
                foreach (ProjectData project in list)
                {
                    Storage.StoredProjects.Remove(project);
                }

                SetProjectlist();
                SetDefaultSelection();
                SortProjects();
            }

            public void RenameProject(string projectName)
            {
                CurrentProject.Namn = projectName;

                foreach (FileData file in CurrentProject.StoredFiles)
                {
                    file.Uppdrag = projectName;
                }
                CurrentProject.SetFiletypeList();
            }

            public void GetGroups()
            {
                Groups.Clear();

                List<string> list = Storage.StoredProjects.Select(x => x.Parent).Where(x => x != null).Distinct().ToList();
                list.Remove("");

                Groups = new ObservableCollection<string>(list);
            }

            public void SetGroups(string group)
            {
                CurrentProject.Parent = group;
            }

            public void SortProjects()
            {
                List<ProjectData> sortedLibrary = Storage.StoredProjects.Where(x => x.Category == "Library").OrderBy(x => x.Namn).ToList();
                List<ProjectData> sortedArchive = Storage.StoredProjects.Where(x => x.Category == "Archive").OrderBy(x => x.Namn).ToList();
                List<ProjectData> sortedProject = Storage.StoredProjects.Where(x => x.Category == PROJECT_CATEGORY).OrderBy(x => x.Namn).ToList();

                Storage.StoredProjects.Clear();

                foreach (var project in sortedLibrary) { Storage.StoredProjects.Add(project); }
                foreach (var project in sortedArchive) { Storage.StoredProjects.Add(project); }
                foreach (var project in sortedProject) { Storage.StoredProjects.Add(project); }

                SetProjectlist();
            }

            public void RemoveSelectedFiles()
            {
                foreach (FileData file in CurrentFiles)
                {
                    CurrentProject.RemoveFile(file);
                }

                CurrentProject.SetFiletypeList();

                if (FilteredFiles == null)
                {
                    SetDefaultSelection();
                }
            }

            public void SetProject(string name)
            {
                ProjectData project = Storage.StoredProjects.FirstOrDefault(x => x.Namn == name);

                SelectProjectAsync(project);

                if (!CurrentProject.Filetypes.Contains(Type))
                {
                    Type = ALL_TYPES;
                }
            }

            public void SelectProjectAsync(ProjectData project)
            {
                CurrentProject = project;
            }

            public void SetProjecCategory(string name)
            {
                CurrentProject.Category = name;

                if (name != PROJECT_CATEGORY)
                {
                    CurrentProject.Parent = null;
                }

                SortProjects();
            }

            public void SetDefaultType()
            {
                Type = ALL_TYPES;
            }

            public void SetTypeSelected(string type)
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Filtyp = type;
                }
                currentProject.SetFiletypeList();
                UpdateFilter();
            }

            public void SetDefaultSelection()
            {
                string defaultProject = Storage.StoredProjects.FirstOrDefault().Namn;
                CurrentProject = GetProject(defaultProject);
                Type = ALL_TYPES;
            }

            public void UpdateFilter()
            {
                FilteredFiles.Clear();

                if (Type != ALL_TYPES)
                {
                    foreach (FileData file in CurrentProject.StoredFiles.Where(x => x.Filtyp == Type).OrderBy(x => x.Namn))
                    {
                        FilteredFiles.Add(file);
                    }
                }
                else
                {
                    foreach (FileData file in CurrentProject.StoredFiles.OrderBy(x => x.Namn).OrderByDescending(x => x.Filtyp))
                    {
                        FilteredFiles.Add(file);
                    }
                }
                OnPropertyChanged(nameof(NrFilteredFiles));

                if (CurrentProject.Category != SEARCH_CATEGORY)
                {
                    IndexedSearch = false;
                    PreviewVM.SearchMode = false;
                    SearchText = String.Empty;
                }
            }

            public ProjectData GetProject(string name)
            {
                return Storage.StoredProjects.FirstOrDefault(x => x.Namn == name);
            }

            public void SetProjectlist()
            {
                ProjectList.Clear();

                List<string> newList = Storage.StoredProjects.Select(x => x.Namn).Distinct().ToList();

                foreach (string item in newList)
                {
                    ProjectList.Add(item);
                }
            }

            public ProjectData GetDefaultProject()
            {
                return Storage.StoredProjects.FirstOrDefault();
            }

            public void ClearSelectedMetadata()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Handling = "";
                    file.Status = "";
                    file.Datum = "";
                    file.Ritningstyp = "";
                    file.Beskrivning1 = "";
                    file.Beskrivning2 = "";
                    file.Beskrivning3 = "";
                    file.Beskrivning4 = "";
                    file.Revidering = "";
                }
            }

            public void AddAppendedFile(string filepath, bool fromFolder = false)
            {
                if (CurrentFile != null && !CurrentFile.AppendedFiles.Any(x => x.Sökväg == filepath))
                {
                    CurrentFile.AppendedFiles.Add(new FileData()
                    {
                        Namn = System.IO.Path.GetFileNameWithoutExtension(filepath),
                        Sökväg = filepath,
                        IsFromFolder = fromFolder
                    });

                    SortAttachedFiles();
                }
            }

            public void AddOtherFile(string filepath)
            {
                if (CurrentFile != null && !CurrentFile.OtherFiles.Any(x => x.Filepath == filepath))
                {
                    OtherData newFile = new() { Filepath = filepath };
                    newFile.SetFile();

                    CurrentFile.OtherFiles.Add(newFile);
                    SortOtherFiles();
                }
            }


            public void RemoveAttachedFile(IList<FileData> files)
            {
                foreach (FileData file in files)
                {
                    CurrentFile.AppendedFiles.Remove(file);
                }

                SortAttachedFiles();
            }

            public void RemoveOtherFile(OtherData file)
            {
                if (file != null)
                {
                    CurrentFile.OtherFiles.Remove(file);
                    SortOtherFiles();
                }
            }

            private void SortAttachedFiles()
            {
                if (CurrentFile != null)
                {
                    SortAttachedFilesDirect(CurrentFile);
                }
            }

            private void SortAttachedFilesDirect(FileData file)
            {
                List<FileData> tempList = file.AppendedFiles.OrderBy(x => x.Namn).ToList();
                file.AppendedFiles.Clear();
                file.AppendedFiles = new ObservableCollection<FileData>(tempList);
            }

            private void SortOtherFiles()
            {
                if (CurrentFile != null)
                {
                    SortOtherFilesDirect(CurrentFile);
                }
            }

            private void SortOtherFilesDirect(FileData file)
            {
                List<OtherData> tempList = file.OtherFiles.OrderBy(x => x.Name).ToList();
                file.OtherFiles.Clear();
                file.OtherFiles = new ObservableCollection<OtherData>(tempList);
            }

            public void SearchFiles()
            {
                FilteredFiles.Clear();
                CurrentProject = new ProjectData() { Namn = SearchText, Category = "Search" };

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        bool finished = false;

                        string b0 = file.Namn;
                        string b1 = file.Beskrivning1;
                        string b2 = file.Beskrivning2;
                        string b3 = file.Beskrivning3;
                        string b4 = file.Tagg;

                        if (b0 != null && !finished) { if (b0.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b1 != null && !finished) { if (b1.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b2 != null && !finished) { if (b2.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b3 != null && !finished) { if (b3.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b4 != null && !finished) { if (b4.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                    }
                }

                OnPropertyChanged("NrFilteredFiles");

            }

            public void RenameOriginal(string newName)
            {
                string oldName = CurrentFile.Namn;
                string oldPath = CurrentFile.Sökväg;

                if (oldName != newName && newName.Length > 0 && CurrentFile.IsLocal())
                {
                    string newPath = CurrentFile.Sökväg.Replace(oldName, newName);
                    try
                    {
                        System.IO.File.Move(oldPath, newPath);
                    }
                    catch
                    {
                        return;
                    }

                    CurrentFile.Sökväg = newPath;
                    CurrentFile.Namn = newName;

                }
            }


            public void MoveSelectedFiles(ProjectData project)
            {
                if (project != null)
                {
                    foreach (FileData file in CurrentFiles.ToList())
                    {

                        if (!project.StoredFiles.Contains(file))
                        {
                            CurrentProject.StoredFiles.Remove(file);
                            file.Filtyp = "New";
                            file.Uppdrag = project.Namn;
                            project.StoredFiles.Add(file);
                        }
                    }

                    UpdateFilter();
                }
            }


            public void WatermarkFiles(string text = "Arbetskopia")
            {

                string date = DateTime.Today.ToString("yyyy-MM-dd");
                string folder = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                string outputPath = folder + "\\" + text + " " + date;

                System.IO.Directory.CreateDirectory(outputPath);

                foreach (FileData file in CurrentFiles)
                {

                    if (file.IsValidPdf())
                    {
                        string outputFilePath = outputPath + "\\" + file.Namn + "_" + text + "_" + date + ".pdf";

                        if (!IsFileInUse(outputFilePath))
                        {
                            PdfDocument pdfDoc = new PdfDocument(new PdfReader(file.Sökväg), new PdfWriter(outputFilePath));

                            PdfFont font = PdfFontFactory.CreateFont(FontProgramFactory.CreateFont(StandardFonts.HELVETICA));
                            Document document = new Document(pdfDoc);
                            iText.Kernel.Geom.Rectangle pageSize;

                            PdfCanvas canvas;
                            int n = pdfDoc.GetNumberOfPages();
                            for (int i = 1; i <= n; i++)
                            {
                                PdfPage page = pdfDoc.GetPage(i);
                                page.NewContentStreamBefore();
                                pageSize = page.GetPageSize();
                                float fontSize = pageSize.GetWidth() / 10;

                                canvas = new PdfCanvas(page);

                                Paragraph paragraph = new Paragraph(text).SetFont(font).SetFontSize(fontSize).SetFontColor(ColorConstants.GRAY).SetOpacity(0.5f);
                                paragraph.SetMultipliedLeading(0.5f);
                                paragraph.Add(Environment.NewLine);
                                paragraph.Add(new Paragraph(date).SetFont(font).SetFontSize(fontSize / 2).SetFontColor(ColorConstants.GRAY).SetOpacity(0.5f));

                                iText.Layout.Canvas canvasWatermark2 = new iText.Layout.Canvas(canvas, pdfDoc.GetDefaultPageSize()).ShowTextAligned(paragraph, pageSize.GetWidth() / 2, pageSize.GetHeight() / 2, 1, TextAlignment.CENTER, VerticalAlignment.MIDDLE, 120);
                            }
                            pdfDoc.Close();
                        }
                    }
                }
            }

            public static bool IsFileInUse(string filePath)
            {
                if (System.IO.File.Exists(filePath) == false)
                {
                    return false;
                }
                else
                {
                    try
                    {
                        // Try opening the file with read-write access and an exclusive lock
                        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // If we can open it, the file isn't in use
                        }
                    }
                    catch (IOException)
                    {
                        // IOException indicates the file is in use
                        return true;
                    }

                    // If no exception was thrown, the file is not in use
                    return false;
                }
            }
        }
    }



