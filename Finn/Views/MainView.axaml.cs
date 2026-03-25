using Avalonia;
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
using Avalonia.Platform.Storage;
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

        // Global arrow-key navigation: Up/Down = file selection, Left/Right = page.
        // Uses Bubble so annotation Tunnel handlers (nudging) get first priority.
        // handledEventsToo: TextBox marks Up/Down as handled — we still need them
        // for search-result navigation when the search tray is active.
        this.AddHandler(KeyDownEvent, OnGlobalKeyDown, RoutingStrategies.Bubble, handledEventsToo: true);

        // Drag-and-drop visual hints
        MainGrid.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        MainGrid.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        MainGrid.AddHandler(DragDrop.DropEvent, OnDragDropCompleted);

        CollectionContent.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);
        CollectionContent.AddHandler(DataGrid.SelectionChangedEvent, SelectFavorite);

        OtherFilesGrid.AddHandler(DragDrop.DropEvent, OnDropOtherFiles);
        OtherFilesGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenOtherFile);

        FolderGrid.AddHandler(DataGrid.DoubleTappedEvent, OnFolderDoubleClick);
        if (FolderGrid.Parent is Control folderDropTarget)
            folderDropTarget.AddHandler(DragDrop.DropEvent, OnDropVersionFolder);

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

    private void OnOpenWhiteboard(object? sender, RoutedEventArgs e)
    {
        // Close the toolbox flyout so the first click after this goes to the canvas
        ToolboxButton.Flyout?.Hide();

        _ctx.OpenWhiteboard(TopLevel.GetTopLevel(this) as Window ?? new Window());
    }

    #region Initialization

    private void InitStartup(object? sender, RoutedEventArgs e)
    {
        _ctx = (MainViewModel)DataContext!;
        _pwr = _ctx.PreviewVM;
        _pwr.DarkMode = _ctx.UI.PreviewDarkMode;
        _pwr.AutoCacheNetworkFiles = _ctx.UI.AutoCacheNetworkFiles;
        _ctx.PropertyChanged += OnViewModelPropertyChanged;
        _ctx.UI.PropertyChanged += OnUIPropertyChanged;
        _ctx.PreviewVM.PropertyChanged += OnPreviewPropertyChanged;

        UpdateFont();
        UpdateMainGrid();

        try
        {
            _ctx.LoadFileAuto();
            _ctx.ReconcileFileCache();
            UpdateFont();
            // Refresh calendar day indicators when data changes
            _ctx.Calendar.DayIndicatorsChanged += () =>
                Dispatcher.UIThread.Post(RefreshCalendarDayIndicators, DispatcherPriority.Loaded);

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

                // Initial paint — use Render priority so CalendarDayButtons are fully templated
                    Dispatcher.UIThread.Post(RefreshCalendarDayIndicators, DispatcherPriority.Render);
                }
                catch (Exception ex) { Debug.WriteLine($"InitStartup failed: {ex}"); }

                UpdateEmptyState();

                // Auto-show analog clock when window is tall enough
                this.SizeChanged += OnMainViewSizeChanged;
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
                UpdateOtherFilesEmptyState();
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

    private void OnMainViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_ctx != null)
            _ctx.UI.ShowClock = e.NewSize.Height > 1250;
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
            case nameof(_ctx.UI.AutoCacheNetworkFiles):
                _pwr.AutoCacheNetworkFiles = _ctx.UI.AutoCacheNetworkFiles;
                break;
            case nameof(_ctx.UI.CalendarOpen):
                if (_ctx.UI.CalendarOpen)
                    Dispatcher.UIThread.Post(RefreshCalendarDayIndicators, DispatcherPriority.Render);
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
            SyncLayerList();
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
        SetDropHintActive(DropOverlay, false);
        var (files, folders) = ExtractDroppedFilesAndFolders(e, extension: ".pdf");
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        if (files.Count > 0)
            await _ctx.AddDroppedFilesAsync(files, window);
        if (folders.Count > 0)
            await _ctx.AddDroppedFoldersAsync(folders, window);
        UpdateEmptyState();
    }

    private async void OnDropOtherFiles(object? sender, DragEventArgs e)
    {
        SetDropHintActive(OtherFilesDropOverlay, false);
        var (files, folders) = ExtractDroppedFilesAndFolders(e);
        await _ctx.AddDroppedOtherFilesAsync(files, folders);
        UpdateOtherFilesEmptyState();
    }

    private async void OnDropVersionFolder(object? sender, DragEventArgs e)
    {
        SetDropHintActive(FolderDropOverlay, false);
        var (_, folders) = ExtractDroppedFilesAndFolders(e);
        if (folders.Count == 0) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        foreach (string path in folders)
        {
            _ctx.NewVersionFolder(path);
            var folder = _ctx.CurrentProject.Folders.LastOrDefault();
            if (folder != null)
                await _ctx.SyncVersionFolderAsync(folder, window);
        }
        UpdateFolderEmptyState();
    }

    /// <summary>
    /// Splits dropped items into file paths and directory paths.
    /// Uses Avalonia storage-item types instead of File.Exists / Directory.Exists
    /// to avoid costly network round-trips for files on slow servers.
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

            if (item is IStorageFolder)
            {
                folders.Add(path);
            }
            else if (item is IStorageFile)
            {
                if (extension == null || Path.GetExtension(path).Equals(extension, StringComparison.OrdinalIgnoreCase))
                    files.Add(path);
            }
        }
        return (files, folders);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var items = e.Data.GetFiles();
        if (items == null) return;

        bool hasPdf = false;
        bool hasFolder = false;
        bool hasAnyFile = false;

        foreach (var item in items)
        {
            if (item is IStorageFolder)
                hasFolder = true;
            else
            {
                hasAnyFile = true;
                if (Path.GetExtension(item.Path.LocalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                    hasPdf = true;
            }
        }

        // FileGrid accepts PDFs and folders (→ Sync Folder)
        SetDropHintActive(DropOverlay, hasPdf || hasFolder);
        // OtherFiles accepts any file or folder (→ Other Files Folder)
        SetDropHintActive(OtherFilesDropOverlay, hasAnyFile || hasFolder);
        // Folder grid accepts folders only (→ Version Delivery Folder)
        SetDropHintActive(FolderDropOverlay, hasFolder);
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
        SetDropHintActive(DropOverlay, false);
        SetDropHintActive(OtherFilesDropOverlay, false);
        SetDropHintActive(FolderDropOverlay, false);
    }

    private static void SetDropHintActive(Avalonia.Controls.Border overlay, bool active)
    {
        overlay.IsVisible = active;
        if (active)
            overlay.Classes.Add("Active");
        else
            overlay.Classes.Remove("Active");
    }

    #endregion

    #region Empty State

    private void UpdateEmptyState()
    {
        bool empty = _ctx.FilteredFiles == null || _ctx.FilteredFiles.Count == 0;
        EmptyStateHint.IsVisible = empty;

        UpdateOtherFilesEmptyState();
        UpdateFolderEmptyState();
    }

    private void UpdateOtherFilesEmptyState()
    {
        var file = _ctx.OtherFilesOwner;
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

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        await _ctx.Search();
        OnUpdateColumns();
    }

    private void OnStartSearch(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSearch(null, null);
    }

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        _ctx.SearchText = string.Empty;
        _ctx.SetDefaultSelection();
        _ctx.BuildTreeData();
        OnUpdateColumns();
        UpdateEmptyState();
    }

    #endregion

    #region Global Arrow-Key Navigation

    /// <summary>
    /// Handles arrow keys via Bubble routing so annotation Tunnel handlers
    /// (nudging) fire first.  Up/Down = file selection, Left/Right = page change.
    /// Skipped when the event was already handled, when focus is in a text input,
    /// or when annotation mode is active.
    /// </summary>
    private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.Left or Key.Right)) return;
        if (_ctx == null || _pwr == null) return;
        if (_pwr?.AnnotationActive == true) return;

        bool previewOpen = _ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen;

        // Search-result navigation runs first and ignores e.Handled because
        // the TextBox (main search field / preview SearchRegex) marks Up/Down
        // as handled during its own Bubble processing.
        if (e.Key is Key.Up or Key.Down
            && previewOpen && _pwr is { SearchMode: true, SearchItems: > 0 })
        {
            if (e.Key == Key.Up) _pwr.PrevSearchPage();
            else _pwr.NextSearchPage();
            e.Handled = true;
            return;
        }

        // Everything below respects prior handling
        if (e.Handled) return;

        // Don't intercept when typing in a TextBox or adjusting a Slider.
        if (TopLevel.GetTopLevel(this) is { } top)
        {
            var focused = top.FocusManager?.GetFocusedElement();
            if (focused is TextBox or Slider) return;
        }

        if (e.Key is Key.Left or Key.Right && previewOpen && _pwr != null && _pwr.Pagecount > 0)
        {
            if (e.Key == Key.Left) _pwr.PrevPage(false);
            else _pwr.NextPage(false);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            if (FileGrid.ItemsSource is not System.Collections.IList items || items.Count == 0) return;
            int current = FileGrid.SelectedItem != null ? items.IndexOf(FileGrid.SelectedItem) : -1;
            int next = e.Key == Key.Up ? current - 1 : current + 1;
            if (next < 0 || next >= items.Count) return;

            FileGrid.SelectedItem = items[next];
            FileGrid.ScrollIntoView(items[next], null);
            e.Handled = true;
        }
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

    private async void RequestPreview(FileData? file)
    {
        // Block automatic file switching while in Dual-File or Diff mode.
        // The user must explicitly close these modes first, or use the
        // View Left/Right context menu items in Dual-File mode.
        if (_pwr.DualFileMode || _pwr.DiffOverlayActive) return;
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
            _ctx.NavigateTo(selectedNode.Header.Split("  ")[0], "All Types");
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

    private void OnOpenFile(object? sender, RoutedEventArgs e)
    {
        // When double-clicking a collection/recent item that is an appended file,
        // open it directly rather than relying on CurrentFiles (which holds the parent).
        if (sender is DataGrid grid && grid == CollectionContent
            && CollectionContent.SelectedItem is FileData cf && cf.ParentFile != null)
        {
            _ctx.OpenFileDirect(cf.Sökväg);
            return;
        }
        _ctx.OpenFile();
    }

    private void OnOpenOtherFile(object? sender, RoutedEventArgs e)
    {
        if (OtherFilesGrid.SelectedItem is OtherData file)
            _ctx.OpenFileDirect(file.Filepath);
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
            // Capture the parent before removal clears references
            var parentToSelect = _ctx.CurrentFile?.IsAppendedFile == true
                ? _ctx.CurrentFile.ParentFile
                : null;

            // Suppress the grid's SelectionChanged handler so UpdateFilter's
            // collection reset doesn't wipe CurrentFiles before we re-select.
            // Also suppress tree selection so BuildTreeData doesn't navigate
            // to the parent's category and change the current type filter.
            _isUpdatingSelection = true;
            _suppressTreeSelection = true;
            try
            {
                _ctx.RemoveSelectedFiles();
                _ctx.UpdateFilter();
                _ctx.BuildTreeData();
            }
            finally
            {
                _isUpdatingSelection = false;
                _suppressTreeSelection = false;
            }

            // Re-select the parent in the grid
            if (parentToSelect != null && _ctx.FilteredFiles.Contains(parentToSelect))
            {
                FileGrid.SelectedItem = parentToSelect;
                FileGrid.ScrollIntoView(parentToSelect, null);
                _ctx.SelectFiles([parentToSelect]);
            }
        }
    }

    private async void OnRemoveOtherFile(object? sender, RoutedEventArgs e)
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed && OtherFilesGrid.SelectedItem is OtherData file)
        {
            _ctx.RemoveOtherFile(file);
            UpdateOtherFilesEmptyState();
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

    private async void OnAttachFiles(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile == null || _ctx.CurrentFile.IsAppendedFile) return;

        var parentName = _ctx.CurrentFile.Namn;
        var existing = _ctx.CurrentProject.StoredFiles
            .Where(f => f.ParentNamn == parentName)
            .OrderBy(f => f.Namn);

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        var dialog = new Finn.Dialogs.xAttachDia
        {
            DataContext = _ctx,
            FontFamily = window.FontFamily,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.RequestedThemeVariant = window.ActualThemeVariant;
        var allProjectPaths = _ctx.CurrentProject.StoredFiles.Select(f => f.Sökväg);
        dialog.SetParentFile(parentName, existing, allProjectPaths);
        await dialog.ShowDialog(window);

        if (!dialog.Confirmed) return;

        foreach (string path in dialog.AcceptedFiles)
            _ctx.AddAppendedFile(path);

        foreach (string folderPath in dialog.AcceptedFolders)
        {
            _ctx.NewFileFolder();
            var folder = _ctx.CurrentProject.Folders.LastOrDefault();
            if (folder != null)
            {
                folder.Path = folderPath;
                folder.Name = new System.IO.DirectoryInfo(folderPath).Name;
                folder.Types = "PDF";
                await _ctx.SyncFolderAsync(folder);
            }
        }

        // Ensure the parent is expanded so newly attached children are visible
        if (_ctx.CurrentFile is { HasChildren: true, IsExpanded: false })
            _ctx.CurrentFile.IsExpanded = true;

        _ctx.UpdateFilter();
        UpdateFolderEmptyState();
    }

    private void OnOpenFolderPath(object? sender, RoutedEventArgs e)
    {
        var folders = FolderGrid.SelectedItems.Cast<FolderData>().ToList();
        foreach (var folder in folders)
        {
            if (!string.IsNullOrEmpty(folder.Path))
                _ctx.OpenFileDirect(folder.Path);
        }
    }

    private void OnFolderDoubleClick(object? sender, RoutedEventArgs e)
    {
        if (FolderGrid.SelectedItem is FolderData folder && !string.IsNullOrEmpty(folder.Path))
            _ctx.OpenFileDirect(folder.Path);
    }

    private async void OnSyncSelectedFolders(object? sender, RoutedEventArgs e)
    {
        var folders = FolderGrid.SelectedItems.Cast<FolderData>().ToList();
        if (folders.Count == 0) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.SyncFoldersAsync(folders, window);
    }

    private async void OnSyncAllFolders(object? sender, RoutedEventArgs e)
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.SyncFoldersAsync(_ctx.CurrentProject.Folders.ToList(), window);
    }

    private async void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        var folders = FolderGrid.SelectedItems.Cast<FolderData>().ToList();
        if (folders.Count == 0) return;

        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
        {
            _ctx.RemoveFolders(folders);
            _ctx.MarkDirty();
            UpdateFolderEmptyState();
        }
    }

    private void OnFolderTypesInfo(object? sender, RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window == null) return;

        var dialog = new Window
        {
            Title = "Folder Types",
            Width = 460,
            Height = 340,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ExtendClientAreaToDecorationsHint = true,
            RequestedThemeVariant = window.ActualThemeVariant,
            Content = BuildFolderTypesInfoContent()
        };
        dialog.ShowDialog(window);
    }

    private static StackPanel BuildFolderTypesInfoContent()
    {
        var panel = new StackPanel { Margin = new Thickness(20, 36, 20, 20), Spacing = 14 };

        panel.Children.Add(new TextBlock
        {
            Text = "Sync Folder Types",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });

        AddFolderTypeEntry(panel,
            "Sync",
            "Watches a folder for PDFs and adds them to the project file list.",
            "Drop a folder onto the file grid.");

        AddFolderTypeEntry(panel,
            "Attached",
            "Watches a folder for PDFs and attaches them as children of a specific file.",
            "Drop a folder in the Attach Files dialog.");

        AddFolderTypeEntry(panel,
            "Other Files",
            "Watches a folder for non-PDF files and links them as attachments.",
            "Drop a folder onto the Other Files panel.");

        AddFolderTypeEntry(panel,
            "Versions",
            "Scans subfolders for PDFs that match existing files by name and imports them as versions.",
            "Drop a folder onto the folder grid.");

        return panel;
    }

    private static void AddFolderTypeEntry(StackPanel parent, string title, string description, string howToAdd)
    {
        var entry = new StackPanel { Spacing = 2 };
        entry.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        entry.Children.Add(new TextBlock { Text = description, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        entry.Children.Add(new TextBlock { Text = $"→  {howToAdd}", FontSize = 12, FontStyle = FontStyle.Italic, Opacity = 0.6 });
        parent.Children.Add(entry);
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
            var target = files.FirstOrDefault();
            if (target == null) return;

            if (target.IsAppendedFile && target.ParentFile is { } parent)
            {
                _ctx.SelectAndNavigateFiles([parent]);
                SelectInFileGrid(parent, addRecent: false);

                // Expand the parent so the appended file is visible, then select it
                if (!parent.IsExpanded)
                {
                    parent.IsExpanded = true;
                    _ctx.UpdateFilter();
                }
                RequestPreview(target);
                _pwr.AddRecentFile(target);
                FileGrid.SelectedItem = target;
            }
            else
            {
                _ctx.SelectAndNavigateFiles(files);
                SelectInFileGrid(target, addRecent: true);
            }
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
            var target = files.FirstOrDefault();
            if (target == null) return;

            if (target.IsAppendedFile && target.ParentFile is { } parent)
            {
                _ctx.SelectAndNavigateFiles([parent]);
                SelectInFileGrid(parent, addRecent: false);

                if (!parent.IsExpanded)
                {
                    parent.IsExpanded = true;
                    _ctx.UpdateFilter();
                }
                RequestPreview(target);
                FileGrid.SelectedItem = target;
            }
            else
            {
                _ctx.SelectAndNavigateFiles(files);
                SelectInFileGrid(target, addRecent: false);
            }
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
        string? color = sender switch
        {
            MenuItem { Tag: string c } => c,
            Button { Tag: string c } => c,
            _ => null
        };
        if (color != null)
            _ctx.AddColor(color);

        // Close the action bar color picker flyout
        if (sender is Button)
            ColorTagButton.Flyout?.Hide();

        UpdateRowColor();
    }

    private void EditType(object? sender, RoutedEventArgs e)
    {
        string? type = sender switch
        {
            MenuItem menuItem when menuItem.SelectedItem != null => menuItem.SelectedItem.ToString(),
            Button { Content: string c } => c,
            _ => null
        };
        if (type != null)
        {
            _ctx.EditType(type);
            _ctx.BuildTreeData();
        }

        if (sender is Button)
            CategoryButton.Flyout?.Hide();
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
        if (!_pwr.DualFileMode) return;
        var file = (FileGrid.SelectedItem ?? CollectionContent.SelectedItem) as FileData;
        if (file != null)
            await _ctx.RequestPreviewLeftAsync(file);
    }

    private async void OnViewRight(object? sender, RoutedEventArgs e)
    {
        if (!_pwr.DualFileMode) return;
        var file = (FileGrid.SelectedItem ?? CollectionContent.SelectedItem) as FileData;
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
    }

    private async void OnFetchThumbnails(object? sender, RoutedEventArgs e)
    {
        if (_ctx.PreviewVM.BackgroundTaskActive) return;

        var cts = new CancellationTokenSource();
        _ctx.PreviewVM.SetBackgroundTaskCts(cts);
        _ctx.PreviewVM.BackgroundTaskMessage = "Generating Thumbnails";
        _ctx.PreviewVM.BackgroundTaskActive = true;
        _ctx.PreviewVM.BackgroundTaskProgress = 0;

        try
        {
            var progress = new Progress<int>(p =>
                _ctx.PreviewVM.BackgroundTaskProgress = p);
            await _ctx.Data.GenerateThumbnailsAsync(progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _ctx.PreviewVM.BackgroundTaskMessage = "Thumbnails cancelled";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _ctx.PreviewVM.SetBackgroundTaskCts(null);
            _ctx.PreviewVM.BackgroundTaskMessage = "";
            _ctx.PreviewVM.BackgroundTaskProgress = 0;
            _ctx.PreviewVM.BackgroundTaskActive = false;
            cts.Dispose();
        }
    }

    private async void OnFetchIndex(object? sender, RoutedEventArgs e)
    {
        if (_ctx.PreviewVM.BackgroundTaskActive) return;

        var cts = new CancellationTokenSource();
        _ctx.PreviewVM.SetBackgroundTaskCts(cts);
        _ctx.PreviewVM.BackgroundTaskMessage = "Indexing Files";
        _ctx.PreviewVM.BackgroundTaskActive = true;
        _ctx.PreviewVM.BackgroundTaskProgress = 0;

        try
        {
            var progress = new Progress<int>(p =>
                _ctx.PreviewVM.BackgroundTaskProgress = p);
            var statusProgress = new Progress<string>(name =>
                _ctx.PreviewVM.BackgroundTaskMessage = $"Indexing: {name}");

            await _ctx.Data.GetContentAsync(progress, statusProgress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _ctx.PreviewVM.BackgroundTaskMessage = "Indexing cancelled";
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            _ctx.PreviewVM.SetBackgroundTaskCts(null);
            _ctx.PreviewVM.BackgroundTaskMessage = "";
            _ctx.PreviewVM.BackgroundTaskProgress = 0;
            _ctx.PreviewVM.BackgroundTaskActive = false;
            cts.Dispose();
        }
    }

    private void OnFetchSingleMeta(object? sender, RoutedEventArgs e) => RunMetaWorker(singleFile: true);
    private void OnFetchFullMeta(object? sender, RoutedEventArgs e) => RunMetaWorker(singleFile: false);

    private void RunMetaWorker(bool singleFile)
    {
        if (_metaWorker.IsBusy) return;
        _ctx.PreviewVM.BackgroundTaskMessage = "Fetching Metadata";
        _ctx.Data.SelectFilesForMetaworker(singleFile);
        _ctx.PreviewVM.BackgroundTaskActive = true;
        _metaWorker.RunWorkerAsync();
    }

    private void MetaWorkerDoWork(object? sender, DoWorkEventArgs e)
    {
        int total = _ctx.Data.GetNrSelectedFiles();
        for (int k = 0; k < total; k++)
        {
            _ctx.Data.GetMetadata(k);
            _metaWorker.ReportProgress((k + 1) * 100 / total);
        }
    }

    private void MetaWorkerProgress(object? sender, ProgressChangedEventArgs e) =>
        _ctx.PreviewVM.BackgroundTaskProgress = e.ProgressPercentage;

    private void MetaWorkerRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs e)
    {
        _ctx.Data.SetMeta();
        _ctx.MarkDirty();
        _ctx.PreviewVM.BackgroundTaskMessage = "";
        _ctx.PreviewVM.BackgroundTaskProgress = 0;
        _ctx.PreviewVM.BackgroundTaskActive = false;
    }

    

    #endregion

    #region Bookmarks

    private void BookmarkSelected(object? sender, RoutedEventArgs e)
    {
        if ((_ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen) && BookmarkGrid.SelectedItem is PageData page)
            _ctx.Collections.SetBookmark(page);
    }

    private void OnAddBookmark(object? sender, RoutedEventArgs e)
    {
        if (_ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen)
        {
            _ctx.Collections.AddBookmark(BookmarkInput.Text);
            BookmarkInput.Clear();
        }
    }

    private void OnRenameBookmark(object? sender, RoutedEventArgs e)
    {
        _ctx.Collections.RenameBookmark(BookmarkInput.Text);
        BookmarkInput.Clear();
    }

    private void OnRemoveBookmark(object? sender, RoutedEventArgs e)
    {
        if (BookmarkGrid.SelectedItem is PageData page)
            _ctx.Collections.RemoveBookmark(page);
    }

    #endregion

    #region Versions

    private async void SelectVersion(object? sender, RoutedEventArgs e)
    {
        if (VersionsGrid.SelectedItem is not FileVersionData version) return;
        // Block version preview while in Diff or Dual-File mode
        if (_pwr.DualFileMode || _pwr.DiffOverlayActive) return;
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

    private void OnCompareVersionWithOriginal(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile is not { HasVersions: true } file) return;
        if (VersionsGrid.SelectedItem is not FileVersionData selected) return;
        if (string.IsNullOrEmpty(file.OriginalPath)) return;

        string pathA = file.OriginalPath;
        string pathB = selected.Sökväg;

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)
            || !pathA.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !pathB.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(pathA) || !File.Exists(pathB))
            return;

        _ctx.RunDiffInPreviewer(pathA, pathB);
    }

    private void OnCompareVersionWithPrevious(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile is not { HasVersions: true } file) return;
        if (VersionsGrid.SelectedItem is not FileVersionData selected) return;

        int idx = file.Versions.IndexOf(selected);
        if (idx < 0) return;

        // First version: compare against original file.
        // Other versions: compare against the preceding version.
        string pathA;
        if (idx == 0)
        {
            if (string.IsNullOrEmpty(file.OriginalPath)) return;
            pathA = file.OriginalPath;
        }
        else
        {
            var prev = file.Versions[idx - 1];
            pathA = prev.Sökväg;
        }

        string pathB = selected.Sökväg;

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)
            || !pathA.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !pathB.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(pathA) || !File.Exists(pathB))
            return;

        _ctx.RunDiffInPreviewer(pathA, pathB);
    }

    private void OnLabelFromFolderDate(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile == null || _ctx.SelectedVersion == null) return;
        _ctx.LabelVersionFromFolderDate();
    }

    #endregion

    #region Annotation Layers

    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-sync the layer list whenever the active layers change.
        // PropertyChanged may fire from a background thread (e.g. CurrentFile
        // is set after ConfigureAwait(false) in SetFileAsync), so dispatch
        // to the UI thread to avoid cross-thread access on LayerList.
        if (e.PropertyName is "CurrentFile" or "WhiteboardMode" or "LayersChanged")
            Dispatcher.UIThread.Post(() => SyncLayerList(), DispatcherPriority.Background);
    }

    private static readonly Avalonia.Media.Color[] LayerColors =
    [
        Avalonia.Media.Color.FromRgb(214, 64, 69),
        Avalonia.Media.Color.FromRgb(59, 130, 217),
        Avalonia.Media.Color.FromRgb(61, 163, 95),
        Avalonia.Media.Color.FromRgb(229, 168, 32),
        Avalonia.Media.Color.FromRgb(155, 95, 192),
    ];

    private Controls.AnnotatedPDFRenderer? AnnotationRenderer
        => (EmbeddedPreview as Views.PreView)?.MuPDFRenderer;

    private void SyncLayerList()
    {
        var renderer = AnnotationRenderer;
        if (renderer == null) return;
        // Ensure at least the default layer exists so the tray is never empty
        renderer.EnsureDefaultLayer();
        if (LayerList.ItemsSource != renderer.Layers)
            LayerList.ItemsSource = renderer.Layers;
        if (renderer.ActiveLayer != null)
            LayerList.SelectedItem = renderer.ActiveLayer;
    }

    private void OnAnnotateNewLayer(object? sender, RoutedEventArgs e)
    {
        SyncLayerList();
        var renderer = AnnotationRenderer;
        if (renderer == null) return;
        renderer.EnsureDefaultLayer();
        int index = renderer.Layers.Count;
        var color = LayerColors[index % LayerColors.Length];
        var layer = renderer.AddLayer($"Layer {index + 1}", color);
        renderer.StrokeColor = color;
        LayerList.SelectedItem = layer;
    }

    private void OnLayerSelected(object? sender, SelectionChangedEventArgs e)
    {
        var renderer = AnnotationRenderer;
        if (renderer == null) return;
        if (LayerList.SelectedItem is Model.AnnotationLayer layer)
        {
            renderer.ActiveLayer = layer;
            // Only update stroke color if not in highlighter mode
            if (!renderer.IsHighlighterMode)
                renderer.StrokeColor = layer.Color;
        }
        renderer.InvalidateVisual();
    }

    private void OnLayerVisibilityToggled(object? sender, RoutedEventArgs e)
    {
        AnnotationRenderer?.InvalidateVisual();
    }

    private void OnClearSelectedLayer(object? sender, RoutedEventArgs e)
    {
        var renderer = AnnotationRenderer;
        if (renderer == null) return;
        if (LayerList.SelectedItem is Model.AnnotationLayer layer)
            renderer.ClearLayer(layer);
    }

    private void OnRemoveSelectedLayer(object? sender, RoutedEventArgs e)
    {
        var renderer = AnnotationRenderer;
        if (renderer == null || renderer.Layers.Count <= 1) return;
        if (LayerList.SelectedItem is Model.AnnotationLayer layer)
            renderer.RemoveLayer(layer);
    }

    #endregion

    #region Collections

    private void OnNewCollection(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CollectionInput.Text))
        {
            _ctx.Collections.NewCollection(CollectionInput.Text);
            CollectionInput.Clear();
        }
    }

    private void OnRenameCollection(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CollectionInput.Text))
        {
            _ctx.Collections.RenameCollection(CollectionInput.Text);
            CollectionInput.Clear();
        }
    }

    private void OnAddToCollection(object? sender, RoutedEventArgs e)
    {
        string? name = sender switch
        {
            MenuItem { SelectedItem: string s } => s,
            Button { Content: string c } => c,
            _ => null
        };
        if (name != null)
            _ctx.Collections.AddFileToCollection(name);

        if (sender is Button)
            CollectionButton.Flyout?.Hide();
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

    private static readonly string[] AllColorClasses =
        ["Yellow", "Orange", "Brown", "Green", "Blue", "Red", "Magenta"];

    private static void ApplyRowClasses(DataGridRow row, bool dotMode = false)
    {
        if (row.DataContext is not FileData data)
        {
            row.Classes.Remove("RedForeground");
            row.Classes.Remove("Placeholder");
            row.Classes.Remove("AppendedRow");
            foreach (var c in AllColorClasses)
                row.Classes.Remove(c);
            return;
        }

        bool isPlaceholder = data.Sökväg == string.Empty;

        SetClass(row, "RedForeground", data.IsFileMissing && !isPlaceholder);
        SetClass(row, "Placeholder", isPlaceholder);
        SetClass(row, "AppendedRow", data.IsAppendedFile);

        string? wantColor = (!dotMode && !string.IsNullOrEmpty(data.Färg)) ? data.Färg : null;
        foreach (var c in AllColorClasses)
            SetClass(row, c, c == wantColor);
    }

    private static void SetClass(DataGridRow row, string cls, bool active)
    {
        if (active)
        {
            if (!row.Classes.Contains(cls))
                row.Classes.Add(cls);
        }
        else
        {
            row.Classes.Remove(cls);
        }
    }

    #endregion

    #region Misc UI

    private void Border_PointerPressed(object? sender, RoutedEventArgs args)
    {
        if (sender is Control ctl)
            FlyoutBase.ShowAttachedFlyout(ctl);
    }

    private void OnCancelBackgroundTask(object? sender, RoutedEventArgs e)
    {
        _pwr?.CancelBackgroundTask();
    }

    #endregion

    #region Calendar

    private async void OnCopyDiaryEntry(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is Model.WeekDiaryEntry entry
            && !string.IsNullOrWhiteSpace(entry.Diary))
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null)
                await top.Clipboard.SetTextAsync(entry.Diary);
        }
    }

    /// <summary>
    /// Called when the Calendar control navigates to a different month.
    /// </summary>
    private void OnCalendarMonthChanged(object? sender, CalendarDateChangedEventArgs e)
    {
        // Delay so the CalendarDayButtons have been laid out for the new month
        Dispatcher.UIThread.Post(RefreshCalendarDayIndicators, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Walks the visual tree of the calendar control and adds small coloured
    /// dot indicators to each <see cref="CalendarDayButton"/> that has timesheet
    /// entries, notes, or reminders.
    /// </summary>
    private void RefreshCalendarDayIndicators()
    {
        var calendar = this.FindControl<Calendar>("MainCalendar");
        if (calendar is null || _ctx?.Calendar is null) return;

        var displayDate = calendar.DisplayDate;
        var dayButtons = calendar.GetVisualDescendants().OfType<CalendarDayButton>();

        foreach (var btn in dayButtons)
        {
            // Resolve the template root Panel so we can add/remove our indicator
            var rootPanel = btn.GetVisualChildren().FirstOrDefault() as Panel;
            if (rootPanel is null) continue;

            // Remove any previously-added indicator panel
            for (int i = rootPanel.Children.Count - 1; i >= 0; i--)
            {
                if (rootPanel.Children[i] is Avalonia.Controls.StackPanel sp && sp.Name == "_DayInd")
                    rootPanel.Children.RemoveAt(i);
            }

            // Skip inactive (previous/next month) day buttons
            if (btn.Classes.Contains(":inactive")) continue;

            // Parse the day number from the button's Content
            if (btn.Content is not string dayStr || !int.TryParse(dayStr, out int day)) continue;
            if (day < 1 || day > DateTime.DaysInMonth(displayDate.Year, displayDate.Month)) continue;

            var date = new DateOnly(displayDate.Year, displayDate.Month, day);
            var (hasNote, hasTime, hasReminder) = _ctx.Calendar.GetDayInfo(date);
            if (!hasNote && !hasTime && !hasReminder) continue;

            var indicator = new Avalonia.Controls.StackPanel
            {
                Name = "_DayInd",
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Spacing = 2,
                Margin = new Thickness(0, 0, 0, 4),
                IsHitTestVisible = false,
            };

            if (hasTime)
                indicator.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                    { Width = 5, Height = 5, Fill = Brushes.DodgerBlue });
            if (hasNote)
                indicator.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                    { Width = 5, Height = 5, Fill = Brushes.MediumSeaGreen });
            if (hasReminder)
                indicator.Children.Add(new Avalonia.Controls.Shapes.Ellipse
                    { Width = 5, Height = 5, Fill = Brushes.Orange });

            rootPanel.Children.Add(indicator);
        }
    }

    #endregion
}