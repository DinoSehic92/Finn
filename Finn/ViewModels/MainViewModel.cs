using Finn.Dialog;
using Finn.Model;
using Finn.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using MuPDFCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;



namespace Finn.ViewModels
{
    public class MainViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public MainViewModel() 
        {
            NewProject("New Project");
            SetProjectlist();
            SetProject("New Project");
            SetDefaultType();


        }

        private PreviewViewModel previewVM = new PreviewViewModel();
        public PreviewViewModel PreviewVM
        {
            get { return previewVM; }
            set { previewVM = value; OnPropertyChanged("PreviewVM"); }
        }

        public List<string[]> MetaStore = new List<string[]>();

        public List<string> PathStore = new List<string>();

        private ObservableCollection<string> favorites = new ObservableCollection<string>() { "Default" };
        public ObservableCollection<string> Favorites
        {
            get { return favorites; }
            set { favorites = value; OnPropertyChanged("Favorites"); }
        }

        private string currentCollection = string.Empty;
        public string CurrentCollection
        {
            get { return currentCollection; }
            set { currentCollection = value; OnPropertyChanged("CurrentCollection"); SetCollectionContent(); }
        }

        private ObservableCollection<FileData> collectionContent = new ObservableCollection<FileData>();
        public ObservableCollection<FileData> CollectionContent
        {
            get { return collectionContent; }
            set { collectionContent = value; OnPropertyChanged("CollectionContent"); }
        }

        public Window PreviewWindow;


        private ObservableCollection<string> groups = new ObservableCollection<string>() {};
        public ObservableCollection<string> Groups
        {
            get { return groups; }
            set { groups = value; OnPropertyChanged("groups"); }
        }

        private PageData favPage;
        public PageData FavPage
        {
            get { return favPage; }
            set { favPage = value; OnPropertyChanged("FavPage"); TrySetPage(); }
        }

        public string ProjectMessage { get; set; } = "";

        public bool Confirmed = false;

        private bool attachedView = false;
        public bool AttachedView
        {
            get { return attachedView; }
            set { attachedView = value; OnPropertyChanged("AttachedView"); }
        }

        private List<MenuItem> fileTypeSelection = new List<MenuItem>();

        public List<MenuItem> FileTypeSelection
        {
            get { return fileTypeSelection; }
            set { fileTypeSelection = value; OnPropertyChanged("FileTypeSelection"); }
        }

        private StoreData storage = new StoreData();
        public StoreData Storage
        {
            get { return storage; }
            set { storage = value; OnPropertyChanged("Storage"); Storage.General.PropertyChanged += OnGeneralChanged; }
        }

        private List<string> projectList = new List<string>();
        public List<string> ProjectList
        {
            get { return projectList; }
            set { projectList = value; OnPropertyChanged("ProjectList"); }
        }

        private ProjectData currentProject;
        public ProjectData CurrentProject
        {
            get { return currentProject; }
            set { currentProject = value; OnPropertyChanged("CurrentProject"); UpdateFilter(); }
        }

        private string type = null;
        public string Type
        {
            get { return type; }
            set { type = value; OnPropertyChanged("Type"); UpdateFilter(); }
        }

        private ObservableCollection<FileData> filteredFiles = new ObservableCollection<FileData>();
        public ObservableCollection<FileData> FilteredFiles
        {
            get { return filteredFiles; }
            set { filteredFiles = value; OnPropertyChanged("FilteredFiles"); OnPropertyChanged("NrFilteredFiles"); }
        }

        private ObservableCollection<ContentData> indexedContent = null;
        public ObservableCollection<ContentData> IndexedContent
        {
            get { return indexedContent; }
            set { indexedContent = value; OnPropertyChanged("IndexedContent"); }
        }

        private ContentData selectedIndexedContent = null;
        public ContentData SelectedIndexedContent
        {
            get { return selectedIndexedContent; }
            set { selectedIndexedContent = value; OnPropertyChanged("SelectedIndexedContent"); }
        }

        public int NrFilteredFiles
        {
            get
            {
                if (FilteredFiles == null) { return 0; }
                else { return FilteredFiles.Count(); }
            }
        }

        public int NrSelectedFiles
        {
            get
            {
                if (CurrentFiles == null) { return 0; }
                else { return CurrentFiles.Count(); }
            }
        }

        private IList<FileData> currentFiles = null;
        public IList<FileData> CurrentFiles
        {
            get { return currentFiles; }
            set
            {
                currentFiles = value;
                OnPropertyChanged("FiletypesTree");
                OnPropertyChanged("CurrentFiles");
                OnPropertyChanged("CurrentFile");
                OnPropertyChanged("NrSelectedFiles");
            }
        }

        private FileData currentFile = null;
        public FileData CurrentFile
        {
            get
            {
                if (CurrentFiles != null)
                {
                    return CurrentFiles.LastOrDefault();
                }
                else
                {
                    return null;
                }
            }
        }

        private bool previewWindowOpen = false;
        public bool PreviewWindowOpen
        {
            get { return previewWindowOpen; }
            set { previewWindowOpen = value; OnPropertyChanged("PreviewWindowOpen"); if (PreviewWindowOpen) { PreviewEmbeddedOpen = false; }; }
        }

        private bool previewEmbeddedOpen = true;
        public bool PreviewEmbeddedOpen
        {
            get { return previewEmbeddedOpen; }
            set { previewEmbeddedOpen = value; OnPropertyChanged("PreviewEmbeddedOpen"); NewPreviewContext(); if (PreviewEmbeddedOpen) { PreviewWindowOpen = false; }; }
        }

        private bool treeViewOpen = true;
        public bool TreeViewOpen
        {
            get { return treeViewOpen; }
            set { treeViewOpen = value; OnPropertyChanged("TreeViewOpen"); }
        }

        private bool calendarOpen = false;
        public bool CalendarOpen
        {
            get { return calendarOpen; }
            set { calendarOpen = value; OnPropertyChanged("CalendarOpen"); }
        }

        private bool timeSheetOpen = false;
        public bool TimeSheetOpen
        {
            get { return timeSheetOpen; }
            set { timeSheetOpen = value; OnPropertyChanged("TimeSheetOpen"); }
        }

        private bool trayViewOpen = false;
        public bool TrayViewOpen
        {
            get { return trayViewOpen; }
            set { trayViewOpen = value; OnPropertyChanged("TrayViewOpen"); }
        }

        private bool showThumbnails = false;
        public bool ShowThumbnails
        {
            get { return showThumbnails; }
            set { showThumbnails = value; OnPropertyChanged("ShowThumbnails"); }
        }

        private string searchText = string.Empty;
        public string SearchText
        {
            get { return searchText; }
            set { searchText = value; OnPropertyChanged("SearchText"); }
        }

        private bool indexedSearch = false;
        public bool IndexedSearch
        {
            get { return indexedSearch; }
            set { indexedSearch = value; OnPropertyChanged("IndexedSearch"); }
        }

        private DateTime selectedDateTime = new DateTime();
        public DateTime SelectedDateTime
        {
            get { return selectedDateTime; }
            set { selectedDateTime = value; OnPropertyChanged("SelectedDateTime"); UpdateMonthly(); SetCurrentCalendarData(); }
        }

        private int selectedWeek = 0;
        public int SelectedWeek
        {
            get { return selectedWeek; }
            set { selectedWeek = value; OnPropertyChanged("SelectedWeek"); }
        }


        private ObservableCollection<CalendarData> monthlyNotes = new ObservableCollection<CalendarData>();
        public ObservableCollection<CalendarData> MonthlyNotes
        {
            get { return monthlyNotes; }
            set { monthlyNotes = value; OnPropertyChanged("MonthlyNotes"); }
        }


        private CalendarData currentCalendarData = new CalendarData();
        public CalendarData CurrentCalendarData
        {
            get { return currentCalendarData; }
            set { currentCalendarData = value; OnPropertyChanged("CurrentCalendarData"); SelectDateTime(); }
        }

        private TimeSheetData currentTimeSheet = new TimeSheetData();
        public TimeSheetData CurrentTimeSheet
        {
            get { return currentTimeSheet; }
            set { currentTimeSheet = value; OnPropertyChanged("CurrentTimeSheet"); }
        }

        private ObservableCollection<int> hours = new ObservableCollection<int>() { 1, 2, 3, 4, 5, 6, 7, 8 };

        public ObservableCollection<int> Hours
        {
            get { return hours; }
            set { hours = value; OnPropertyChanged("Hours"); }
        }

        private string currentTimeSheetProject = string.Empty;
        public string CurrentTimeSheetProject
        {
            get { return currentTimeSheetProject; }
            set { currentTimeSheetProject = value; OnPropertyChanged("CurrentTimeSheetProject"); UpdateTimeSheetSummary(); Debug.WriteLine(CurrentTimeSheetProject); }
        }

        public void NewTimeSheet()
        {
            CurrentCalendarData.TimeSheets.Add(new TimeSheetData() { Hours = 1, Project = "New"});
            CurrentCalendarData.TriggerDateStringUpdate();
        }

        public void RemoveTimeSheet()
        {
            CurrentCalendarData.TimeSheets.Remove(CurrentTimeSheet);
            CurrentCalendarData.TriggerDateStringUpdate();
        }

        private void UpdateTimeSheetSummary()
        {
            foreach (CalendarData calendarData in MonthlyNotes)
            {
                calendarData.SetCurrentTimeSheetProjectDiary(CurrentTimeSheetProject);
            }
        }


        private void UpdateMonthly()
        {

            if (SelectedDateTime != null)
            {

                SelectedWeek = ISOWeek.GetWeekOfYear(SelectedDateTime);

                MonthlyNotes.Clear();

                foreach (CalendarData data in Storage.General.CalendarList.Where(x => x.Date.Month == SelectedDateTime.Month).Where(x => x.Date.Year == SelectedDateTime.Year).OrderBy(x => x.Date))
                {
                    MonthlyNotes.Add(data);
                }

            }
        }

        private void SetCurrentCalendarData()
        {

            if (SelectedDateTime != null)
            {
                if (Storage.General.CalendarList.Where(x => x.Date == DateOnly.FromDateTime(SelectedDateTime)).Any())
                {
                    CurrentCalendarData = Storage.General.CalendarList.Where(x => x.Date == DateOnly.FromDateTime(SelectedDateTime)).FirstOrDefault();
                }
                else
                {
                    CalendarData newData = new CalendarData()
                    {
                        Date = DateOnly.FromDateTime(SelectedDateTime)
                    };

                    Storage.General.CalendarList.Add(newData);

                    CurrentCalendarData = newData;
                }
            }
        }

        public void SetCalendarMonth()
        {
            foreach (DateTime datetime in GetDates(SelectedDateTime.Year, SelectedDateTime.Month))
            {
                DateOnly date = DateOnly.FromDateTime(datetime);
                if (!Storage.General.CalendarList.Any(x=>x.Date == date))
                {
                    Storage.General.CalendarList.Add(new CalendarData() { Date = date });
                }
            }

            UpdateMonthly();
        }

        public static List<DateTime> GetDates(int year, int month)
        {
            return Enumerable.Range(1, DateTime.DaysInMonth(year, month))  // Days: 1, 2 ... 31 etc.
                             .Select(day => new DateTime(year, month, day)) // Map each day to a date
                             .ToList(); // Load dates into a list
        }

        public void RemoveCalendarNote()
        {
            Storage.General.CalendarList.Remove(CurrentCalendarData);
            CurrentCalendarData = Storage.General.CalendarList.FirstOrDefault();
            UpdateMonthly();
        }


        public void ResetDate()
        {
            SelectedDateTime = DateTime.Now;
            SelectedDateTime = DateTime.Now;
        }

        private void SelectDateTime()
        {

            if (CurrentCalendarData != null)
            {
                if (CurrentCalendarData.Date != DateOnly.FromDateTime(SelectedDateTime.Date))
                {
                    SelectedDateTime = CurrentCalendarData.Date.ToDateTime(TimeOnly.Parse("10:00 PM"));
                }
            }
        }

        public void ResetPreviewer()
        {
            PreviewVM.FileWorkerBusy = false;
        }

        public void OpenWhiteboard(Window mainWindow)
        {
            var window = new xPaintDia()
            {
                DataContext = this
            };
            window.FontFamily = mainWindow.FontFamily;
            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.ShowDialog(mainWindow);
        }

        public void OpenPreviewWindow(ThemeVariant theme)
        {
            PreviewWindow = new PreWindow()
            {
                DataContext = this
            };

            PreviewWindow.RequestedThemeVariant = theme;
            PreviewWindow.Show();
        }

        public void OpenInfoDia(Window mainWindow)
        {
            var window = new xProgDia()
            {
                DataContext = this
            };
            window.FontFamily = mainWindow.FontFamily;
            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.ShowDialog(mainWindow);
        }


        public void OpenColorDia(Window mainWindow)
        {
            var window = new xColorDia()
            {
                DataContext = this
            };


            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.FontCombo.SelectionChanged += SignalFontChanged;
            window.FontSizeCombo.SelectionChanged += SignalFontChanged;
            window.Focusable = true;
            window.ShowDialog(mainWindow);
        }

        private void NewPreviewContext()
        {

        }

        private void SignalFontChanged(object sender, SelectionChangedEventArgs e)
        {
            OnPropertyChanged("FontChanged");
        }

        public void OpenMetaEditDia(Window mainWindow)
        {
            var window = new xMetaDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.Focusable = true;
            window.ShowDialog(mainWindow);
        }

        public void OpenProjectEditDia(Window mainWindow)
        {
            var window = new xEditDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.Focusable = true;
            window.ShowDialog(mainWindow);
        }

        public void OpenProjectNewDia(Window mainWindow)
        {
            var window = new xNewDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.ShowDialog(mainWindow);
            window.ProjectName.Focus();
        }

        public void OpenTagDia(Window mainWindow)
        {
            var window = new xTagDia()
            {
                DataContext = this
            };

            window.TagMenuInput.Text = CurrentFile.Tagg;
            window.FontFamily = mainWindow.FontFamily;
            window.TagMenuInput.CaretIndex = window.TagMenuInput.Text.Length;

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.ShowDialog(mainWindow);
            window.TagMenuInput.Focus();
        }

        public void TryOpenRenameDia(Window mainWindow)
        {
            if (!CurrentFile.IsLocal)
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
            var window = new xRenameDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.Focusable = true;

            window.SetCurrentName(CurrentFile.Namn);

            window.NewNameInput.CaretIndex = window.NewNameInput.Text.Length;
            window.ShowDialog(mainWindow);

            window.NewNameInput.Focus();


        }

        public async Task OpenMessageDia(Window mainWindow)
        {
            var window = new xMessageDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.Focusable = true;
            window.SetMessage("Only available for files stored on C:\\");

            await window.ShowDialog(mainWindow);
        }


        public async Task ConfirmDeleteDia(Window mainWindow)
        {
            var window = new xDeleteDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.Focusable = true;
            await window.ShowDialog(mainWindow);
        }

        public void OnInfoDia(Window mainWindow)
        {
            var window = new xInfoDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.ShowDialog(mainWindow);
        }

        public void OnIndexDia(Window mainWindow)
        {
            var window = new xIndexDia()
            {
                DataContext = this
            };

            window.RequestedThemeVariant = mainWindow.ActualThemeVariant;
            window.FontFamily = mainWindow.FontFamily;
            window.ShowDialog(mainWindow);
        }

        private void OnGeneralChanged(object sender, PropertyChangedEventArgs e)
        {
            string val = e.PropertyName;

            if (val == "Color1" || val == "Color2" || val == "Color3" || val == "Color4")
            {
                SetWindowColors();
            }
        }

        public void TrySetPage()
        {
            if(FavPage != null)
            {
                PreviewVM.RequestPage1 = FavPage.PageNr;
            }
        }

        public void GetThumbnails()
        {
            foreach (FileData file in CurrentFiles)
            {
                file.RemoveThumbnail();

                byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);

                MuPDFDocument fileDocument = new MuPDFDocument(new MuPDFContext(), bytes, InputFileTypes.PDF);

                file.ThumbnailSource = "C:\\FIlePathManager\\Thumbnails\\" + file.Namn + ".jpeg";
                fileDocument.SaveImageAsJPEG(0, 1, file.ThumbnailSource, 20);

                fileDocument.Dispose();

            }
        }

        public void GetIndexedContent()
        {

            string indexPath = Storage.General.SavePath + "\\IndexedContent.json";

            if (System.IO.File.Exists(indexPath))
            {
                LoadIndexFile(indexPath);
            }

            if (IndexedContent == null)
            {
                IndexedContent = new ObservableCollection<ContentData>();
            }

            foreach (FileData file in CurrentFiles)
            {
                byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);

                MuPDFDocument fileDocument = new MuPDFDocument(new MuPDFContext(), bytes, InputFileTypes.PDF);
                ContentData existing = IndexedContent.FirstOrDefault(x => x.Filepath == file.Sökväg);

                IndexedContent.Remove(existing);

                ContentData content = new ContentData();

                content.Name = file.Namn;
                content.Filepath = file.Sökväg;
                content.PlainText = fileDocument.ExtractText();

                IndexedContent.Add(content);

                file.HasPlainText = true;

                fileDocument.Dispose();
            }

            SaveIndexFile(indexPath);

        }

        public void ClearIndexedContent()
        {
            string indexPath = Storage.General.SavePath + "\\IndexedContent.json";

            foreach(FileData file in CurrentFiles)
            {
                file.HasPlainText = false;

                if (IndexedContent != null && IndexedContent.Where(x => x.Filepath == file.Sökväg) != null)
                {
                    ContentData contentToRemove = IndexedContent.FirstOrDefault(x => x.Filepath == file.Sökväg);
                    IndexedContent.Remove(contentToRemove);
                    SaveIndexFile(indexPath);
                }
            }
        }

        private void LoadIndexFile(string indexPath)
        {               
            using (StreamReader streamReader = new StreamReader(indexPath))
            {
                string fileContent = streamReader.ReadToEnd();
                IndexedContent = JsonConvert.DeserializeObject<ObservableCollection<ContentData>>(fileContent);
            }

            List<string> indexedFiles = new List<string>();

            foreach(ContentData content in IndexedContent)
            {
                indexedFiles.Add(content.Filepath);
            }

            foreach(ProjectData project in Storage.StoredProjects)
            {
                foreach(FileData file in project.StoredFiles)
                {
                    if(indexedFiles.Contains(file.Sökväg))
                    {
                        file.HasPlainText = true;
                    }
                    else
                    {
                        file.HasPlainText = false;
                    }
                }
            }
        }

        private void SaveIndexFile(string indexPath)
        {
            if (!Directory.Exists(Storage.General.SavePath))
            {
                Directory.CreateDirectory(Storage.General.SavePath);
            }

            using (StreamWriter streamWriter = new StreamWriter(indexPath))
            {
                var data = JsonConvert.SerializeObject(IndexedContent);
                streamWriter.WriteLine(data);
            }
        }

        public void ClearThumbnails()
        {
            foreach (FileData file in CurrentFiles)
            {
                file.RemoveThumbnail();
            }
        }

        public void SetBookmark(PageData page)
        {
            FavPage = page;
        }

        public void AddBookmark(string pageName)
        {
            int pageNr = PreviewVM.CurrentPage1;
            PageData page = new PageData() { PageNr = pageNr, PageName = pageName };

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
            Storage.General.Collections.Add(name);
        }


        public void RemoveCollection()
        {
            if (CollectionContent.Count() > 0)
            {
                foreach (FileData file in CollectionContent)
                {
                    file.PartOfCollections.Remove(CurrentCollection);
                }
            }
            Storage.General.Collections.Remove(CurrentCollection);
        }

        public void AddFileToCollection(string collection)
        {
            CurrentFile.PartOfCollections.Add(collection);
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
                foreach(FileData file in project.StoredFiles.Where(x => x.PartOfCollections.Contains(CurrentCollection)))
                {
                    CollectionContent.Add(file);
                }
            }
        }

        public void RenameCollection(string newName)
        {
            if (!Storage.General.Collections.Contains(newName))
            {
                foreach (FileData file in CollectionContent)
                {
                    file.PartOfCollections.Remove(CurrentCollection);
                    file.PartOfCollections.Add(newName);
                }

                int index = Storage.General.Collections.IndexOf(CurrentCollection);
                Storage.General.Collections[index] = newName;

                CurrentCollection = newName;
            }
        }

        public async Task LoadFile(Visual window)
        {
            var topLevel = TopLevel.GetTopLevel(window);

            var jsonformat = new FilePickerFileType("Json format") { Patterns = new[] { "*.json" } };
            List<FilePickerFileType> formatlist = new List<FilePickerFileType>();
            formatlist.Add(jsonformat);
            IReadOnlyList<FilePickerFileType> fileformat = formatlist;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load File",
                AllowMultiple = false,
                FileTypeFilter = fileformat
            });

            if (files.Count > 0)
            {
                await using var stream = await files[0].OpenReadAsync();

                using var streamReader = new StreamReader(stream);
                string fileContent = await streamReader.ReadToEndAsync();

                DeserializeLoadFile(fileContent);

            }
        }

        public void LoadFileAuto()
        {
            using (StreamReader streamReader = new StreamReader(Storage.General.SavePath + "\\Projects.json"))
            {
                string fileContent = streamReader.ReadToEnd();
                DeserializeLoadFile(fileContent);
            }
        }

        public void DeserializeLoadFile(string fileContent)
        {
            Storage = new StoreData();
            try // Trying reading v.2 save file
            {
                Storage = JsonConvert.DeserializeObject<StoreData>(fileContent);
            }
            catch // If not, try read as v.1 save file
            {
                Storage.StoredProjects = JsonConvert.DeserializeObject<ObservableCollection<ProjectData>>(fileContent);
                RemoveProjects(Storage.StoredProjects.Where(x => x.Category == "Search").ToList());
                RemoveProjects(Storage.StoredProjects.Where(x => x.Category == "Favorites").ToList());

            }

            SetProjectlist();
            SetDefaultSelection();
            SetWindowColors();
            SetWindowBorders();
            SetAllowedTypes();
            GetGroups();
        }

        public async Task SaveFile(Avalonia.Visual window)
        {
            var topLevel = TopLevel.GetTopLevel(window);

            var jsonformat = new FilePickerFileType("Json format") { Patterns = new[] { "*.json" } };
            List<FilePickerFileType> formatlist = new List<FilePickerFileType>();
            formatlist.Add(jsonformat);
            IReadOnlyList<FilePickerFileType> fileformat = formatlist;


            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save File",
                FileTypeChoices = fileformat
            });

            if (file is not null)
            {
                await using var stream = await file.OpenWriteAsync();
                using var streamWriter = new StreamWriter(stream);
                var data = JsonConvert.SerializeObject(Storage);
                await streamWriter.WriteLineAsync(data);
            }
        }

        public async Task SaveFileAuto()
        {
            if (!Directory.Exists(Storage.General.SavePath))
            {
                Directory.CreateDirectory(Storage.General.SavePath);
            }

            using (StreamWriter streamWriter = new StreamWriter(Storage.General.SavePath + "\\Projects.json"))
            {
                var data = JsonConvert.SerializeObject(Storage);
                await streamWriter.WriteLineAsync(data);
            }
        }

        public void BackupSaveFile()
        {
            string backupDir = Storage.General.SavePath + "\\Backup_" + DateTime.Today.ToString("d");

            Directory.CreateDirectory(backupDir);

            System.IO.File.Copy(Storage.General.SavePath + "\\Projects.json", backupDir + "\\Projects.json", true);

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

        public void SetWindowColors()
        {
            var theme = new FluentTheme()
            {
                Palettes =
                {
                    [ThemeVariant.Dark] = new ColorPaletteResources() {RegionColor = Storage.General.Color1, Accent = Storage.General.Color2},
                    [ThemeVariant.Light] = new ColorPaletteResources() {RegionColor = Storage.General.Color3, Accent = Storage.General.Color4 }
                }
            };

            App.Current.Resources = theme.Resources;   

        }

        public void SetWindowBorders()
        {
            Storage.General.CornerRadiusVal = Storage.General.CornerRadiusVal;
            Storage.General.BorderVal = Storage.General.BorderVal;
        }

        public void AddFilesDrag(string path)
        {
            CurrentProject.Newfile(path);
            SetDefaultType();
        }

        public void SetCategory(string category)
        {
            SetProjecCategory(category);
            SetAllowedTypes();
        }

        public void SetAllowedTypes()
        {
            FileTypeSelection = GetAllowedTypes();
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
                if (CurrentProject.Meta_1 == true) { store += file.Namn + "\t"; };
                if (CurrentProject.Meta_2 == true) { store += file.Filtyp + "\t"; };
                if (CurrentProject.Meta_3 == true) { store += file.Uppdrag + "\t"; };
                if (CurrentProject.Meta_4 == true) { store += file.Tagg + "\t"; };
                if (CurrentProject.Meta_5 == true) { store += file.Färg + "\t"; };
                if (CurrentProject.Meta_6 == true) { store += file.Handling + "\t"; };
                if (CurrentProject.Meta_7 == true) { store += file.Status + "\t"; };
                if (CurrentProject.Meta_8 == true) { store += file.Datum + "\t"; };
                if (CurrentProject.Meta_9 == true) { store += file.Ritningstyp + "\t"; };
                if (CurrentProject.Meta_10 == true) { store += file.Beskrivning1 + "\t"; };
                if (CurrentProject.Meta_11 == true) { store += file.Beskrivning2 + "\t"; };
                if (CurrentProject.Meta_12 == true) { store += file.Beskrivning3 + "\t"; };
                if (CurrentProject.Meta_13 == true) { store += file.Beskrivning4 + "\t"; };
                if (CurrentProject.Meta_14 == true) { store += file.Revidering + "\t"; };
                if (CurrentProject.Meta_15 == true) { store += file.Sökväg + "\t"; };

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
            return PathStore.Count();
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
            int ntags = tags.Count();

            List<string[]> metadata = new List<string[]>();

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

            FileInfo file = new FileInfo("amit.txt");
            DateTime dt = file.CreationTime;

        }

        public void ClearMeta()
        {
            ClearSelectedMetadata();
        }

        public void Search(string searchtext)
        {
            SearchText = searchtext;

            if (IndexedSearch)
            {
                SearchIndex();
            }

            else
            {
                SeachFiles();
            }

            OnPropertyChanged("UpdateColumns");
        }

        public void SearchIndex()
        {
            string indexPath = Storage.General.SavePath + "//IndexedContent.json";

            if (IndexedContent == null)
            {
                if (System.IO.File.Exists(indexPath))
                {
                    LoadIndexFile(indexPath);
                }
                else
                {
                    IndexedContent = new ObservableCollection<ContentData>();
                }
            }

            FilteredFiles.Clear();

            List<string> filepaths = new List<string>();

            foreach (ContentData content in IndexedContent)
            {
                if (content.PlainText.Contains(SearchText,StringComparison.OrdinalIgnoreCase))
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
        }

        public void CheckSingleFile()
        {
            if (CurrentFile != null)
            {
                if (System.IO.File.Exists(CurrentFile.Sökväg))
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

            int n = CurrentProject.StoredFiles.Count();
            int i = 0;

            foreach (FileData file in CurrentProject.StoredFiles)
            {
                i++;

                if (System.IO.File.Exists(file.Sökväg))
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
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = file.Sökväg;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
            }
            catch (Exception e) { Debug.WriteLine(e); }
        }

        public void OpenMeta()
        {
            try
            {
                foreach (FileData file in CurrentFiles)
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = file.Sökväg + ".md";
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
            }
            catch { }
        }

        public void OpenDwg()
        {
            if (CurrentFile.Filtyp == "Drawing")
            {
                string dwgPathOld = CurrentFile.Sökväg.Replace("Ritning", "Ritdef").Replace("pdf", "dwg");
                string dwgPathNew = CurrentFile.Sökväg.Replace("Drawing", "Drawing Definition").Replace("pdf", "dwg");

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = dwgPathOld;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception)
                { }

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = dwgPathNew;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception)
                { }
            }
        }

        public void OpenDoc()
        {
            if (CurrentFile.Filtyp == "Document")
            {
                string docPath = CurrentFile.Sökväg.Replace("pdf", "docx");

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo();
                    psi.FileName = docPath;
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                catch (Exception)
                { }
            }

        }

        public void OpenPath()
        {
            try
            {
                string folderpath = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
            }

            catch (Exception e)
            { }
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
                SetAllowedTypes();
            }
            OnPropertyChanged("UpdateColumns");
        }

        public void ReselectProject()
        {
            SetProject(CurrentProject.Namn);
            SetAllowedTypes();
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

        public void NewProject(string name, string group = null, string category = "Project")
        {
            if (!Storage.StoredProjects.Any(x => x.Namn == name))
            {
                ProjectData newProject = new ProjectData { Namn = name, Parent = group, Category = category };

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

        public List<MenuItem> GetAllowedTypes()
        {

            if (CurrentProject.Category == "Project")
            {
                return new List<MenuItem>()
                {
                    new MenuItem(){Header="Drawing", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Document", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Other", Icon=new Label(){Content="○" } }
                };
            }

            if (CurrentProject.Category == "Library")
            {

                return new List<MenuItem>()
                {
                    new MenuItem(){Header="General", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Loads", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Concrete", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Steel", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Timber", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="FEM", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Mechanics", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Dynamics", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Geotechnics", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Other", Icon=new Label(){Content="○" } }
                };
            }

            if (CurrentProject.Category == "Archive")
            {
                return new List<MenuItem>()
                {
                    new MenuItem(){Header="Portal Frame", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Slab", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Beam", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Composite", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Concrete deck", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Integral", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Steel", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Post tension", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Substructure", Icon=new Label(){Content="○" } },
                    new MenuItem(){Header="Other", Icon=new Label(){Content="○" } }
                };
            }

            else
            {
                return new List<MenuItem>()
                {
                    new MenuItem(){Header="" }
                };
            }
        }

        public void SortProjects()
        {
            List<ProjectData> sortedLibrary = Storage.StoredProjects.Where(x => x.Category == "Library").OrderBy(x => x.Namn).ToList();
            List<ProjectData> sortedArchive = Storage.StoredProjects.Where(x => x.Category == "Archive").OrderBy(x => x.Namn).ToList();
            List<ProjectData> sortedProject = Storage.StoredProjects.Where(x => x.Category == "Project").OrderBy(x => x.Namn).ToList();

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
                Type = "All Types";
            }
        }

        public async Task SelectProjectAsync(ProjectData project)
        {
            CurrentProject = project;
        }

        public void SetProjecCategory(string name)
        {
            CurrentProject.Category = name;

            if (name != "Project")
            {
                CurrentProject.Parent = null;
            }

            SortProjects();
        }

        public void SetDefaultType()
        {
            Type = "All Types";
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
            Type = "All Types";
        }

        public void UpdateFilter()
        {
            FilteredFiles.Clear();

            if (Type != "All Types")
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
            OnPropertyChanged("NrFilteredFiles");
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


        public void AddAppendedFile(string filepath)
        {
            if (CurrentFile != null && CurrentFile.AppendedFiles.Where(x => x.Sökväg == filepath).Count() == 0)
            {
                CurrentFile.AppendedFiles.Add(new FileData()
                {
                    Namn = System.IO.Path.GetFileNameWithoutExtension(filepath),
                    Sökväg = filepath
                });

                SortAttachedFiles();
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


        private void SortAttachedFiles()
        {
            List<FileData> tempList = CurrentFile.AppendedFiles.OrderBy(x => x.Namn).ToList();
            CurrentFile.AppendedFiles.Clear();
            CurrentFile.AppendedFiles = new ObservableCollection<FileData>(tempList);
        }


        public void SeachFiles()
        {
            FilteredFiles.Clear();

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
        }

        public void RenameOriginal(string newName)
        {
            string oldName = CurrentFile.Namn;
            string oldPath = CurrentFile.Sökväg;

            if (oldName != newName && newName.Length > 0 && CurrentFile.IsLocal)
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




        public void WatermarkFiles(string text = "Arbetskopia")
        {

            string date = DateTime.Today.ToString("yyyy-MM-dd");
            string folder = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
            string outputPath = folder + "\\" + text + " " + date;

            System.IO.Directory.CreateDirectory(outputPath);

            foreach (FileData file in CurrentFiles)
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
                    Debug.WriteLine(filePath);
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

