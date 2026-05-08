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
using Avalonia.Input.Platform;
using System.Collections.Generic;
using Finn.Model;
using System.IO;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Finn.Dialogs;

namespace Finn.Views;

public partial class MainView : UserControl
{
    private readonly HashSet<DataGridRow> _trackedRows = [];
    private readonly Dictionary<DataGridRow, (FileData Data, PropertyChangedEventHandler Handler)> _rowBindings = [];

    private MainViewModel _ctx = null!;

    /// <summary>Shorthand for the parent window — avoids repeated <c>(MainWindow)TopLevel.GetTopLevel(this)!</c> casts.</summary>
    private MainWindow ParentWindow => (MainWindow)TopLevel.GetTopLevel(this)!;

    /// <summary>
    /// Returns a disposable guard that sets <c>_isUpdatingSelection</c>
    /// (and optionally <c>_suppressTreeSelection</c>) for the duration of a <c>using</c> block.
    /// </summary>
    private SelectionGuard SuppressSelection(bool suppressTree = false) => new(this, suppressTree);

    private struct SelectionGuard : IDisposable
    {
        private readonly MainView _view;
        private readonly bool _suppressTree;
        public SelectionGuard(MainView view, bool suppressTree)
        {
            _view = view;
            _suppressTree = suppressTree;
            _view._isUpdatingSelection = true;
            if (suppressTree) _view._suppressTreeSelection = true;
        }
        public void Dispose()
        {
            _view._isUpdatingSelection = false;
            if (_suppressTree) _view._suppressTreeSelection = false;
        }
    }
    private PreviewViewModel _pwr = null!;

    public MainView()
    {
        InitializeComponent();

        FileGrid.AddHandler(DataGrid.LoadedEvent, InitStartup);
        FileGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);
        FileGrid.AddHandler(DataGrid.SelectionChangedEvent, SetPreviewRequestMain);
        FileGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectFiles);
        FileGrid.AddHandler(DragDrop.DropEvent, OnDrop);

        // Internal row drag-drop for nesting files into groups
        FileGrid.AddHandler(PointerPressedEvent, OnFileGridPointerPressed, RoutingStrategies.Tunnel);
        FileGrid.AddHandler(PointerMovedEvent, OnFileGridPointerMoved, RoutingStrategies.Tunnel);
        FileGrid.AddHandler(PointerReleasedEvent, OnFileGridPointerReleased, RoutingStrategies.Tunnel);

        // Drag-and-drop visual hints
        MainGrid.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        MainGrid.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        MainGrid.AddHandler(DragDrop.DropEvent, OnDragDropCompleted);

        CollectionContent.AddHandler(DataGrid.DoubleTappedEvent, OnOpenFile);
        CollectionContent.AddHandler(DataGrid.SelectionChangedEvent, SelectFavorite);

        OtherFilesGrid.AddHandler(DragDrop.DropEvent, OnDropOtherFiles);
        OtherFilesGrid.AddHandler(DataGrid.DoubleTappedEvent, OnOpenOtherFile);

        FolderGrid.AddHandler(DataGrid.DoubleTappedEvent, OnFolderDoubleClick);
        // The outer Grid (grandparent) has DragDrop.AllowDrop
        var folderDropTarget = FolderGrid.Parent?.Parent as Control;
        if (folderDropTarget != null)
            folderDropTarget.AddHandler(DragDrop.DropEvent, OnDropVersionFolder);

        RecentGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectRecent);

        BookmarkGrid.AddHandler(DataGrid.SelectionChangedEvent, BookmarkSelected);

        VersionsGrid.AddHandler(DataGrid.DoubleTappedEvent, OnVersionDoubleTapped);
        VersionsGrid.AddHandler(DataGrid.SelectionChangedEvent, SelectVersion);

        MainTree.SelectionChanged += OnTreeviewSelected;
    }

    // Removed file-open debugger / benchmark command and handler

    private void OnOpenWhiteboard(object? sender, RoutedEventArgs e)
    {
        // Close the toolbox flyout so the first click after this goes to the canvas
        ToolboxButton.Flyout?.Hide();

        _ctx.OpenWhiteboard(ParentWindow);
    }

    #region Initialization

    private async void InitStartup(object? sender, RoutedEventArgs e)
    {
        _ctx = (MainViewModel)DataContext!;
        _pwr = _ctx.PreviewVM;
        _pwr.DarkMode = _ctx.UI.PreviewDarkMode;
        _pwr.DarkModeTint = _ctx.UI.PreviewDarkModeTint;
        _pwr.DarkModeTintIntensity = _ctx.UI.PreviewTintIntensity;
        _pwr.LightPaper = _ctx.UI.PreviewLightPaper;
        _pwr.AutoCacheNetworkFiles = _ctx.UI.AutoCacheNetworkFiles;
        _pwr.ReadBytesMode = _ctx.UI.ReadBytesMode;
        _ctx.PropertyChanged += OnViewModelPropertyChanged;
        _ctx.UI.PropertyChanged += OnUIPropertyChanged;
        _ctx.PreviewVM.PropertyChanged += OnPreviewPropertyChanged;
        _ctx.Collections.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(_ctx.Collections.CollectionContent)
                or nameof(_ctx.Collections.CurrentCollection))
                UpdateCollectionsEmptyHint();
        };
        _ctx.Collections.CollectionContent.CollectionChanged += (_, _) => UpdateCollectionsEmptyHint();
        _ctx.PreviewVM.RecentFiles.CollectionChanged += (_, _) => UpdateRecentEmptyHint();
        _ctx.Calendar.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_ctx.Calendar.SelectableProjectNames))
                Resources["CalendarProjectNames"] = _ctx.Calendar.SelectableProjectNames;
        };
        _ctx.ColumnsChanged += () => { OnUpdateColumns(); UpdateEmptyState(); };
        _ctx.TreeViewUpdateRequested += () => _ctx.BuildTreeData();
        _ctx.FontChanged += () => UpdateFont();
        _ctx.StartPassiveEditTracking();

        PushViewModelResources();
        UpdateFont();
        UpdateMainGrid();

        try
        {
            // Clean up stale diff temp directories on a background thread — no UI needed
            _ = Task.Run(() => PreviewViewModel.CleanupStaleDiffTempDirs());

            await _ctx.LoadFileAutoAsync();
            _ctx.BuildTreeData();
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
                await _ctx.Calendar.LoadOrCreateStorageAsync(MainViewModel.SavePath);
            }
            catch (Exception ex)
            {
                Utils.ErrorLogger.Log(ex, "InitStartup.LoadCalendar");
            }

                // Initial paint — use Render priority so CalendarDayButtons are fully templated
                    Dispatcher.UIThread.Post(RefreshCalendarDayIndicators, DispatcherPriority.Render);
                }
                catch (Exception ex) { Utils.ErrorLogger.Log(ex, "InitStartup"); }

                UpdateEmptyState();
                SubscribeTodoItems();
                ApplyAlternatingRowShading();

                // Start watching sync folders for file changes (if enabled)
                _ctx.RefreshFolderWatchers();
                // Check if any folders changed while the app was closed (async — no UI freeze)
                if (_ctx.UI.FolderWatchEnabled)
                    _ = _ctx.CheckFolderSyncOnStartupAsync();
                // Check shared project sync status (async — network I/O)
                // Skipped when the user disabled it in Settings to avoid slow startup on sluggish network shares.
                if (_ctx.UI.SharedSyncCheckOnStartup)
                    _ = _ctx.CheckSharedSyncOnStartupAsync();

                // Auto-show analog clock when window is tall enough
                this.SizeChanged += OnMainViewSizeChanged;

                // Everything is ready — center the main window and close the splash.
                if (_ctx.SplashWindow is { } splash)
                {
                    var mainWindow = ParentWindow;
                    var screen = mainWindow.Screens.ScreenFromWindow(splash)
                                 ?? mainWindow.Screens.Primary;
                    if (screen != null)
                    {
                        var workArea = screen.WorkingArea;
                        int width  = (int)(mainWindow.Width  > 0 ? mainWindow.Width  : mainWindow.Bounds.Width);
                        int height = (int)(mainWindow.Height > 0 ? mainWindow.Height : mainWindow.Bounds.Height);
                        mainWindow.Position = new PixelPoint(
                                workArea.X + (workArea.Width  - width)  / 2,
                                workArea.Y + (workArea.Height - height) / 2);
                        }
                        mainWindow.Opacity = 1;
                        mainWindow.Activate();
                    splash.Close();
                    _ctx.SplashWindow = null;
                }
            }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.FilteredFiles):
                OnUpdateColumns();
                UpdateEmptyState();
                break;
            case nameof(MainViewModel.PreviewEmbeddedOpen):
                UpdateMainGrid();
                break;
            case nameof(MainViewModel.PreviewWindowOpen):
                OnTogglePreviewWindow();
                break;
            case nameof(MainViewModel.NrFilteredFiles):
                UpdateEmptyState();
                break;
            case nameof(MainViewModel.CurrentFile):
                UpdateOtherFilesEmptyState();
                UpdateVersionsEmptyHint();
                UpdateLayersEmptyHint();
                UpdateBookmarksEmptyHint();
                SyncLayerList();
                break;
            case nameof(MainViewModel.CurrentProject):
                SubscribeTodoItems();
                UpdateCollectionsEmptyHint();
                UpdateRecentEmptyHint();
                break;
        }
    }

    private void UpdateFont()
    {
        var window = (TopLevel)ParentWindow;
        if (window == null) return;

        // Read font settings from the UI viewmodel
        window.FontFamily = (FontFamily)Resources[_ctx.UI.Font]!;
        window.FontSize = _ctx.UI.FontSize;

        // Keep the detached preview window in sync
        if (_ctx.PreviewWindow is Window pw)
        {
            pw.FontFamily = window.FontFamily;
            pw.FontSize = _ctx.UI.FontSize;
        }
    }

    private void OnMainViewSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_ctx != null)
            _ctx.UI.ShowClock = e.NewSize.Height > 1250;
    }

    /// <summary>
    /// User clicked the "Sync" button on an individual folder in the sync
    /// status dropdown. Navigates to the project, syncs the single folder
    /// (showing the import dialog), and removes the entry on success.
    /// If the user cancels the import dialog the entry remains unsynced.
    /// </summary>
    private async void OnSyncSingleFolder(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SyncStatusEntry entry }) return;

        SyncStatusButton.Flyout?.Hide();
        try
        {
            var window = ParentWindow;
            if (window == null) return;
            await _ctx.SyncSingleEntryAsync(entry, window);
        }
        catch (Exception ex)
        {
            Utils.ErrorLogger.Log(ex, "FolderWatcher.SyncSingleFolder");
        }
    }

    private async void OnCheckAllFolders(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _ctx.CheckAllFoldersAsync();
        }
        catch (Exception ex)
        {
            Utils.ErrorLogger.Log(ex, "FolderWatcher.CheckAllFolders");
        }
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
            case nameof(_ctx.UI.AutoCacheNetworkFiles):
                _pwr.AutoCacheNetworkFiles = _ctx.UI.AutoCacheNetworkFiles;
                break;
            case nameof(_ctx.UI.ReadBytesMode):
                _pwr.ReadBytesMode = _ctx.UI.ReadBytesMode;
                break;
            case nameof(_ctx.UI.PreviewDarkModeTint):
                _pwr.DarkModeTint = _ctx.UI.PreviewDarkModeTint;
                break;
            case nameof(_ctx.UI.PreviewTintIntensity):
                _pwr.DarkModeTintIntensity = _ctx.UI.PreviewTintIntensity;
                break;
            case nameof(_ctx.UI.PreviewLightPaper):
                _pwr.LightPaper = _ctx.UI.PreviewLightPaper;
                break;
            case nameof(_ctx.UI.CalendarOpen):
                if (_ctx.UI.CalendarOpen)
                    Dispatcher.UIThread.Post(RefreshCalendarDayIndicators, DispatcherPriority.Render);
                break;
            case nameof(_ctx.UI.FolderWatchEnabled):
                _ctx.RefreshFolderWatchers();
                if (_ctx.UI.FolderWatchEnabled)
                {
                    _ = _ctx.CheckFolderSyncOnStartupAsync();
                    _ = _ctx.CheckSharedSyncOnStartupAsync();
                }
                else
                    _ctx.DismissFolderSyncNotification();
                break;
            case nameof(_ctx.UI.FontSize):
            case nameof(_ctx.UI.FontSizeCompact):
                PushViewModelResources();
                break;
            case nameof(_ctx.UI.AlternatingRowShading):
                ApplyAlternatingRowShading();
                break;
        }
    }

    /// <summary>
    /// Pushes ViewModel values into UserControl.Resources so DataTemplates
    /// can access them via DynamicResource (avoids $parent[DataGrid] which
    /// Avalonia 12 cannot resolve at runtime).
    /// </summary>
    private void PushViewModelResources()
    {
        Resources["ViewFontSize"] = (double)_ctx.UI.FontSize;
        Resources["ViewFontSizeCompact"] = (double)_ctx.UI.FontSizeCompact;
        Resources["CalendarHours"] = _ctx.Calendar.Hours;
        Resources["CalendarProjectNames"] = _ctx.Calendar.SelectableProjectNames;
    }

    private void OnTogglePreviewWindow()
    {
        if (_ctx.PreviewWindowOpen)
        {
            var window = (TopLevel)ParentWindow;
            if (window is not null)
                _ctx.OpenPreviewWindow(window.RequestedThemeVariant!);
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
        try
        {
            var (files, folders) = ExtractDroppedFilesAndFolders(e, extension: ".pdf");
            var window = ParentWindow;
            if (files.Count > 0)
                await _ctx.AddDroppedFilesAsync(files, window);
            if (folders.Count > 0)
                await _ctx.AddDroppedFoldersAsync(folders, window);
            UpdateEmptyState();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, "OnDrop"); }
    }

    private async void OnDropOtherFiles(object? sender, DragEventArgs e)
    {
        SetDropHintActive(OtherFilesDropOverlay, false);
        try
        {
            var (files, folders) = ExtractDroppedFilesAndFolders(e);
            await _ctx.AddDroppedOtherFilesAsync(files, folders);
            UpdateOtherFilesEmptyState();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, "OnDropOtherFiles"); }
    }

    private async void OnDropVersionFolder(object? sender, DragEventArgs e)
    {
        SetDropHintActive(FolderDropOverlay, false);
        try
        {
            var (_, folders) = ExtractDroppedFilesAndFolders(e);
            if (folders.Count == 0) return;

            var window = ParentWindow;
            await _ctx.AddDroppedVersionFoldersAsync(folders, window);
            UpdateFolderEmptyState();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, "OnDropVersionFolder"); }
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
        var items = e.DataTransfer.TryGetFiles();
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
        var items = e.DataTransfer.TryGetFiles();
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

    #region Internal Row Drag-Drop (nest files into groups)

    private Point _dragStartPoint;
    private bool _isDraggingToGroup;
    private const double DragThreshold = 10;

    private void OnFileGridPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isDraggingToGroup = false;
        if (!e.GetCurrentPoint(FileGrid).Properties.IsLeftButtonPressed) return;
        _dragStartPoint = e.GetPosition(FileGrid);
    }

    private void OnFileGridPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!e.GetCurrentPoint(FileGrid).Properties.IsLeftButtonPressed) return;
        if (_isDraggingToGroup) return;
        if (_ctx?.CurrentFiles == null || _ctx.CurrentFiles.Count == 0) return;

        var pos = e.GetPosition(FileGrid);
        var delta = pos - _dragStartPoint;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        // Only start a drag for top-level, non-group files
        if (_ctx.CurrentFiles.Any(f => !f.IsRegularFile)) return;

        _isDraggingToGroup = true;
        FileGrid.Cursor = new Cursor(StandardCursorType.DragMove);
    }

    private void OnFileGridPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDraggingToGroup) return;
        _isDraggingToGroup = false;
        FileGrid.Cursor = Cursor.Default;

        var target = FindGroupRowUnderPointer(e);
        if (target != null && _ctx?.CurrentFiles != null)
            _ctx.MoveFilesToParent(target, _ctx.CurrentFiles.ToList());
    }

    /// <summary>
    /// Hit-tests the pointer position against the DataGrid to find a group
    /// or parent row that files can be dropped into.
    /// </summary>
    private FileData? FindGroupRowUnderPointer(PointerEventArgs e)
    {
        var pos = e.GetPosition(FileGrid);
        var hit = FileGrid.InputHitTest(pos);
        if (hit is not Visual visual) return null;

        var row = visual.FindAncestorOfType<DataGridRow>();
        if (row?.DataContext is FileData data && data.IsParent)
        {
            // Don't allow dropping onto a file that's part of the selection
            if (_ctx.CurrentFiles?.Contains(data) == true) return null;
            return data;
        }

        return null;
    }

    #endregion

    #region Empty State

    private void UpdateEmptyState()
    {
        bool empty = _ctx.FilteredFiles == null || _ctx.FilteredFiles.Count == 0;
        EmptyStateHint.IsVisible = empty;

        UpdateOtherFilesEmptyState();
        UpdateFolderEmptyState();
        UpdateVersionsEmptyHint();
        UpdateLayersEmptyHint();
        UpdateCollectionsEmptyHint();
        UpdateBookmarksEmptyHint();
        UpdateRecentEmptyHint();
        UpdateTodoEmptyHint();
    }

    private void UpdateOtherFilesEmptyState()
    {
        var file = _ctx.OtherFilesOwner;
        OtherFilesEmptyHint.IsVisible = file?.OtherFiles == null || file.OtherFiles.Count == 0;
    }

    private void UpdateFolderEmptyState()
    {
        FolderEmptyHint.IsVisible = _ctx.CurrentProject?.Folders == null || _ctx.CurrentProject.Folders.Count == 0;
    }

    private void UpdateVersionsEmptyHint()
    {
        var versions = _ctx.CurrentFile?.Versions;
        VersionsEmptyHint.IsVisible = versions == null || versions.Count == 0;
    }

    private void UpdateLayersEmptyHint()
    {
        var rendererLayers = AnnotationRenderer?.Layers;
        LayersEmptyHint.IsVisible = rendererLayers == null || rendererLayers.Count == 0;
    }

    private void UpdateCollectionsEmptyHint()
    {
        CollectionsEmptyHint.IsVisible = _ctx.Storage.Collections == null || _ctx.Storage.Collections.Count == 0;
        var content = _ctx.Collections.CollectionContent;
        CollectionContentEmptyHint.IsVisible = content == null || content.Count == 0;
    }

    private void UpdateBookmarksEmptyHint()
    {
        // Use MainViewModel.CurrentFile (synchronous selection) rather than
        // PreviewVM.CurrentFile, which is set asynchronously after the preview
        // loads and may still point to the previously selected file when this
        // method is called immediately after a selection change.
        var favPages = _ctx.CurrentFile?.FavPages;
        BookmarksEmptyHint.IsVisible = favPages == null || favPages.Count == 0;
    }

    private void UpdateRecentEmptyHint()
    {
        var recent = _ctx.PreviewVM?.RecentFiles;
        RecentEmptyHint.IsVisible = recent == null || recent.Count == 0;
    }

    #endregion

    #region Alternating Row Shading

    private void ApplyAlternatingRowShading()
    {
        bool enabled = _ctx.UI.AlternatingRowShading;
        foreach (var grid in new[] { FileGrid, RecentGrid, BookmarkGrid, VersionsGrid, OtherFilesGrid,
                                     Collections, CollectionContent })
        {
            if (enabled)
            {
                if (!grid.Classes.Contains("AltRows"))
                    grid.Classes.Add("AltRows");
            }
            else
            {
                grid.Classes.Remove("AltRows");
            }
        }
    }

    #endregion

    #region Search

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _ctx.Search();
            OnUpdateColumns();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, "OnSearch"); }
    }

    private void OnStartSearch(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            OnSearch(null!, null!);
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
        try
        {
            await _ctx.RequestPreviewAsync(file, searchText);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(RequestPreview)); }
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
        using (SuppressSelection())
        {
            if (active != FileGrid)        FileGrid.SelectedItem = null;
            if (active != CollectionContent) CollectionContent.SelectedItem = null;
            if (active != RecentGrid)      RecentGrid.SelectedItem = null;
        }
    }

    private void OnTreeviewSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressTreeSelection)
            return;

        if (MainTree.SelectedItem is not TreeNodeData selectedNode)
            return;

        string? tag = selectedNode.Tag;

        if (tag is "Header" or "Group" or "Subgroup")
        {
            _suppressTreeSelection = true;
            MainTree.SelectedItem = null;
            _suppressTreeSelection = false;
            using (SuppressSelection())
                FileGrid.SelectedItem = null;
            _ = _ctx.PreviewVM.CloseRendererAsync();
            return;
        }

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
            _ctx.OpenFileDirect(file.Filepath); // works for both file paths and URLs
    }

    private void OnOtherFileDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (OtherFilesGrid.SelectedItem is OtherData file)
            _ctx.OpenFileDirect(file.Filepath);
    }

    private async void OnAddOtherLink(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_ctx.OtherFilesOwner == null) return;

            var window = ParentWindow;
            var dialog = new Finn.Dialogs.xEditLinkDia();
            _ctx.ConfigureWindow(dialog, window);
            await dialog.ShowDialog(window);

            if (!dialog.Confirmed) return;

            var entry = new OtherData();
            entry.SetLink(dialog.ResultUrl!);
            entry.Name = dialog.ResultName!;
            _ctx.OtherFilesOwner.OtherFiles.Add(entry);
            _ctx.MarkDirty();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnAddOtherLink)); }
    }

    private async void OnEditOtherLink(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (OtherFilesGrid.SelectedItem is not OtherData file || !file.IsLink) return;

            var window = ParentWindow;
            var dialog = new Finn.Dialogs.xEditLinkDia();
            _ctx.ConfigureWindow(dialog, window);
            dialog.Populate(file.Name, file.Filepath);
            await dialog.ShowDialog(window);

            if (!dialog.Confirmed) return;

            file.Name     = dialog.ResultName!;
            file.Filepath = dialog.ResultUrl!;
            _ctx.MarkDirty();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnEditOtherLink)); }
    }

    private void OnOpenOtherFolder(object? sender, RoutedEventArgs e)
    {
        if (OtherFilesGrid.SelectedItem is OtherData file)
            _ctx.OpenPathDirect(file.Filepath);
    }

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _ctx.AddFile(this);
            _ctx.BuildTreeData();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnAddFiles)); }
    }

    private async void OnLoadFile(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _ctx.LoadFile(this);
            _ctx.BuildTreeData();
            UpdateFont();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnLoadFile)); }
    }

    private async void OnSaveFile(object? sender, RoutedEventArgs e)
    {
        try { await _ctx.SaveFile(this); }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnSaveFile)); }
    }
    private async void OnSaveFileAuto(object? sender, RoutedEventArgs e)
    {
        try
        {
            await _ctx.SaveFileAuto();
            _ctx.RefreshFolderWatchers();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnSaveFileAuto)); }
    }

    private async void OnRunIntegrityCheck(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
            await _ctx.ShowIntegrityReportAsync(window);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnRunIntegrityCheck)); }
    }

    private async void OnExportProjectZip(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
            var dialog = new xExportOptionsDia();
            _ctx.ConfigureWindow(dialog, window);
            dialog.RequestedThemeVariant = window.ActualThemeVariant;

            await dialog.ShowDialog(window);

            if (!dialog.Confirmed)
                return;

            await _ctx.CreateZipFromProjectAsync(dialog.Options);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnExportProjectZip)); }
    }

    /// <summary>
    /// Prevents the file grid context menu from opening when no file is selected.
    /// Refreshes dynamic submenu sources like AvailableParents.
    /// </summary>
    private void OnFileGridContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Always show the context menu so the user can create groups
        // even before any files exist. Items that require a selection
        // are hidden via IsVisible bindings that check FileSelected,
        // SelectedFileIsTopLevel, etc.
        // Force-refresh the submenu because context menus are detached
        // from the visual tree and may not pick up binding updates.
        MoveToGroupMenuItem.ItemsSource = _ctx.AvailableParents;

        // Update the toggle label to reflect the current state of the selected file.
        bool isDesignated = _ctx.CurrentFile?.IsDesignatedParent == true;
        ToggleDesignatedParentMenuItem.Header = isDesignated ? "Unmark as Parent" : "Mark as Parent";
        // Hide for groups — they are already always in the parent list.
        ToggleDesignatedParentMenuItem.IsVisible = _ctx.SelectedFileIsTopLevel && !(_ctx.CurrentFile?.IsGroup == true);
    }

    private void ReselectFile(FileData? file)
    {
        if (file != null && _ctx.FilteredFiles.Contains(file))
        {
            FileGrid.SelectedItem = file;
            FileGrid.ScrollIntoView(file, null);
            _ctx.SelectFiles([file]);
            return;
        }

        if (_ctx.FilteredFiles.Count > 0)
        {
            var firstFile = _ctx.FilteredFiles[0];
            FileGrid.SelectedItem = firstFile;
            FileGrid.ScrollIntoView(firstFile, null);
            _ctx.SelectFiles([firstFile]);
        }
        else
        {
            _ctx.SelectFiles([]);
        }
    }

    private async void OnRemoveFiles(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
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
                using (SuppressSelection(suppressTree: true))
                {
                    _ctx.RemoveSelectedFiles();
                    _ctx.UpdateFilter();
                    _ctx.BuildTreeData();
                }

                // Re-select the parent in the grid (when removing an appended file)
                // or select the first remaining file (to avoid stale CurrentFile references)
                ReselectFile(parentToSelect);
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnRemoveFiles)); }
    }

    private async void OnRemoveOtherFile(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
            var selected = OtherFilesGrid.SelectedItems.OfType<OtherData>().ToList();
            if (selected.Count == 0) return;

            await _ctx.ConfirmDeleteDia(window);

            if (_ctx.Confirmed)
            {
                _ctx.RemoveOtherFiles(selected);
                UpdateOtherFilesEmptyState();
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnRemoveOtherFile)); }
    }

    private async void OnRemoveProject(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_ctx.Storage.StoredProjects.Count <= 1) return;

            // Warn specifically about shared projects
            if (_ctx.CurrentProject?.IsShared == true)
            {
                var window = ParentWindow;
                var msgDialog = new Finn.Dialogs.xMessageDia();
                _ctx.ConfigureWindow(msgDialog, window);
                msgDialog.SetMessage("This project is shared. Removing it will disconnect from the server file.");
                await msgDialog.ShowDialog(window);
            }

            var mainWindow = ParentWindow;
            await _ctx.ConfirmDeleteDia(mainWindow);

            if (_ctx.Confirmed)
            {
                _ctx.RemoveProject();
                _ctx.MarkDirty();
                _ctx.BuildTreeData();
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnRemoveProject)); }
    }

    

    private async void OnAttachFiles(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_ctx.CurrentFile == null || _ctx.CurrentFile.IsChild) return;

            var parentName = _ctx.CurrentFile.Namn;
            var existing = _ctx.CurrentProject!.GetChildren(_ctx.CurrentFile)
                .OrderBy(f => f.Namn);

            var window = ParentWindow;
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

            // Suppress the grid's SelectionChanged handler so that
            // UpdateFilter calls inside AddAppendedFile don't clear
            // CurrentFile via a collection-Reset selection loss.
            using (SuppressSelection())
            {
                foreach (string path in dialog.AcceptedFiles)
                    _ctx.AddAppendedFile(path);

                foreach (string folderPath in dialog.AcceptedFolders)
                    await _ctx.AddAttachedFolderAsync(folderPath);
            }

            _ctx.RefreshFolderWatchers();

            // Ensure the parent is expanded so newly attached children are visible
            if (_ctx.CurrentFile is { HasChildren: true, IsExpanded: false })
                _ctx.CurrentFile.IsExpanded = true;

            _ctx.UpdateFilter();
            UpdateFolderEmptyState();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnAttachFiles)); }
    }

    private void OnToggleGroupExpanded(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is FileData file && file.HasChildren)
        {
            _ctx.ToggleExpansionInPlace(file);
            UpdateRowColor();
        }
    }

    private void OnToggleGroupExpandedDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        e.Handled = true;
    }

    private async void OnNewGroup(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
            var dialog = new Finn.Dialogs.xPlaceholderDia();
            _ctx.ConfigureWindow(dialog, window);
            dialog.SetTitle("New Group");
            dialog.NewFileName.Watermark = "Group name";
            await dialog.ShowDialog(window);

            string? name = dialog.ResultName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                // Only auto-move when multiple files are explicitly selected.
                // A single selection is usually just the user browsing, not
                // an intentional "group these files" action.
                var selectedFiles = _ctx.CurrentFiles?.Where(f => f.IsRegularFile).ToList() ?? [];
                bool autoMove = selectedFiles.Count > 1;

                // If all selected files share the same category, inherit it
                string? sharedCategory = null;
                if (selectedFiles.Count > 0)
                {
                    var distinct = selectedFiles.Select(f => f.Filtyp).Distinct().ToList();
                    if (distinct.Count == 1)
                        sharedCategory = distinct[0];
                }

                // Suppress selection events so SyncExpansionToSelection doesn't
                // collapse the group between AddGroup and MoveFilesToParent.
                using (SuppressSelection())
                {
                    var group = _ctx.AddGroup(name, sharedCategory);

                    if (autoMove)
                        _ctx.MoveFilesToParent(group, selectedFiles);
                }
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnNewGroup)); }
    }

    private async void OnRenameGroup(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_ctx.CurrentFile is not { IsGroup: true } group) return;

            var window = ParentWindow;
            var dialog = new Finn.Dialogs.xPlaceholderDia();
            _ctx.ConfigureWindow(dialog, window);
            dialog.SetTitle("Rename Group");
            dialog.NewFileName.Watermark = "New group name";
            dialog.NewFileName.Text = group.Namn;
            await dialog.ShowDialog(window);

            string? newName = dialog.ResultName;
            if (!string.IsNullOrWhiteSpace(newName))
                _ctx.RenameGroup(group, newName);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnRenameGroup)); }
    }

    private async void OnConvertToGroup(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_ctx.CurrentFile == null || _ctx.CurrentFile.IsGroup || _ctx.CurrentFile.IsAppendedFile) return;

            var window = ParentWindow;
            await _ctx.ConfirmDeleteDia(window);

            if (_ctx.Confirmed)
                _ctx.ConvertToGroup(_ctx.CurrentFile);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnConvertToGroup)); }
    }

    private void OnMoveToGroup(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { SelectedItem: FileData group })
        {
            var files = _ctx.CurrentFiles.ToList();
            int moved = _ctx.MoveFilesToParent(group, files);
            int skipped = files.Count - moved;
            if (skipped > 0 && moved > 0)
                _ctx.PreviewVM.StatusMessage = $"Attached {moved} file(s) to \"{group.Namn}\" \u2014 {skipped} skipped";
        }
    }

    private void OnDetachFiles(object? sender, RoutedEventArgs e)
    {
        _ctx.DetachFiles(_ctx.CurrentFiles.ToList());
    }

    private void OnToggleDesignatedParent(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile == null || _ctx.CurrentFile.IsGroup || _ctx.CurrentFile.IsChild) return;
        _ctx.ToggleDesignatedParent(_ctx.CurrentFile);
    }

    private void OnDissolveGroup(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile is { IsGroup: true } group)
            _ctx.DissolveGroup(group);
    }

    private async void OnTreeNewGroup(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
            var dialog = new Finn.Dialogs.xNewGroupDia();
            _ctx.ConfigureWindow(dialog, window);
            await dialog.ShowDialog(window);

            if (!string.IsNullOrWhiteSpace(dialog.ResultName))
            {
                _ctx.AddProjectGroup(dialog.ResultName, dialog.ResultCategory, dialog.ResultParentGroup);
                _ctx.BuildTreeData();
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnTreeNewGroup)); }
    }

    private async void OnTreeNewSubgroup(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
            var dialog = new Finn.Dialogs.xNewGroupDia();
            _ctx.ConfigureWindow(dialog, window);

            // Pre-select the right-clicked group so it opens as the parent
            string? parentGroupName = _lastRightClickedNode?.GroupName;
            if (!string.IsNullOrEmpty(parentGroupName))
                dialog.PreSelectGroup(parentGroupName);

            await dialog.ShowDialog(window);

            if (!string.IsNullOrWhiteSpace(dialog.ResultName))
            {
                _ctx.AddProjectGroup(dialog.ResultName, dialog.ResultCategory, dialog.ResultParentGroup);
                _ctx.BuildTreeData();
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnTreeNewSubgroup)); }
    }

    private async void OnTreeRenameGroup(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? groupName = _lastRightClickedNode?.GroupName;
            if (string.IsNullOrEmpty(groupName)) return;

            var group = _ctx.Storage.ProjectGroups.FirstOrDefault(g => g.Name == groupName);
            if (group == null) return;

            var window = ParentWindow;
            var dialog = new Finn.Dialogs.xPlaceholderDia();
            _ctx.ConfigureWindow(dialog, window);
            dialog.SetTitle("Rename Group");
            dialog.NewFileName.Watermark = "New name";
            dialog.NewFileName.Text = group.Name;
            await dialog.ShowDialog(window);

            string? newName = dialog.ResultName;
            if (!string.IsNullOrWhiteSpace(newName))
            {
                _ctx.RenameProjectGroup(group, newName);
                _ctx.BuildTreeData();
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnTreeRenameGroup)); }
    }

    private async void OnTreeRemoveGroup(object? sender, RoutedEventArgs e)
    {
        try
        {
            string? groupName = _lastRightClickedNode?.GroupName;
            if (string.IsNullOrEmpty(groupName)) return;

            var group = _ctx.Storage.ProjectGroups.FirstOrDefault(g => g.Name == groupName);
            if (group == null) return;

            var window = ParentWindow;
            await _ctx.ConfirmDeleteDia(window);
            if (!_ctx.Confirmed) return;

            _ctx.RemoveProjectGroup(group);
            _ctx.BuildTreeData();
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnTreeRemoveGroup)); }
    }

    private void OnTreeMoveProjectToGroup(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentProject == null) return;

        string projectCategory = _ctx.CurrentProject.Category;

        // Only show groups that belong to the same category as the project
        var allGroups = _ctx.Storage.ProjectGroups
            .Where(g => g.Category == projectCategory)
            .OrderBy(g => g.ParentGroup ?? "")
            .ThenBy(g => g.SortOrder)
            .ToList();

        // Build display entries: indent subgroups visually
        var entries = new List<(string Display, string? GroupName)>();
        entries.Add(("— Top Level —", null));
        foreach (var g in allGroups)
        {
            string indent = string.IsNullOrEmpty(g.ParentGroup) ? "" : "    ";
            entries.Add(($"{indent}{g.Name}", g.Name));
        }

        var menu = new Avalonia.Controls.ContextMenu();
        foreach (var (display, groupName) in entries)
        {
            var item = new Avalonia.Controls.MenuItem { Header = display };
            var captured = groupName;
            item.Click += (_, _) =>
            {
                _ctx.CurrentProject.Parent = captured;
                _ctx.MarkDirty();
                _ctx.BuildTreeData();
            };
            menu.Items.Add(item);
        }

        // Open the menu anchored to the treeview
        menu.Open(MainTree);
    }

    private void OnOpenFolderPath(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = FolderGrid.SelectedItems.OfType<FolderData>().ToList();
            foreach (var folder in folders)
            {
                if (!string.IsNullOrEmpty(folder.Path))
                    _ctx.OpenFileDirect(folder.Path);
            }
        }
        catch (Exception ex)
        {
            Utils.ErrorLogger.Log(ex, nameof(OnOpenFolderPath));
            _ = _ctx.OpenMessageDia(ParentWindow, $"Could not open the selected folder path. {ex.Message}");
        }
    }

    private void OnFolderDoubleClick(object? sender, RoutedEventArgs e)
    {
        if (FolderGrid.SelectedItem is FolderData folder && !string.IsNullOrEmpty(folder.Path))
            _ctx.OpenFileDirect(folder.Path);
    }

    private async void OnSyncSelectedFolders(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = FolderGrid.SelectedItems.OfType<FolderData>().ToList();
            if (folders.Count == 0) return;

            var window = ParentWindow;
            await _ctx.SyncFoldersAsync(folders, window);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnSyncSelectedFolders)); }
    }

    private async void OnSyncAllFolders(object? sender, RoutedEventArgs e)
    {
        try
        {
            var window = ParentWindow;
            await _ctx.SyncFoldersAsync(_ctx.CurrentProject!.Folders.ToList(), window);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnSyncAllFolders)); }
    }

    private async void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = FolderGrid.SelectedItems.OfType<FolderData>().ToList();
            if (folders.Count == 0) return;

            var window = ParentWindow;
            await _ctx.ConfirmDeleteDia(window);

            if (_ctx.Confirmed)
            {
                _ctx.RemoveFolders(folders);
                _ctx.MarkDirty();
                UpdateFolderEmptyState();
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnRemoveFolder)); }
    }

    private async void OnManageSyncFilter(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (FolderGrid.SelectedItem is not FolderData folder) return;
            var window = ParentWindow;
            await _ctx.ShowSyncFilterDialogAsync(folder, window);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnManageSyncFilter)); }
    }

    private async void OnChangeFolderDirectory(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (FolderGrid.SelectedItem is not FolderData folder) return;
            var window = ParentWindow;

            var dialog = new Finn.Dialogs.xChangeFolderDia();
            _ctx.ConfigureWindow(dialog, window);
            dialog.SetCurrentPath(folder.Name, folder.Path);

            await dialog.ShowDialog(window);

            if (dialog.Confirmed && !string.Equals(folder.Path, dialog.SelectedPath, StringComparison.OrdinalIgnoreCase))
            {
                string oldPath = folder.Path;
                _ctx.RelocateFolderPaths(folder, oldPath, dialog.SelectedPath);
                folder.Path = dialog.SelectedPath;
                // Path.GetFileName returns empty string for root paths (e.g. "C:\");
                // fall back to the full path so the folder always has a visible name.
                folder.Name = System.IO.Path.GetFileName(dialog.SelectedPath.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar))
                    is { Length: > 0 } n ? n : dialog.SelectedPath;
                _ctx.MarkDirty();
                _ctx.RefreshFolderWatchers();
            }
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnChangeFolderDirectory)); }
    }

    private void OnFolderTypesInfo(object? sender, RoutedEventArgs e)
    {
        var window = (Window)ParentWindow;
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
        using (SuppressSelection())
        {
            var files = CollectionContent.SelectedItems.Cast<FileData>().ToList();
            var target = files.FirstOrDefault();
            if (target == null) return;

            SelectAuxiliaryFile(target, files, addRecentForTopLevel: true, addRecentForChild: true);
        }
    }

    private void SelectRecent(object? sender, RoutedEventArgs e)
    {
        if (_isUpdatingSelection) return;
        ClearOtherGridSelections(RecentGrid);
        using (SuppressSelection())
        {
            var files = RecentGrid.SelectedItems.Cast<FileData>().ToList();
            var target = files.FirstOrDefault();
            if (target == null) return;

            SelectAuxiliaryFile(target, files, addRecentForTopLevel: false, addRecentForChild: false);
        }
    }

    private void SelectAuxiliaryFile(FileData target, IReadOnlyList<FileData> selection, bool addRecentForTopLevel, bool addRecentForChild)
    {
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
            if (addRecentForChild)
                _pwr.AddRecentFile(target);

            FileGrid.SelectedItem = target;
            _ctx.SelectFiles([target]);
            return;
        }

        _ctx.SelectAndNavigateFiles(selection.ToList());
        SelectInFileGrid(target, addRecent: addRecentForTopLevel);
    }

    private void OnRecentOpen(object? sender, RoutedEventArgs e)
    {
        var file = RecentGrid.SelectedItem as FileData;
        if (file == null) return;
        _ctx.OpenFileDirect(file.Sökväg);
    }

    private void OnRecentOpenFolder(object? sender, RoutedEventArgs e)
    {
        var file = RecentGrid.SelectedItem as FileData;
        if (file == null) return;
        _ctx.OpenPathDirect(file.Sökväg);
    }

    private async void OnRecentCopyPath(object? sender, RoutedEventArgs e)
    {
        try
        {
            var file = RecentGrid.SelectedItem as FileData;
            if (file == null) return;
            var topLevel = (TopLevel)ParentWindow;
            if (topLevel?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(file.Sökväg);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnRecentCopyPath)); }
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
    }

    private void OnClearFiles(object? sender, RoutedEventArgs e)
    {
        _ctx.ClearAll();
        FileGrid.SelectedItem = null;
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
        try
        {
            if (!_pwr.DualFileMode) return;
            var file = (FileGrid.SelectedItem ?? CollectionContent.SelectedItem) as FileData;
            if (file != null)
                await _ctx.RequestPreviewLeftAsync(file);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnViewLeft)); }
    }

    private async void OnViewRight(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!_pwr.DualFileMode) return;
            var file = (FileGrid.SelectedItem ?? CollectionContent.SelectedItem) as FileData;
            if (file != null)
                await _ctx.RequestPreview2Async(file);
        }
        catch (Exception ex) { Utils.ErrorLogger.Log(ex, nameof(OnViewRight)); }
    }

    #endregion

    

    

    


    #region Row Styling

    private void DataGrid_OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        var row = e.Row;
        if (_trackedRows.Add(row))
            row.DataContextChanged += OnRowDataContextChanged;
        BindRowToFileData(row, row.DataContext as FileData);
        ApplyRowClasses(row, IsMainFileGridRow(row));
    }

    private void OnRowDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is DataGridRow row)
        {
            BindRowToFileData(row, row.DataContext as FileData);
            ApplyRowClasses(row, IsMainFileGridRow(row));
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
            if (args.PropertyName is nameof(FileData.IsFileMissing) or nameof(FileData.Färg) or nameof(FileData.Sökväg)
                or nameof(FileData.HasChildren) or nameof(FileData.IsExpanded)
                or nameof(FileData.IsGroup) or nameof(FileData.IsAppendedFile)
                or nameof(FileData.IsStyledAsAttached))
                Dispatcher.UIThread.Post(() => ApplyRowClasses(row, IsMainFileGridRow(row)));
        };
        newData.PropertyChanged += handler;
        _rowBindings[row] = (newData, handler);
    }

    private void OnUpdateColumns()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var sizeToCell = new DataGridLength(1.0, DataGridLengthUnitType.SizeToCells);
            int count = Math.Min(FileGrid.Columns.Count, 10);
            for (int i = 0; i < count; i++)
                FileGrid.Columns[i].Width = sizeToCell;

            FileGrid.InvalidateMeasure();
            FileGrid.InvalidateArrange();
        }, DispatcherPriority.Loaded);
    }

    private void UpdateRowColor()
    {
        foreach (var row in _trackedRows)
            ApplyRowClasses(row, IsMainFileGridRow(row));
    }

    private static readonly string[] AllColorClasses =
        ["Yellow", "Orange", "Brown", "Green", "Blue", "Red", "Magenta"];

    private bool IsMainFileGridRow(DataGridRow row) => row.FindAncestorOfType<DataGrid>() == FileGrid;

    private void ApplyRowClasses(DataGridRow row, bool isMainFileGrid = false)
    {
        if (row.DataContext is not FileData data)
        {
            row.Classes.Remove("MissingFile");
            row.Classes.Remove("ParentRow");
            row.Classes.Remove("GroupRow");
            row.Classes.Remove("ChildRow");
            row.Classes.Remove("AttachedChildRow");
            foreach (var c in AllColorClasses)
                row.Classes.Remove(c);
            return;
        }

        bool isPlaceholder = data.Sökväg == string.Empty;

        SetClass(row, "MissingFile", data.IsFileMissing && !isPlaceholder);

        foreach (var c in AllColorClasses)
            row.Classes.Remove(c);

        if (isMainFileGrid)
        {
            bool isParentRow = data.HasChildren || data.IsGroup;
            SetClass(row, "ParentRow", isParentRow);
            SetClass(row, "GroupRow", data.IsGroup);
            SetClass(row, "ChildRow", data.IsAppendedFile);
            SetClass(row, "AttachedChildRow", data.IsStyledAsAttached);
        }
        else
        {
            row.Classes.Remove("ParentRow");
            row.Classes.Remove("GroupRow");
            row.Classes.Remove("ChildRow");
            row.Classes.Remove("AttachedChildRow");
        }
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

    
}