using Avalonia.Controls;
using System;
using Avalonia.Interactivity;
using System.Linq;
using Finn.ViewModels;
using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Input;
using System.Collections.Generic;
using Finn.Model;
using System.IO;
using Avalonia.Styling;
using System.Diagnostics;
using System.Threading;
using Avalonia.VisualTree;
using Avalonia.Threading;

namespace Finn.Views;

public partial class MainView : UserControl
{
    private readonly BackgroundWorker _metaWorker = new() { WorkerReportsProgress = true };
    private readonly HashSet<DataGridRow> _trackedRows = [];
    private readonly Dictionary<DataGridRow, (FileData Data, PropertyChangedEventHandler Handler)> _rowBindings = [];

    private MainViewModel _ctx = null!;
    private PreviewViewModel _pwr = null!;

    public MainView()
    {
        InitializeComponent();

        FileGrid.AddHandler(DataGrid.LoadedEvent, InitStartup);
        FileGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);
        FileGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestMain);
        FileGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectFiles);
        FileGrid.AddHandler(DragDrop.DropEvent, OnDrop);

        // Drag-and-drop visual hints
        MainGrid.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        MainGrid.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        MainGrid.AddHandler(DragDrop.DropEvent, OnDragDropCompleted);

        CollectionContent.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);
        CollectionContent.AddHandler(DataGrid.SelectionChangedEvent, SelectFavorite);

        AppendixGrid.AddHandler(DragDrop.DropEvent, OnDropAppendedFiles);
        AppendixGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenAppendedFile);
        AppendixGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestAppendedFiles);

        OtherFilesGrid.AddHandler(DragDrop.DropEvent, OnDropOtherFiles);
        OtherFilesGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenOtherFile);

        RecentGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectRecent);

        BookmarkGrid.AddHandler(DataGrid.SelectionChangedEvent, BookmarkSelected);

        VersionsGrid.AddHandler(DataGrid.DoubleTappedEvent, OnVersionDoubleTapped);
        VersionsGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectVersion);

        InitMetaworker();
    }

    // Removed file-open debugger / benchmark command and handler

    private async void OnOpenAnalogClock(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new Finn.Dialogs.AnalogWatchDialog();
        if (this.FindAncestorOfType<Window>() is Window owner)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show(); // Not awaited, just show the window
        }
    }

    private async void OpenTimesheetWindow(object? sender, RoutedEventArgs e)
    {
        var window = new Finn.Views.TimesheetWindow();
        // Attach the concrete MainViewModel instance when available to avoid
        // stale/old DataContext instances after a reload. Fall back to the
        // current DataContext if _ctx hasn't been initialized yet.
        var dc = _ctx ?? (DataContext as MainViewModel);
        if (dc != null)
            window.AttachDataContext(dc);
        else
            window.AttachDataContext(this.DataContext);

        Debug.WriteLine($"Opening TimesheetWindow with Calendar instance: {((dc as MainViewModel)?.Calendar?.GetHashCode().ToString() ?? "null")}");

        if (this.FindAncestorOfType<Window>() is Window owner)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    #region Initialization

    private void InitStartup(object? sender, RoutedEventArgs e)
    {
        _ctx = (MainViewModel)DataContext!;
        _pwr = _ctx.PreviewVM;
        _ctx.PropertyChanged += OnViewModelPropertyChanged;
        _ctx.UI.PropertyChanged += OnUIPropertyChanged;

        UpdateFont();
        UpdateMainGrid();

        try
        {
            _ctx.LoadFileAuto();
            UpdateFont();
            // initialize calendar selected date on separate viewmodel
            _ctx.Calendar.SelectedDateTime = DateTime.Now;
            // Initialize calendar persistence after projects have been loaded so
            // the service uses the SavePath configured in Projects.json.
            try
            {
                // Load calendar storage directly into the CalendarViewModel
                _ctx.Calendar.LoadOrCreateStorage(MainViewModel.SavePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }
        catch { }

        UpdateEmptyState();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.FilteredFiles):
            case "UpdateColumns":
                OnUpdateColumns();
                UpdateEmptyState();
                break;
            case nameof(MainViewModel.PreviewEmbeddedOpen):
                UpdateMainGrid();
                break;
            case nameof(MainViewModel.PreviewWindowOpen):
                OnTogglePreviewWindow();
                break;
            case "FontChanged":
                UpdateFont();
                break;
            case "TreeViewUpdate":
                _ctx.BuildTreeData();
                break;
            case nameof(MainViewModel.NrFilteredFiles):
                UpdateEmptyState();
                break;
            case nameof(MainViewModel.CurrentFile):
                UpdateAttachedEmptyState();
                break;
        }
    }

    private void UpdateFont()
    {
        var window = TopLevel.GetTopLevel(this);
        if (window == null) return;

        // Read font settings from the UI viewmodel
        window.FontFamily = (FontFamily)Resources[_ctx.UI.Font]!;
        window.FontSize = _ctx.UI.FontSize;
    }

    #endregion

    #region Preview Window & Grid

    private void OnUIPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(_ctx.UI.PreviewEmbeddedOpen):
                if (_ctx.UI.PreviewEmbeddedOpen && _ctx.PreviewWindowOpen)
                    _ctx.PreviewWindowOpen = false;
                UpdateMainGrid();
                break;
            case nameof(_ctx.UI.Color1):
            case nameof(_ctx.UI.Color3):
            case nameof(_ctx.UI.DarkMode):
                _ctx.SyncPreviewRegionColor();
                break;
            case nameof(_ctx.UI.ColorTagDot):
            case nameof(_ctx.UI.ColorTagRow):
                UpdateRowColor();
                break;
        }
    }

    private void OnTogglePreviewWindow()
    {
        if (_ctx.PreviewWindowOpen)
        {
            var window = TopLevel.GetTopLevel(this);
            if (window is not null)
                _ctx.OpenPreviewWindow(window.RequestedThemeVariant);
        }
        else
        {
            _ctx.PreviewWindow?.Close();
        }
    }

    private void UpdateMainGrid()
    {
        if (_ctx.UI.PreviewEmbeddedOpen)
        {
            MainGrid.ColumnDefinitions[2] = new ColumnDefinition(10, GridUnitType.Pixel);
            MainGrid.ColumnDefinitions[3] = new ColumnDefinition(2.5, GridUnitType.Star) { MinWidth = 400 };
            MainGrid.ColumnDefinitions[1] = new ColumnDefinition(1, GridUnitType.Star) { MinWidth = 300 };
            EmbeddedPreview.SetRenderer();
        }
        else
        {
            MainGrid.ColumnDefinitions[2] = new ColumnDefinition(0, GridUnitType.Star);
            MainGrid.ColumnDefinitions[3] = new ColumnDefinition(0, GridUnitType.Star);
            MainGrid.ColumnDefinitions[1] = new ColumnDefinition(1, GridUnitType.Star);
        }
    }

    #endregion

    #region Drag & Drop (extract paths only, delegate to ViewModel)

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var (files, folders) = ExtractDroppedFilesAndFolders(e, extension: ".pdf");
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        if (files.Count > 0)
            await _ctx.AddDroppedFilesAsync(files, window);
        if (folders.Count > 0)
            _ctx.AddDroppedFolders(folders);
        UpdateEmptyState();
    }

    private void OnDropAppendedFiles(object? sender, DragEventArgs e)
    {
        var (files, folders) = ExtractDroppedFilesAndFolders(e, extension: ".pdf");
        _ctx.AddDroppedAppendedFiles(files, folders);
        UpdateAttachedEmptyState();
    }

    private void OnDropOtherFiles(object? sender, DragEventArgs e)
    {
        var (files, folders) = ExtractDroppedFilesAndFolders(e);
        _ctx.AddDroppedOtherFiles(files, folders);
        UpdateAttachedEmptyState();
    }

    /// <summary>
    /// Extracts local paths from a drag event, filtering by type and optional extension.
    /// </summary>
    private static List<string> ExtractDroppedPaths(DragEventArgs e,
        bool filesOnly = false, bool directoriesOnly = false, string? extension = null)
    {
        var result = new List<string>();
        var items = e.Data.GetFiles();
        if (items == null) return result;

        foreach (var item in items)
        {
            string path = item.Path.LocalPath;

            if (directoriesOnly && !Directory.Exists(path)) continue;
            if (filesOnly && !item.Path.IsFile) continue;
            if (extension != null && !Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase)) continue;

            result.Add(path);
        }
        return result;
    }

    /// <summary>
    /// Splits dropped items into file paths and directory paths.
    /// </summary>
    private static (List<string> files, List<string> folders) ExtractDroppedFilesAndFolders(
        DragEventArgs e, string? extension = null)
    {
        var files = new List<string>();
        var folders = new List<string>();
        var items = e.Data.GetFiles();
        if (items == null) return (files, folders);

        foreach (var item in items)
        {
            string path = item.Path.LocalPath;

            if (File.Exists(path))
            {
                if (extension == null || Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
                    files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                folders.Add(path);
            }
        }
        return (files, folders);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var items = e.Data.GetFiles();
        if (items == null) return;

        bool hasPdf = false;
        bool hasNonPdfFile = false;
        bool hasFolder = false;

        foreach (var item in items)
        {
            string path = item.Path.LocalPath;
            if (Directory.Exists(path))
            {
                hasFolder = true;
            }
            else if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                hasPdf = true;
            }
            else
            {
                hasNonPdfFile = true;
            }
        }

        // FileGrid accepts PDFs and folders (folders are synced to project)
        DropOverlay.IsVisible = hasPdf || hasFolder;
        // AppendixGrid accepts PDFs and folders
        AppendixDropOverlay.IsVisible = hasPdf || hasFolder;
        // OtherFilesGrid accepts any file type and folders
        OtherFilesDropOverlay.IsVisible = hasPdf || hasNonPdfFile || hasFolder;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        HideAllDropOverlays();
    }

    private void OnDragDropCompleted(object? sender, DragEventArgs e)
    {
        HideAllDropOverlays();
    }

    private void HideAllDropOverlays()
    {
        DropOverlay.IsVisible = false;
        AppendixDropOverlay.IsVisible = false;
        OtherFilesDropOverlay.IsVisible = false;
    }

    #endregion

    #region Empty State

    private void UpdateEmptyState()
    {
        bool empty = _ctx.FilteredFiles == null || _ctx.FilteredFiles.Count == 0;
        EmptyStateHint.IsVisible = empty;

        UpdateAttachedEmptyState();
        UpdateFolderEmptyState();
    }

    private void UpdateAttachedEmptyState()
    {
        var file = _ctx.CurrentFile;

        bool appendixEmpty = file?.AppendedFiles == null || file.AppendedFiles.Count == 0;
        AppendixEmptyHint.IsVisible = appendixEmpty;

        bool otherEmpty = file?.OtherFiles == null || file.OtherFiles.Count == 0;
        OtherFilesEmptyHint.IsVisible = otherEmpty;
    }

    private void UpdateFolderEmptyState()
    {
        bool empty = _ctx.CurrentProject?.Folders == null || _ctx.CurrentProject.Folders.Count == 0;
        FolderEmptyHint.IsVisible = empty;
    }

    #endregion

    #region Search

    private void OnSearch(object? sender, RoutedEventArgs e)
    {
        _ctx.Search();
        OnUpdateColumns();
    }

    private void OnStartSearch(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSearch(null, null);
    }

    #endregion

    #region Preview Requests (thin wrappers → ViewModel does the work)

    private void SetPreviewRequestMain(object? sender, RoutedEventArgs r)
    {
        if (_isUpdatingSelection) return;
        ClearOtherGridSelections(FileGrid);
        _ctx.ClearSelectedVersion();
        var file = FileGrid.SelectedItem as FileData;
        RequestPreview(file);
        _pwr.AddRecentFile(file);
    }

    private void SetPreviewRequestAppendedFiles(object? sender, RoutedEventArgs r)
    {
        ClearOtherGridSelections(AppendixGrid);
        var file = AppendixGrid.SelectedItem as FileData;
        RequestPreview(file);
        _pwr.AddRecentFile(file);
    }

    private async void RequestPreview(FileData? file)
    {
        if (_pwr.DualFileMode) return;
        string? searchText = _ctx.IndexedSearch ? SearchText.Text : null;
        await _ctx.RequestPreviewAsync(file, searchText);
    }

    #endregion

    #region Tree View (view only handles selection, data comes from ViewModel)

    private bool _suppressTreeSelection;
    private bool _isUpdatingSelection = false;

    /// <summary>
    /// Clears selection on every FileData grid except <paramref name="active"/>.
    /// The guard flag prevents the resulting SelectionChanged events from re-entering
    /// and causing a recursive loop.
    /// </summary>
    private void ClearOtherGridSelections(DataGrid active)
    {
        if (_isUpdatingSelection) return;
        _isUpdatingSelection = true;
        try
        {
            if (active != FileGrid)        FileGrid.SelectedItem = null;
            if (active != CollectionContent) CollectionContent.SelectedItem = null;
            if (active != AppendixGrid)    AppendixGrid.SelectedItem = null;
            if (active != RecentGrid)      RecentGrid.SelectedItem = null;
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void OnTreeviewSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressTreeSelection)
            return;

        if (MainTree.SelectedItem is not TreeNodeData selectedNode)
            return;

        string? tag = selectedNode.Tag;

        if (tag is "Header" or "Group")
        {
            _suppressTreeSelection = true;
            MainTree.SelectedItem = null;
            _suppressTreeSelection = false;

            if (MainTree.ContextMenu != null)
                MainTree.ContextMenu.IsEnabled = false;
            return;
        }

        if (MainTree.ContextMenu != null)
            MainTree.ContextMenu.IsEnabled = true;

        if (tag == "All Types")
        {
            _ctx.NavigateTo(selectedNode.Header, "All Types");
        }
        else
        {
            string header = selectedNode.Header;
            _ctx.NavigateTo(tag ?? string.Empty, header.Split("  ")[0]);
        }
    }

    private void SetupTreeview(object? sender, RoutedEventArgs e)
    {
        if (_ctx != null)
            _ctx.BuildTreeData();
    }

    #endregion

    #region File Operations

    private void OnOpenFile(object? sender, RoutedEventArgs e) => _ctx.OpenFile();

    private void OnOpenAppendedFile(object? sender, RoutedEventArgs e)
    {
        if (AppendixGrid.SelectedItem is FileData file)
            _ctx.OpenFileDirect(file.Sökväg);
    }

    private void OnOpenOtherFile(object? sender, RoutedEventArgs e)
    {
        if (OtherFilesGrid.SelectedItem is OtherData file)
            _ctx.OpenFileDirect(file.Filepath);
    }

    private void OnOpenAppendedFolder(object? sender, RoutedEventArgs e)
    {
        if (AppendixGrid.SelectedItem is FileData file)
            _ctx.OpenPathDirect(file.Sökväg);
    }

    private void OnOpenOtherFolder(object? sender, RoutedEventArgs e)
    {
        if (OtherFilesGrid.SelectedItem is OtherData file)
            _ctx.OpenPathDirect(file.Filepath);
    }

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        await _ctx.AddFile(this);
        _ctx.BuildTreeData();
    }

    private async void OnLoadFile(object? sender, RoutedEventArgs e)
    {
        await _ctx.LoadFile(this);
        _ctx.BuildTreeData();
        UpdateFont();
    }

    private async void OnSaveFile(object? sender, RoutedEventArgs e) => await _ctx.SaveFile(this);
    private async void OnSaveFileAuto(object? sender, RoutedEventArgs e) => await _ctx.SaveFileAuto();

    private async void OnRemoveFiles(object? sender, RoutedEventArgs e)
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
        {
            _ctx.RemoveSelectedFiles();
            _ctx.UpdateFilter();
            _ctx.BuildTreeData();
        }
    }

    private async void OnRemoveAttachedFile(object? sender, RoutedEventArgs e)
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
            {
                var files = AppendixGrid.SelectedItems.Cast<FileData>().ToList();
                _ctx.RemoveAttachedFile(files);
                UpdateAttachedEmptyState();
            }
        }

    private async void OnRemoveOtherFile(object? sender, RoutedEventArgs e)
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed && OtherFilesGrid.SelectedItem is OtherData file)
        {
            _ctx.RemoveOtherFile(file);
            UpdateAttachedEmptyState();
        }
    }

    private async void OnRemoveProject(object? sender, RoutedEventArgs e)
    {
        if (_ctx.Storage.StoredProjects.Count <= 1) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
        {
            _ctx.RemoveProject();
            _ctx.MarkDirty();
            _ctx.BuildTreeData();
        }
    }

    private async void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFolder == null) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
        {
            _ctx.RemoveFolder();
            _ctx.MarkDirty();
            UpdateFolderEmptyState();
        }
    }

    #endregion

    #region Selection (delegate to ViewModel)

    private void SelectFiles(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        var files = FileGrid.SelectedItems.Cast<FileData>().ToList();
        _ctx.SelectFiles(files);
    }

    private void SelectFavorite(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        ClearOtherGridSelections(CollectionContent);
        _isUpdatingSelection = true;
        try
        {
            var files = CollectionContent.SelectedItems.Cast<FileData>().ToList();
            _ctx.SelectAndNavigateFiles(files);
            SelectInFileGrid(_ctx.CurrentFile, addRecent: true);
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    private void SelectRecent(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        ClearOtherGridSelections(RecentGrid);
        _isUpdatingSelection = true;
        try
        {
            var files = RecentGrid.SelectedItems.Cast<FileData>().ToList();
            _ctx.SelectAndNavigateFiles(files);
            SelectInFileGrid(_ctx.CurrentFile, addRecent: false);
        }
        finally
        {
            _isUpdatingSelection = false;
        }
    }

    /// <summary>
    /// Selects <paramref name="target"/> in the main FileGrid, requests a
    /// preview, and optionally adds it to the recent-files list.
    /// Must be called while <see cref="_isUpdatingSelection"/> is <c>true</c>
    /// so that intermediate SelectionChanged events are suppressed.
    /// </summary>
    private void SelectInFileGrid(FileData? target, bool addRecent)
    {
        if (target == null || !_ctx.FilteredFiles.Contains(target)) return;

        FileGrid.SelectedItem = target;
        FileGrid.ScrollIntoView(target, null);
        RequestPreview(target);
        if (addRecent)
            _pwr.AddRecentFile(target);
    }

    #endregion

    #region Editing & Tags

    private void EditColor(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string color })
            _ctx.AddColor(color);

        FileGrid.SelectedItem = null;
        UpdateRowColor();
    }

    private void EditType(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.SelectedItem != null)
        {
            _ctx.EditType(menuItem.SelectedItem.ToString()!);
            _ctx.BuildTreeData();
        }
    }

    private void OnClearFiles(object? sender, RoutedEventArgs e)
    {
        _ctx.ClearAll();
        FileGrid.SelectedItem = null;
        UpdateRowColor();
    }

    private void OnMoveFile(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { SelectedItem: ProjectData moveToProject })
        {
            _ctx.MoveSelectedFiles(moveToProject);
            _ctx.BuildTreeData();
        }
    }

    private async void OnViewLeft(object? sender, RoutedEventArgs e)
    {
        var file = (FileGrid.SelectedItem ?? AppendixGrid.SelectedItem ?? CollectionContent.SelectedItem) as FileData;
        if (file != null)
            await _ctx.RequestPreviewLeftAsync(file);
    }

    private async void OnViewRight(object? sender, RoutedEventArgs e)
    {
        var file = (FileGrid.SelectedItem ?? AppendixGrid.SelectedItem ?? CollectionContent.SelectedItem) as FileData;
        if (file != null)
            await _ctx.RequestPreview2Async(file);
    }

    #endregion

    #region Metadata Worker

    private void InitMetaworker()
    {
        _metaWorker.DoWork += MetaWorkerDoWork;
        _metaWorker.ProgressChanged += MetaWorkerProgress;
        _metaWorker.RunWorkerCompleted += MetaWorkerRunWorkerCompleted;

        // Hook up thumbnail worker events (reuse background worker for simplicity)
        _metaWorker.DoWork += ThumbnailWorkerDoWork;
        _metaWorker.ProgressChanged += ThumbnailWorkerProgress;
        _metaWorker.RunWorkerCompleted += ThumbnailWorkerRunWorkerCompleted;
    }

    private void OnFetchThumbnails(object? sender, RoutedEventArgs e)
    {
        ProgressStatus.Content = "Generating Thumbnails";
        ProgressBar.IsVisible = true;
        // Start thumbnail work on the background worker; it will call back into the shared progress handlers
        _metaWorker.RunWorkerAsync("thumbnails");
    }

    private void OnFetchIndex(object? sender, RoutedEventArgs e)
    {
        ProgressStatus.Content = "Indexing Files";
        ProgressBar.IsVisible = true;
        // Start indexing work on the background worker; handled in the same worker loop
        _metaWorker.RunWorkerAsync("index");
    }

    private void ThumbnailWorkerDoWork(object? sender, DoWorkEventArgs e)
    {
        if (e.Argument is string arg)
        {
            if (arg == "thumbnails")
            {
                var vm = _ctx;
                int total = vm.CurrentFiles?.Count ?? 0;
                string thumbnailPath = $"{MainViewModel.SavePath}\\Thumbnails\\";

                for (int i = 0; i < total; i++)
                {
                    var file = vm.CurrentFiles[i];
                    vm.GenerateThumbnail(file, thumbnailPath);
                    _metaWorker.ReportProgress((i + 1) * 100 / Math.Max(1, total));
                }
            }
            else if (arg == "index")
            {
                // Run indexing using the async method, report progress back to background worker
                try
                {
                    // Pass a progress reporter that forwards to the background worker
                    var progress = new Progress<int>(p => _metaWorker.ReportProgress(p));
                    // Call the async indexing and wait for completion on this background thread
                    _ctx.GetContentAsync(progress).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
    }

    private void ThumbnailWorkerProgress(object? sender, ProgressChangedEventArgs e)
    {
        // Use same progress bar for meta/thumbnail work
        ProgressBar.Value = e.ProgressPercentage;
    }

    private void ThumbnailWorkerRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
    {
        ProgressStatus.Content = "";
        ProgressBar.Value = 0;
        ProgressBar.IsVisible = false;
    }

    private void OnFetchSingleMeta(object? sender, RoutedEventArgs e) => RunMetaWorker(singleFile: true);
    private void OnFetchFullMeta(object? sender, RoutedEventArgs e) => RunMetaWorker(singleFile: false);

    private void RunMetaWorker(bool singleFile)
    {
        ProgressStatus.Content = "Fetching Metadata";
        _ctx.SelectFilesForMetaworker(singleFile);
        ProgressBar.IsVisible = true;
        _metaWorker.RunWorkerAsync();
    }

    private void MetaWorkerDoWork(object? sender, DoWorkEventArgs e)
    {
        int total = _ctx.GetNrSelectedFiles();
        for (int k = 0; k < total; k++)
        {
            _ctx.GetMetadata(k);
            _metaWorker.ReportProgress((k + 1) * 100 / total);
        }
    }

    private void MetaWorkerProgress(object? sender, ProgressChangedEventArgs e) =>
        ProgressBar.Value = e.ProgressPercentage;

    private void MetaWorkerRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
    {
        _ctx.SetMeta();
        _ctx.MarkDirty();
        ProgressStatus.Content = "";
        ProgressBar.Value = 0;
        ProgressBar.IsVisible = false;
    }

    

    #endregion

    #region Bookmarks

    private void BookmarkSelected(object? sender, RoutedEventArgs e)
    {
        if ((_ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen) && BookmarkGrid.SelectedItem is PageData page)
            _ctx.SetBookmark(page);
    }

    private void OnAddBookmark(object? sender, RoutedEventArgs e)
    {
        if (_ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen)
        {
            _ctx.AddBookmark(BookmarkInput.Text);
            BookmarkInput.Clear();
        }
    }

    private void OnRenameBookmark(object? sender, RoutedEventArgs e)
    {
        _ctx.RenameBookmark(BookmarkInput.Text);
        BookmarkInput.Clear();
    }

    private void OnRemoveBookmark(object? sender, RoutedEventArgs e)
    {
        if (BookmarkGrid.SelectedItem is PageData page)
            _ctx.RemoveBookmark(page);
    }

    #endregion

    #region Versions

    private async void SelectVersion(object? sender, RoutedEventArgs e)
    {
        if (VersionsGrid.SelectedItem is not FileVersionData version) return;
        _ctx.SelectedVersion = version;
        string? searchText = _ctx.IndexedSearch ? SearchText.Text : null;
        await _ctx.PreviewVersionAsync(version, searchText);
    }

    private async void OnRemoveVersion(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile == null || _ctx.SelectedVersion == null) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
            _ctx.RemoveSelectedVersion();
    }

    private void OnVersionDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        _ctx.SetActiveVersion();
    }

    private void OnSetCurrentVersion(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not MenuItem { DataContext: FileVersionData version }) return;
        _ctx.SetCurrentVersionOnSelected(version.Label);
    }

    private void OnLabelFirstVersion(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { SelectedItem: string label }) return;
        _ctx.LabelFirstVersionOnSelected(label);
    }

    private void OnLabelLatestVersion(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { SelectedItem: string label }) return;
        _ctx.LabelLastVersionOnSelected(label);
    }

    private async void OnCompareVersionWithCurrent(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile is not { HasVersions: true } file) return;
        if (VersionsGrid.SelectedItem is not FileVersionData selected) return;

        var current = file.Versions.FirstOrDefault(v => v.Label == file.CurrentVersion);
        if (current == null || current == selected) return;

        string pathA = current.Sökväg;
        string pathB = selected.Sökväg;

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)
            || !pathA.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !pathB.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(pathA) || !File.Exists(pathB))
            return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.OpenDiffDia(window,
            $"{file.Namn} ({current.Label})", pathA,
            $"{file.Namn} ({selected.Label})", pathB);
    }

    private async void OnCompareVersionDiff(object? sender, RoutedEventArgs e)
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.CompareLastTwoVersions(window);
    }

    #endregion

    #region Collections

    private void OnNewCollection(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CollectionInput.Text))
        {
            _ctx.NewCollection(CollectionInput.Text);
            CollectionInput.Clear();
        }
    }

    private void OnRenameCollection(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CollectionInput.Text))
        {
            _ctx.RenameCollection(CollectionInput.Text);
            CollectionInput.Clear();
        }
    }

    private void OnAddToCollection(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem { Header: string header } && header != "Collection")
            _ctx.AddFileToCollection(header);
    }

    #endregion


    #region Row Styling

    private void DataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var row = e.Row;
        if (_trackedRows.Add(row))
            row.DataContextChanged += OnRowDataContextChanged;
        BindRowToFileData(row, row.DataContext as FileData);
        ApplyRowClasses(row, _ctx?.UI?.ColorTagDot == true);
    }

    private void OnRowDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is DataGridRow row)
        {
            BindRowToFileData(row, row.DataContext as FileData);
            ApplyRowClasses(row, _ctx?.UI?.ColorTagDot == true);
        }
    }

    private void BindRowToFileData(DataGridRow row, FileData? newData)
    {
        if (_rowBindings.TryGetValue(row, out var prev))
        {
            prev.Data.PropertyChanged -= prev.Handler;
            _rowBindings.Remove(row);
        }

        if (newData == null) return;

        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is nameof(FileData.IsFileMissing) or nameof(FileData.Färg) or nameof(FileData.Sökväg))
                Dispatcher.UIThread.Post(() => ApplyRowClasses(row, _ctx?.UI?.ColorTagDot == true));
        };
        newData.PropertyChanged += handler;
        _rowBindings[row] = (newData, handler);
    }

    private void OnUpdateColumns()
    {
        var sizeToCell = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
        int count = Math.Min(FileGrid.Columns.Count, 10);
        for (int i = 0; i < count; i++)
            FileGrid.Columns[i].Width = sizeToCell;

        FileGrid.UpdateLayout();
    }

    private void UpdateRowColor()
    {
        foreach (var row in _trackedRows)
            ApplyRowClasses(row, _ctx?.UI?.ColorTagDot == true);
    }

    private static void ApplyRowClasses(DataGridRow row, bool dotMode = false)
    {
        row.Classes.Clear();

        if (row.DataContext is not FileData data)
            return;

        if (data.IsFileMissing)
            row.Classes.Add("RedForeground");

        if (data.Sökväg == string.Empty)
            row.Classes.Add("Placeholder");

        if (!dotMode && !string.IsNullOrEmpty(data.Färg))
            row.Classes.Add(data.Färg);
    }

    #endregion

    #region Misc UI

    private void Border_PointerPressed(object? sender, RoutedEventArgs args)
    {
        if (sender is Control ctl)
            FlyoutBase.ShowAttachedFlyout(ctl);
    }

    #endregion
}