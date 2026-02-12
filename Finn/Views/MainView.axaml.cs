using Avalonia.Controls;
using System;
using Avalonia.Interactivity;
using System.Linq;
using Finn.ViewModels;
using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;
using System.Threading;
using System.Collections.Generic;
using Finn.Model;
using System.IO;
using Avalonia.Data;
using Avalonia.Styling;
using System.Diagnostics;
using System.Drawing.Printing;
using Avalonia.Platform.Storage;


namespace Finn.Views;

public partial class MainView : UserControl, INotifyPropertyChanged
{
    public MainView() 
    {
        InitializeComponent();

        FileGrid.AddHandler(DataGrid.LoadedEvent, InitStartup);

        FileGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);
        CollectionContent.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);

        FetchMetadata.AddHandler(Button.ClickEvent, OnFetchFullMeta);

        FileGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestMain);
        FileGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectFiles);
        FileGrid.AddHandler(DragDrop.DropEvent, OnDrop);

        FolderGrid.AddHandler(DragDrop.DropEvent, OnFolderDrop);

        AppendixGrid.AddHandler(DragDrop.DropEvent, OnDropAppendedFiles);
        OtherFilesGrid.AddHandler(DragDrop.DropEvent, OnDropOtherFiles);
        OtherFilesGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenOtherFile);
        //AppendixGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectAppendedFiles);
        AppendixGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestAppendedFiles);

        CollectionContent.AddHandler(DataGrid.SelectionChangedEvent, SelectFavorite);
        CollectionContent.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestCollection);

        RecentGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectRecent);
        RecentGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestRecent);

        BookmarkGrid.AddHandler(DataGrid.SelectionChangedEvent, BookmarkSelected);

        HoursCombo.AddHandler(ComboBox.SelectionChangedEvent, OnUpdateTotalTime);


        InitMetaworker();
    }


    private BackgroundWorker MetaWorker = new BackgroundWorker();

    private Thread taskThread = null;

    private CancellationTokenSource cts = new CancellationTokenSource();

    public MainViewModel ctx = null;
    public PreviewViewModel pwr = null; 
    public GeneralData gnr = null;

    public List<DataGridRowEventArgs> Args = new List<DataGridRowEventArgs>();

    public ColumnDefinition a = new ColumnDefinition();
    public ColumnDefinition b = new ColumnDefinition();
    public ColumnDefinition c = new ColumnDefinition();
    public ColumnDefinition d = new ColumnDefinition();

    private bool PreviewTaskBusy = false;
    private bool PreviewReady = false;
    private double BitmapRes = 0.5;

    private void InitStartup(object sender, RoutedEventArgs e)
    {
        GetDatacontext();
        UpdateFont();
        //ctx.PreviewEmbeddedOpen = false;

        try
        {
            ctx.LoadFileAuto();
            UpdateFont();
            ctx.SelectedDateTime = DateTime.Now;

        }
        catch
        { }
    }

    public void GetDatacontext()
    {
        ctx = (MainViewModel)this.DataContext;
        pwr = ctx.PreviewVM;
        
        
        ctx.PropertyChanged += OnBindingCtx;
    }


    public void OnBindingCtx(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "FilteredFiles") { OnUpdateColumns(); }
        if (e.PropertyName == "UpdateColumns") { OnUpdateColumns(); }
        if (e.PropertyName == "Color1") { UpdateRowColor(); }
        if (e.PropertyName == "Color3") { UpdateRowColor(); }
        if (e.PropertyName == "TreeViewUpdate") { SetupTreeview(null, null); }

        if (e.PropertyName == "PreviewEmbeddedOpen") { UpdateMainGrid(); }
        if (e.PropertyName == "PreviewWindowOpen") { OnTogglePreviewWindow(); }
        if (e.PropertyName == "FontChanged") { UpdateFont(); }

    }

    private void UpdateFont()
    {
        var window = Window.GetTopLevel(this);

        window.FontFamily = (FontFamily)this.Resources[ctx.Storage.General.Font];
        window.FontSize = ctx.Storage.General.FontSize;
    }

    public void OnTogglePreviewWindow()
    {
        
        if (ctx.PreviewWindowOpen)
        {
            var window = Window.GetTopLevel(this);
            ThemeVariant theme = window.RequestedThemeVariant;
            ctx.OpenPreviewWindow(theme);
        }

        else
        {
            if (ctx.PreviewWindow != null)
            {
                ctx.PreviewWindow.Close();
            }
        }
    }

    public void UpdateMainGrid()
    {
        if (ctx.PreviewEmbeddedOpen)
        {
            MainGrid.ColumnDefinitions[2] = new ColumnDefinition(5, GridUnitType.Pixel);
            MainGrid.ColumnDefinitions[3] = new ColumnDefinition(2.5, GridUnitType.Star);

            EmbeddedPreview.SetRenderer();
        }
        else
        {
            MainGrid.ColumnDefinitions[2] = new ColumnDefinition(0, GridUnitType.Star);
            MainGrid.ColumnDefinitions[3] = new ColumnDefinition(0, GridUnitType.Star);
        }

        MainGrid.ColumnDefinitions[1] = new ColumnDefinition(1, GridUnitType.Star);
    }

    private void OnFolderDrop(object sender, DragEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;

        var items = e.Data.GetFiles();

        foreach (var item in items)
        {
            string path = item.Path.LocalPath;

            if (Directory.Exists(path))
            {
                ctx.CurrentProject.Folders.Add(new Model.FolderData()
                {
                    Name = Path.GetFileName(Path.GetDirectoryName(path)),
                    Types = "PDF",
                    Path = path
                });
            }
        }
    }

    private void NewTimeSheetProject(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;
        ctx.Storage.General.TimeProjects.Add(new TimeSheetProjectData() { Project = "New Project"});
    }

    private void RemoveTimeSheetProject(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;
        ctx.Storage.General.TimeProjects.Remove(ctx.CurrentTimeSheetProject);
    }

    public void OnSearch(object sender, RoutedEventArgs e)
    {
        ctx.Search();
        OnUpdateColumns();
    }

    public void OnStartSearch(object sender, KeyEventArgs e)
    {
        if(e.Key == Key.Enter)
        {
            OnSearch(null, null);
        }
    }

    public void OnDrop(object sender, DragEventArgs e)
    {
        var items = e.Data.GetFiles();

        foreach(var item in items)
        {
            if (item.Path.IsFile)
            {
                string path = item.Path.LocalPath;
                string type = Path.GetExtension(path);

                if (type == ".pdf")
                {
                    ctx.AddFilesDrag(path);
                }
            }
        }
        SetupTreeview(null, null);
    }

    public void OnDropAppendedFiles(object sender, DragEventArgs e)
    {
        var items = e.Data.GetFiles();

        if (ctx.CurrentFile == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (File.Exists(item.Path.LocalPath))
            {
                string path = item.Path.LocalPath;
                string type = Path.GetExtension(path);

                if (type == ".pdf")
                {
                    ctx.AddAppendedFile(path);
                }
            }

            if (Directory.Exists(item.Path.LocalPath))
            {
                FolderData newfolder = new FolderData()
                {
                    Name = Path.GetFileName(Path.GetDirectoryName(item.Path.LocalPath)),
                    AttachToFile = ctx.CurrentFile.Namn,
                    AttachToFilePath = ctx.CurrentFile.Sökväg,
                    Types = "PDF",
                    Path = item.Path.LocalPath
                };

                ctx.CurrentProject.Folders.Add(newfolder);
                ctx.SyncFolder(newfolder);
            }

        }
    }

    public void OnDropOtherFiles(object sender, DragEventArgs e)
    {
        var items = e.Data.GetFiles();

        if (ctx.CurrentFile == null)
        {
            return;
        }

        foreach (var item in items)
        {
            if (File.Exists(item.Path.LocalPath))
            {
                string path = item.Path.LocalPath;
                ctx.AddOtherFile(path);
            }

            if (Directory.Exists(item.Path.LocalPath))
            {
                FolderData newfolder = new FolderData()
                {
                    Name = Path.GetFileName(Path.GetDirectoryName(item.Path.LocalPath)),
                    AttachToFile = ctx.CurrentFile.Namn,
                    AttachToFilePath = ctx.CurrentFile.Sökväg,
                    Types = "Other Files",
                    Path = item.Path.LocalPath
                };

                ctx.CurrentProject.Folders.Add(newfolder);
                ctx.SyncFolder(newfolder);
            }

        }
    }

    private void SetupTreeview(object sender, RoutedEventArgs e)
    {
        MainTree.Items.Clear();
        ctx.GetGroups();
        TreeViewItem selectedItem = new TreeViewItem();


        List<string> typeList = new List<string>() { "Archive", "Library", "Project" };


        foreach (string type in typeList)
        {
            List<TreeViewItem> items = new List<TreeViewItem>();

            IEnumerable<ProjectData> projects = ctx.Storage.StoredProjects.Where(x => x.Category == type);

            if (projects.Count() != 0)
            {
                foreach (ProjectData project in projects)
                {

                    if (project.Parent == null || project.Parent == "")
                    {
                        List<TreeViewItem> fileTypeTree = new List<TreeViewItem>();
                        foreach (string filetype in project.StoredFiles.Select(x => x.Filtyp).Distinct())
                        {
                            int nfiles = project.StoredFiles.Where(x => x.Filtyp == filetype).Count();

                            TreeViewItem newItemType = new TreeViewItem()
                            {
                                FontSize = 13,
                                FontWeight = FontWeight.Light,
                                Header = filetype + "  (" + nfiles + ")",
                                Tag = project.Namn
                            };

                            if (ctx.CurrentProject != null && ctx.CurrentFile != null)
                            {
                                if (ctx.CurrentProject == project && ctx.CurrentFile.Filtyp == filetype)
                                {
                                    selectedItem = newItemType;
                                }
                            }

                            if (project.Foreground != Color.Parse("#FFFFFFFF"))
                            {
                                newItemType.Foreground = Brush.Parse(project.Foreground.ToString());
                            }

                            fileTypeTree.Add(newItemType);
                        }
                        TreeViewItem newItem = new TreeViewItem()
                        {
                            FontSize = 15,
                            FontWeight = FontWeight.Normal,
                            FontStyle = FontStyle.Normal,
                            Header = project.Namn,
                            IsExpanded = (project == ctx.CurrentProject),
                            Tag = "All Types",
                            ItemsSource = fileTypeTree
                        };


                        if (project.Foreground != Color.Parse("#FFFFFFFF"))
                        {
                            newItem.Foreground = Brush.Parse(project.Foreground.ToString());
                        }

                        items.Add(newItem);
       
    
                    }
                }

                if (type == "Project")
                {
                    foreach (string group in ctx.Groups)
                    {
                        IEnumerable<ProjectData> groupedProject = ctx.Storage.StoredProjects.Where(x => x.Parent == group);
                        List<TreeViewItem> groupedTree = new List<TreeViewItem>();

                        foreach (ProjectData project in groupedProject)
                        {
                            List<TreeViewItem> fileTypeTree = new List<TreeViewItem>();
                            foreach (string filetype in project.StoredFiles.Select(x => x.Filtyp).Distinct())
                            {
                                int nfiles = project.StoredFiles.Where(x => x.Filtyp == filetype).Count();
                                TreeViewItem newItem = new TreeViewItem()
                                {
                                    FontSize = 13,
                                    FontWeight = FontWeight.Light,
                                    Header = filetype + "  (" + nfiles + ")",
                                    Tag = project.Namn
                                };

                                if (ctx.CurrentProject != null && ctx.CurrentFile != null)
                                {
                                    if ( ctx.CurrentProject == project && ctx.CurrentFile.Filtyp == filetype )
                                    {
                                        selectedItem = newItem;
                                    }
                                }

                                if (project.Foreground != Color.Parse("#FFFFFFFF"))
                                {
                                    newItem.Foreground = Brush.Parse(project.Foreground.ToString());
                                }

                                fileTypeTree.Add(newItem);


                            }

                            TreeViewItem newGroupedItem = new TreeViewItem()
                            {
                                FontSize = 15,
                                FontWeight = FontWeight.Normal,
                                FontStyle = FontStyle.Normal,
                                Header = project.Namn,
                                IsExpanded = (project == ctx.CurrentProject),
                                Tag = "All Types",
                                ItemsSource = fileTypeTree
                            };

                            if (project.Foreground != Color.Parse("#FFFFFFFF"))
                            {
                                newGroupedItem.Foreground = Brush.Parse(project.Foreground.ToString());
                            }

                            groupedTree.Add(newGroupedItem);
                        }

                        items.Add(
                            new TreeViewItem()
                            {
                                FontSize = 15,
                                FontWeight = FontWeight.Bold,
                                FontStyle = FontStyle.Normal,
                                Header = group,
                                IsExpanded = true,
                                Tag = "Group",
                                ItemsSource = groupedTree
                            }
                        );
                    }
                }

                MainTree.Items.Add(
                    new TreeViewItem()
                    {
                        FontSize = 16,
                        FontWeight = FontWeight.Bold,
                        FontStyle = FontStyle.Italic,
                        Header = type,
                        Tag = "Header",
                        IsExpanded = true,
                        ItemsSource = items
                    }
                );
            }

        }

        Debug.WriteLine(selectedItem.Header);
        selectedItem.IsSelected = true;
    }

    public void OnTreeviewSelected(object sender, SelectionChangedEventArgs e)
    {
        object selected = MainTree.SelectedItem;


        if (selected != null)
        {
    
            TreeViewItem selectedTree = (TreeViewItem)selected;

            if(selectedTree.Tag == "Header" || selectedTree.Tag == "Group")
            {
                MainTree.SelectedItem = null;
                MainTree.ContextMenu.IsEnabled = false;
                return;
            }

            else
            {

                TreeViewItem parentTree = (TreeViewItem)selectedTree.Parent;
                

                MainTree.ContextMenu.IsEnabled = true;

                if (selectedTree.Tag == "All Types")
                {
                    ctx.SelectType("All Types");
                    ctx.SelectProject(selectedTree.Header.ToString());
                }
                else
                {
                    ctx.SelectType(selectedTree.Header.ToString().Split("  ")[0]);
                    ctx.SelectProject(selectedTree.Tag.ToString());
                }
            }

            OnUpdateColumns();
        }
    }


    private void Border_PointerPressed(object sender, RoutedEventArgs args)
    {
        var ctl = sender as Control;
        if (ctl != null)
        {
            FlyoutBase.ShowAttachedFlyout(ctl);
        }
    }

    private void SetPreviewRequestMain(object sender, RoutedEventArgs r)
    {
        FileData file = (FileData)FileGrid.SelectedItem;
        SetPreviewRequest(file);
        RecentGrid.SelectedItem = null;

        pwr.AddRecentFile(file);

    }

    private void SetPreviewRequestCollection(object sender, RoutedEventArgs r)
    {
        FileData file = (FileData)CollectionContent.SelectedItem;
        SetPreviewRequest(file);
        pwr.AddRecentFile(file);
    }

    private void SetPreviewRequestAppendedFiles(object sender, RoutedEventArgs r)
    {
        FileData file = (FileData)AppendixGrid.SelectedItem;
        SetPreviewRequest(file);
        pwr.AddRecentFile(file);
    }

    private void SetPreviewRequestRecent(object sender, RoutedEventArgs r)
    {
        FileData file = (FileData)RecentGrid.SelectedItem;
        SetPreviewRequest(file);
    }

    private async void SetPreviewRequest(FileData file)
    {
        if (ctx.PreviewEmbeddedOpen || ctx.PreviewWindowOpen)
        {
            CheckStatusSingleFile();

            if (file != null && file.IsValidPdf()) 
            {
                int startPage = file.DefaultPage;

                pwr.SetupPage(startPage);
                pwr.RequestFile = file;

                pwr.ToggleVisibility(false);

                if (ctx.IndexedSearch && SearchText.Text != null)
                {
                    await pwr.SetFileAsync(SearchText.Text);
                }
                else
                {
                    await pwr.SetFileAsync();
                }
            }
            else
            {
                await pwr.CloseRendererAsync();
            }
        }
    }

    private void EditColor(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        string color = menuItem.Tag.ToString();

        if (color != "None")
        {
            ctx.AddColor(color);
        }

        DeselectItems();
        UpdateRowColor();
    }

    private void EditType(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;

        ctx.EditType(menuItem.SelectedItem.ToString());
        SetupTreeview(null, null);
    }

    private void DeselectItems()
    {
        FileGrid.SelectedItem = null;
    }


    private void OnClearFiles(object sender, RoutedEventArgs e)
    {
        ctx.ClearAll();

       DeselectItems();
       UpdateRowColor();
    }


    private void OnCheckStatusSingleFile(object sender, RoutedEventArgs e)
    {
        CheckStatusSingleFile();
    }


    private void CheckStatusSingleFile()
    {
        ctx.CheckSingleFile();
        UpdateRowColor();
    }


    private async void OnCheckProjectFiles(object sender, RoutedEventArgs e)
    {
        await ctx.CheckProjectFiles();
        DeselectItems();
        UpdateRowColor();
    }


    async void OnRemoveProject(object sender, RoutedEventArgs e)
    {
        if (ctx.Storage.StoredProjects.Count() > 1)
        {
            Window window = (MainWindow)Window.GetTopLevel(this);
            await ctx.ConfirmDeleteDia(window);

            if (ctx.Confirmed)
            {
                ctx.RemoveProject();
                SetupTreeview(null, null);
            }
        }
    }

    async void OnRemoveFolder(object sender, RoutedEventArgs e)
    {
        if (ctx.CurrentFolder != null)
        {
            Window window = (MainWindow)Window.GetTopLevel(this);
            await ctx.ConfirmDeleteDia(window);

            if (ctx.Confirmed)
            {
                ctx.RemoveFolder();
            }
        }
    }


    private void OnAddFiles(object sender, RoutedEventArgs e)
    {
        ctx.AddFile(this);
        SetupTreeview(null, null);
    }


    private void OnFetchSingleMeta(object sender, RoutedEventArgs e)
    {
        ProgressStatus.Content = "Fetching Metadata";

        ctx.SelectFilesForMetaworker(true);
        MetaWorker.RunWorkerAsync();
    }


    private void OnFetchFullMeta(object sender, RoutedEventArgs e)
    {
        ProgressStatus.Content = "Fetching Metadata";

        ctx.SelectFilesForMetaworker(false);
        MetaWorker.RunWorkerAsync();
    }


    private void InitMetaworker()
    {
        MetaWorker.DoWork += MetaWorkerDoWork;
        MetaWorker.WorkerReportsProgress = true;
        MetaWorker.ProgressChanged += MetaWorkerProgress;
        MetaWorker.RunWorkerCompleted += MetaWorkerRunWorkerCompleted;
    }


    private void MetaWorkerDoWork(object sender, DoWorkEventArgs e)
    {
        int nPaths = ctx.GetNrSelectedFiles();

        for (int k = 0; k < nPaths; k++)
        {
            ctx.GetMetadata(k);

            int percentage = (k + 1) * 100 / nPaths;
            MetaWorker.ReportProgress(percentage);
        }
    }


    private void MetaWorkerProgress(object sender, ProgressChangedEventArgs e)
    {
        ProgressBar.Value = e.ProgressPercentage;
    }


    private void MetaWorkerRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
    {
        ctx.SetMeta();
        ProgressStatus.Content = "";
        ProgressBar.Value = 0;
    }


    private void BookmarkSelected(object sender, RoutedEventArgs e)
    {
        if (ctx.PreviewEmbeddedOpen || ctx.PreviewWindowOpen)
        {
            PageData page = (PageData)BookmarkGrid.SelectedItem;
            ctx.SetBookmark(page);
        }
    }

    private void OnAddBookmark(object sender, RoutedEventArgs e)
    {
        if (ctx.PreviewEmbeddedOpen || ctx.PreviewWindowOpen)
        {
            ctx.AddBookmark(BookmarkInput.Text);
            BookmarkInput.Clear();
        }
    }


    private void OnRenameBookmark(object sender, RoutedEventArgs e)
    {
        ctx.RenameBookmark(BookmarkInput.Text);
        BookmarkInput.Clear();
    }


    private void OnRemoveBookmark(object sender, RoutedEventArgs e)
    {
        PageData page = (PageData)BookmarkGrid.SelectedItem;
        ctx.RemoveBookmark(page);
    }

    private void SelectFavorite(object sender, RoutedEventArgs e)
    {
        IList<FileData> files = CollectionContent.SelectedItems.Cast<FileData>().ToList();

        FileData file = files.FirstOrDefault();
        if (file != null)
        {
            if (file.Uppdrag != string.Empty && file.Filtyp != string.Empty)
            {
                ctx.SelectProject(file.Uppdrag);
                ctx.SelectType(file.Filtyp);
            }
        }

        DeselectItems();
        ctx.select_files(files);
        ctx.UpdateTreeview();
    }

    private void SelectRecent(object sender, RoutedEventArgs e)
    {
        IList<FileData> files = RecentGrid.SelectedItems.Cast<FileData>().ToList();

        FileData file = files.FirstOrDefault();

        if (file != null)
        {
            if (file.Uppdrag != string.Empty && file.Filtyp != string.Empty)
            {
                ctx.SelectProject(file.Uppdrag);
                ctx.SelectType(file.Filtyp);
            }
        }

        DeselectItems();
        ctx.select_files(files);
        ctx.UpdateTreeview();
    }



    private void OnNewCollection(object sender, RoutedEventArgs e)
    {
        string text = CollectionInput.Text;

        if(text != null && text.ToString().Length > 0)
        {
            ctx.NewCollection(CollectionInput.Text);
            CollectionInput.Clear();
        }
    }

    private void OnRenameCollection(object sender, RoutedEventArgs e)
    {
        string text = CollectionInput.Text;

        if (text != null && text.ToString().Length > 0)
        {
            ctx.RenameCollection(CollectionInput.Text);
            CollectionInput.Clear();
        }
    }

    private void OnAddToCollection(object sender, RoutedEventArgs e)
    {
        MenuItem source = e.Source as MenuItem;

        if (source.Header.ToString()!= "Collection")
        {
            ctx.AddFileToCollection(source.Header.ToString());
        }
    }

    private void OnMoveFile(object sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        ProjectData moveToProject = menuItem.SelectedItem as ProjectData;
        ctx.MoveSelectedFiles(moveToProject);
        SetupTreeview(null, null);
    }
    

    private void OnUpdateTotalTime(object sender, RoutedEventArgs args)
    {
        if (ctx.CurrentCalendarData != null)
        {
            ctx.CurrentCalendarData.TriggerDateStringUpdate();
        }
    }

    async void OnRemoveAttachedFile(object sender, RoutedEventArgs e)
    {
        Window window = (MainWindow)Window.GetTopLevel(this);
        await ctx.ConfirmDeleteDia(window);

        if (ctx.Confirmed)
        {
            IList<FileData> files = AppendixGrid.SelectedItems.Cast<FileData>().ToList();

            ctx.RemoveAttachedFile(files);
        }
    }

    async void OnRemoveOtherFile(object sender, RoutedEventArgs e)
    {
        Window window = (MainWindow)Window.GetTopLevel(this);
        await ctx.ConfirmDeleteDia(window);

        if (ctx.Confirmed)
        {
            OtherData file = (OtherData)OtherFilesGrid.SelectedItem;
            
            ctx.RemoveOtherFile(file);
        }
    }

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        ctx.OpenFile();
    }

    private void OnOpenAppendedFile(object sender, RoutedEventArgs e)
    {
        FileData file = (FileData)AppendixGrid.SelectedItem;
        if (file != null)
        {
            ctx.OpenFileDirect(file.Sökväg);
        }
    }

    private void OnOpenOtherFile(object sender, RoutedEventArgs e)
    {
        OtherData file = (OtherData)OtherFilesGrid.SelectedItem;
        if (file != null)
        {
            ctx.OpenFileDirect(file.Filepath);
        }
    }

    private void OnOpenAppendedFolder(object sender, RoutedEventArgs e)
    {
        FileData file = (FileData)AppendixGrid.SelectedItem;

        if (file != null)
        {
            ctx.OpenPathDirect(file.Sökväg);
        }
    }

    private void OnOpenOtherFolder(object sender, RoutedEventArgs e)
    {
        OtherData file = (OtherData)OtherFilesGrid.SelectedItem;

        if (file != null)
        {
            ctx.OpenPathDirect(file.Filepath);
        }
    }

    private void SelectFiles(object sender, RoutedEventArgs e)
    {
        IList<FileData> files = FileGrid.SelectedItems.Cast<FileData>().ToList();
        CollectionContent.SelectedItem = null;

        ctx.select_files(files);
    }

    private void SelectAppendedFiles(object sender, RoutedEventArgs e)
    {
        IList<FileData> files = AppendixGrid.SelectedItems.Cast<FileData>().ToList();

        ctx.select_files(files);
    }

    private async void OnLoadFile(object sender, RoutedEventArgs e)
    {
        await ctx.LoadFile(this);
        SetupTreeview(null, null);
        UpdateFont();
    }

    private async void OnSaveFile(object sender, RoutedEventArgs e)
    {
        await ctx.SaveFile(this);
    }

    private async void OnSaveFileAuto(object sender, RoutedEventArgs e)
    {
        await ctx.SaveFileAuto();
    }

    async void OnRemoveFiles(object sender, RoutedEventArgs e)
    {
        Window window = (MainWindow)Window.GetTopLevel(this);
        await ctx.ConfirmDeleteDia(window);

        if (ctx.Confirmed)
        {
            ctx.RemoveSelectedFiles();
            ctx.UpdateFilter();
            SetupTreeview(null, null);
        }
    }

    private void OnUpdateColumns()
    {
        FileGrid.Columns[0].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[1].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[2].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[3].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[4].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[5].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[6].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[7].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[8].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        FileGrid.Columns[9].Width = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);

        FileGrid.UpdateLayout();

    }

    private void DataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        Args.Add(e);

        var dataObject = e.Row.DataContext as FileData;
        e.Row.Classes.Clear();

        if (dataObject != null && dataObject.Färg == "") { e.Row.Classes.Clear(); }

        if (dataObject != null && dataObject.FileStatus == "Missing") { e.Row.Classes.Add("RedForeground"); }
        if (dataObject != null && dataObject.Sökväg == string.Empty) { e.Row.Classes.Add("Placeholder"); }

        if (dataObject != null && dataObject.Färg == "Yellow") { e.Row.Classes.Add("Yellow"); }
        if (dataObject != null && dataObject.Färg == "Orange") { e.Row.Classes.Add("Orange"); }
        if (dataObject != null && dataObject.Färg == "Brown") { e.Row.Classes.Add("Brown"); }
        if (dataObject != null && dataObject.Färg == "Green") { e.Row.Classes.Add("Green"); }
        if (dataObject != null && dataObject.Färg == "Blue") { e.Row.Classes.Add("Blue"); }
        if (dataObject != null && dataObject.Färg == "Red") { e.Row.Classes.Add("Red"); }
        if (dataObject != null && dataObject.Färg == "Magenta") { e.Row.Classes.Add("Magenta"); }
    }

    private void UpdateRowColor()
    {

        foreach (DataGridRowEventArgs e in Args)
        {
            var dataObject = e.Row.DataContext as FileData;

            e.Row.Classes.Clear();

            if (dataObject != null && dataObject.FileStatus == "Missing") { e.Row.Classes.Add("RedForeground"); }
            if (dataObject != null && dataObject.Sökväg == string.Empty) { e.Row.Classes.Add("Placeholder"); }

            if (dataObject != null && dataObject.Färg == "Yellow") { e.Row.Classes.Add("Yellow"); }
            if (dataObject != null && dataObject.Färg == "Orange") { e.Row.Classes.Add("Orange"); }
            if (dataObject != null && dataObject.Färg == "Brown") { e.Row.Classes.Add("Brown"); }
            if (dataObject != null && dataObject.Färg == "Green") { e.Row.Classes.Add("Green"); }
            if (dataObject != null && dataObject.Färg == "Blue") { e.Row.Classes.Add("Blue"); }
            if (dataObject != null && dataObject.Färg == "Red") { e.Row.Classes.Add("Red"); }
            if (dataObject != null && dataObject.Färg == "Magenta") { e.Row.Classes.Add("Magenta"); }
        }
    }


    private void RaisePropertyChanged(string propName)
    {
        if (PropertyChanged != null)
            PropertyChanged(this, new PropertyChangedEventArgs(propName));
    }
    public event PropertyChangedEventHandler PropertyChanged;

    private void Binding(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
    }
}

