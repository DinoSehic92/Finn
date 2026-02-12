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

namespace Finn.Views;

public partial class MainView : UserControl
{
    private readonly BackgroundWorker _metaWorker = new() { WorkerReportsProgress = true };
    private readonly List<DataGridRowEventArgs> _rowArgs = [];

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

        CollectionContent.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);
        CollectionContent.AddHandler(DataGrid.SelectionChangedEvent, SelectFavorite);
        CollectionContent.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestCollection);

        FolderGrid.AddHandler(DragDrop.DropEvent, OnFolderDrop);

        AppendixGrid.AddHandler(DragDrop.DropEvent, OnDropAppendedFiles);
        AppendixGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestAppendedFiles);

        OtherFilesGrid.AddHandler(DragDrop.DropEvent, OnDropOtherFiles);
        OtherFilesGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenOtherFile);

        RecentGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectRecent);
        RecentGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestRecent);

        BookmarkGrid.AddHandler(DataGrid.SelectionChangedEvent, BookmarkSelected);
        HoursCombo.AddHandler(ComboBox.SelectionChangedEvent, OnUpdateTotalTime);

        InitMetaworker();
    }

    #region Initialization

    private void InitStartup(object? sender, RoutedEventArgs e)
    {
        _ctx = (MainViewModel)DataContext!;
        _pwr = _ctx.PreviewVM;
        _ctx.PropertyChanged += OnViewModelPropertyChanged;

        UpdateFont();

        try
        {
            _ctx.LoadFileAuto();
            UpdateFont();
            _ctx.SelectedDateTime = DateTime.Now;
        }
        catch { }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.FilteredFiles):
            case "UpdateColumns":
                OnUpdateColumns();
                break;
            case "Color1":
            case "Color3":
                UpdateRowColor();
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
        }
    }

    private void UpdateFont()
    {
        var window = TopLevel.GetTopLevel(this);
        if (window == null) return;

        window.FontFamily = (FontFamily)Resources[_ctx.Storage.General.Font]!;
        window.FontSize = _ctx.Storage.General.FontSize;
    }

    #endregion

    #region Preview Window & Grid

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
        if (_ctx.PreviewEmbeddedOpen)
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

    #endregion

    #region Drag & Drop (extract paths only, delegate to ViewModel)

    private void OnFolderDrop(object? sender, DragEventArgs e)
    {
        var paths = ExtractDroppedPaths(e, directoriesOnly: true);
        if (paths.Count > 0)
            _ctx.AddDroppedFolders(paths);
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var paths = ExtractDroppedPaths(e, filesOnly: true, extension: ".pdf");
        if (paths.Count > 0)
            _ctx.AddDroppedFiles(paths);
    }

    private void OnDropAppendedFiles(object? sender, DragEventArgs e)
    {
        var (files, folders) = ExtractDroppedFilesAndFolders(e, extension: ".pdf");
        _ctx.AddDroppedAppendedFiles(files, folders);
    }

    private void OnDropOtherFiles(object? sender, DragEventArgs e)
    {
        var (files, folders) = ExtractDroppedFilesAndFolders(e);
        _ctx.AddDroppedOtherFiles(files, folders);
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
        var file = FileGrid.SelectedItem as FileData;
        RequestPreview(file);
        RecentGrid.SelectedItem = null;
        _pwr.AddRecentFile(file);
    }

    private void SetPreviewRequestCollection(object? sender, RoutedEventArgs r)
    {
        var file = CollectionContent.SelectedItem as FileData;
        RequestPreview(file);
        _pwr.AddRecentFile(file);
    }

    private void SetPreviewRequestAppendedFiles(object? sender, RoutedEventArgs r)
    {
        var file = AppendixGrid.SelectedItem as FileData;
        RequestPreview(file);
        _pwr.AddRecentFile(file);
    }

    private void SetPreviewRequestRecent(object? sender, RoutedEventArgs r)
    {
        RequestPreview(RecentGrid.SelectedItem as FileData);
    }

    private async void RequestPreview(FileData? file)
    {
        string? searchText = _ctx.IndexedSearch ? SearchText.Text : null;
        await _ctx.RequestPreviewAsync(file, searchText);
    }

    #endregion

    #region Tree View (view only handles selection, data comes from ViewModel)

    private bool _suppressTreeSelection;

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
            _ctx.SelectType("All Types");
            _ctx.SelectProject(selectedNode.Header);
        }
        else
        {
            string header = selectedNode.Header;
            _ctx.SelectType(header.Split("  ")[0]);
            _ctx.SelectProject(tag ?? string.Empty);
        }

        OnUpdateColumns();
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

    private void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        _ctx.AddFile(this);
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
        }
    }

    private async void OnRemoveOtherFile(object? sender, RoutedEventArgs e)
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed && OtherFilesGrid.SelectedItem is OtherData file)
            _ctx.RemoveOtherFile(file);
    }

    private async void OnRemoveProject(object? sender, RoutedEventArgs e)
    {
        if (_ctx.Storage.StoredProjects.Count <= 1) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
        {
            _ctx.RemoveProject();
            _ctx.BuildTreeData();
        }
    }

    private async void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFolder == null) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
            _ctx.RemoveFolder();
    }

    #endregion

    #region Selection (delegate to ViewModel)

    private void SelectFiles(object? sender, RoutedEventArgs e)
    {
        var files = FileGrid.SelectedItems.Cast<FileData>().ToList();
        CollectionContent.SelectedItem = null;
        _ctx.select_files(files);
    }

    private void SelectFavorite(object? sender, RoutedEventArgs e)
    {
        var files = CollectionContent.SelectedItems.Cast<FileData>().ToList();
        FileGrid.SelectedItem = null;
        _ctx.SelectAndNavigateFiles(files);
    }

    private void SelectRecent(object? sender, RoutedEventArgs e)
    {
        var files = RecentGrid.SelectedItems.Cast<FileData>().ToList();
        FileGrid.SelectedItem = null;
        _ctx.SelectAndNavigateFiles(files);
    }

    #endregion

    #region Editing & Tags

    private void EditColor(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string color } && !string.IsNullOrEmpty(color))
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

    #endregion

    #region Status Checks

    private void OnCheckStatusSingleFile(object? sender, RoutedEventArgs e)
    {
        _ctx.CheckSingleFile();
        UpdateRowColor();
    }

    private async void OnCheckProjectFiles(object? sender, RoutedEventArgs e)
    {
        await _ctx.CheckProjectFiles();
        FileGrid.SelectedItem = null;
        UpdateRowColor();
    }

    #endregion

    #region Metadata Worker

    private void InitMetaworker()
    {
        _metaWorker.DoWork += MetaWorkerDoWork;
        _metaWorker.ProgressChanged += MetaWorkerProgress;
        _metaWorker.RunWorkerCompleted += MetaWorkerRunWorkerCompleted;
    }

    private void OnFetchSingleMeta(object? sender, RoutedEventArgs e) => RunMetaWorker(singleFile: true);
    private void OnFetchFullMeta(object? sender, RoutedEventArgs e) => RunMetaWorker(singleFile: false);

    private void RunMetaWorker(bool singleFile)
    {
        ProgressStatus.Content = "Fetching Metadata";
        _ctx.SelectFilesForMetaworker(singleFile);
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
        ProgressStatus.Content = "";
        ProgressBar.Value = 0;
    }

    #endregion

    #region Bookmarks

    private void BookmarkSelected(object? sender, RoutedEventArgs e)
    {
        if ((_ctx.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen) && BookmarkGrid.SelectedItem is PageData page)
            _ctx.SetBookmark(page);
    }

    private void OnAddBookmark(object? sender, RoutedEventArgs e)
    {
        if (_ctx.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen)
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

    #region Timesheet

    private void NewTimeSheetProject(object? sender, RoutedEventArgs e) =>
        _ctx.Storage.General.TimeProjects.Add(new TimeSheetProjectData { Project = "New Project" });

    private void RemoveTimeSheetProject(object? sender, RoutedEventArgs e) =>
        _ctx.Storage.General.TimeProjects.Remove(_ctx.CurrentTimeSheetProject);

    private void OnUpdateTotalTime(object? sender, RoutedEventArgs args) =>
        _ctx.CurrentCalendarData?.TriggerDateStringUpdate();

    #endregion

    #region Row Styling

    private void DataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        _rowArgs.Add(e);
        ApplyRowClasses(e.Row);
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
        foreach (var e in _rowArgs)
            ApplyRowClasses(e.Row);
    }

    private static void ApplyRowClasses(DataGridRow row)
    {
        row.Classes.Clear();

        if (row.DataContext is not FileData data)
            return;

        if (data.FileStatus == "Missing")
            row.Classes.Add("RedForeground");

        if (data.Sökväg == string.Empty)
            row.Classes.Add("Placeholder");

        if (!string.IsNullOrEmpty(data.Färg))
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