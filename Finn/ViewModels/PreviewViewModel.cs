using System.Threading.Tasks;
using System.Threading;
using System.Collections.ObjectModel;
using Finn.Model;
using Finn.Services;
using System;
using Microsoft.Extensions.Logging;
using Avalonia.Media;
using MuPDFCore.MuPDFRenderer;
using System.Text.RegularExpressions;
using Avalonia.Collections;
using MuPDFCore;
using Avalonia.Threading;
using System.IO;
using System.Collections.Generic;
using MuPDFCore.StructuredText;
using System.Linq;
using Avalonia.Controls;
using System.Diagnostics;

namespace Finn.ViewModels
{
    /// <summary>
    /// View model for previewing files and handling PDF rendering.
    /// </summary>
    public partial class PreviewViewModel : ViewModelBase, IAsyncDisposable
    {
        #region Constants
        private const int MAX_RECENT_FILES = 20;
        private const double ZOOM_LEVEL = 0.35;
        private const int RENDER_DELAY = 5;
        #endregion

        #region Fields
        private readonly SemaphoreSlim renderSemaphore = new(1, 1);
        private bool disposed = false;
        private int fileGeneration = 0;
        private int secondaryFileGeneration = 0;
        private CancellationTokenSource secondaryCts = new();
        private TaskCompletionSource? searchDone; // Signalled when SearchDocumentAsync finishes
        private CancellationTokenSource? _diffCts;
        private CancellationTokenSource? _backgroundTaskCts;
        private readonly LocalFileCache _fileCache = new(
            Path.Combine(MainViewModel.SavePath, "Cache"));
        private string? _mainPinnedCachePath;   // cached path held open by MuPDF (fast-open)
        private string? _secondaryPinnedCachePath; // cached path held open by secondary doc
        private bool _autoCacheNetworkFiles; // snapshot of UI setting
        #endregion

        #region Constructor
        public PreviewViewModel(ILogger<PreviewViewModel>? logger = null) : base(logger)
        {
            recentFiles = new AvaloniaList<FileData>();
            searchPages = new AvaloniaList<int>();
            searchPagesText = new AvaloniaList<string>();

            mainCts = new CancellationTokenSource();
            searchCts = new CancellationTokenSource();

            statusMessage = "Ready!";
            linkedPageMode = true;
        }
        #endregion

        #region PDF Document Properties
        private MuPDFDocument? mainPreviewFile = null;

        /// <summary>Visible in the toolbar when the current file is cached.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool ShowForceServerRead => CurrentFile?.IsCached == true || RequestFile?.IsCached == true;

        /// <summary>Number of files currently in the local cache.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public int CacheFileCount => _fileCache.CachedFileCount;

        /// <summary>
        /// When true, network files are automatically routed through the local
        /// cache before preview — even if not explicitly marked <c>IsCached</c>.
        /// Set by MainViewModel from <see cref="UISettingsViewModel.AutoCacheNetworkFiles"/>.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool AutoCacheNetworkFiles
        {
            get => _autoCacheNetworkFiles;
            set => SetProperty(ref _autoCacheNetworkFiles, value);
        }

        private bool _readBytesMode;
        /// <summary>
        /// When true, files are read entirely into a byte[] before passing to
        /// MuPDF instead of opening by path. Avoids holding file locks on
        /// network shares but uses more memory. Set by MainView from UI settings.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool ReadBytesMode
        {
            get => _readBytesMode;
            set => SetProperty(ref _readBytesMode, value);
        }

        /// <summary>Total size in bytes of all cached files.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public long CacheTotalBytes => _fileCache.CachedTotalBytes;

        /// <summary>Clears all cached files and removes the index.</summary>
        public void ClearCache() => _fileCache.Clear();

        /// <summary>
        /// Removes cache entries whose server paths are not in the provided set.
        /// Call once at startup after loading Projects.json.
        /// </summary>
        public void ReconcileCache(IReadOnlySet<string> validPaths) => _fileCache.Reconcile(validPaths);

        /// <summary>
        /// Pre-caches a single file in the background (no document open).
        /// Used when the user marks files for caching.
        /// </summary>
        public async Task PreCacheFileAsync(string serverPath, CancellationToken token = default)
        {
            await _fileCache.GetLocalPathAsync(serverPath, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves a file path through the local cache when possible.
        /// Returns the original path unchanged for non-network or local files.
        /// Use this for any code path that reads a file (diff, export, etc.)
        /// so it benefits from the local cache.
        /// </summary>
        public async Task<string> ResolveCachedPathAsync(string path, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            // Fast exit: local files don't need cache resolution
            if (!LocalFileCache.IsNetworkPath(path))
                return path;
            var result = await _fileCache.GetLocalPathAsync(path, token).ConfigureAwait(false);
            return result.Path;
        }

        /// <summary>
        /// Immediately invalidates the local cache for the current file and
        /// reloads it from the server.
        /// </summary>
        public async Task ForceServerRead()
        {
            var file = CurrentFile ?? RequestFile;
            if (file?.Sökväg == null)
            {
                StatusMessage = "No file to reload";
                return;
            }

            string target = file.Sökväg;
            _fileCache.Invalidate(target);
            StatusMessage = $"Reloading {Path.GetFileName(target)} from server…";

            RequestFile = file;
            await SetFileAsync().ConfigureAwait(false);
        }
        public MuPDFDocument? MainPreviewFile
        {
            get => mainPreviewFile;
            internal set => SetProperty(ref mainPreviewFile, value);
        }

        private MuPDFContext? context = null;
        private bool fileAvailable = false;
        private MuPDFDocument? secondaryFile = null;
        private MuPDFContext? secondaryContext = null;
        private int _secondaryCloseGen;
        private int _dualViewGen;

        /// <summary>
        /// Atomically swaps the secondary document and context fields, disposing the
        /// old ones in the correct order (document before context). Prevents the GC
        /// finalizer crash where a document is finalized after its context is already gone.
        /// Must be called on the UI thread when the renderer is not using the old document.
        /// </summary>
        private void SwapSecondaryDocument(MuPDFDocument? newDoc, MuPDFContext? newCtx)
        {
            var oldDoc = secondaryFile;
            var oldCtx = secondaryContext;
            secondaryFile = newDoc;
            secondaryContext = newCtx;
            // Dispose in strict order: document first, then context
            try { oldDoc?.Dispose(); } catch { }
            try { oldCtx?.Dispose(); } catch { }
        }
        #endregion

        #region File Properties
        private FileData? currentFile = null;
        public FileData? CurrentFile
        {
            get => currentFile;
            set
            {
                if (SetProperty(ref currentFile, value))
                {
                    OnPropertyChanged(nameof(CanCompareVersions));
                    OnPropertyChanged(nameof(ShowForceServerRead));
                }
            }
        }

        /// <summary>
        /// The real file when previewing a version (the stub doesn't carry HasVersions).
        /// Set by MainViewModel.PreviewVersionAsync, cleared when leaving version preview.
        /// </summary>
        private FileData? _versionSourceFile;
        public FileData? VersionSourceFile
        {
            get => _versionSourceFile;
            set
            {
                _versionSourceFile = value;
                OnPropertyChanged(nameof(CanCompareVersions));
                OnPropertyChanged(nameof(IsViewingVersion));
            }
        }

        /// <summary>True when previewing a specific version of a file.</summary>
        public bool IsViewingVersion => _versionSourceFile != null;

        private FileData? currentFile2 = null;
        public FileData? CurrentFile2
        {
            get => currentFile2;
            set => SetProperty(ref currentFile2, value);
        }

        private FileData? requestFile = null;
        public FileData? RequestFile
        {
            get => requestFile;
            set => SetProperty(ref requestFile, value);
        }

        private AvaloniaList<FileData> recentFiles;
        public AvaloniaList<FileData> RecentFiles
        {
            get => recentFiles;
            set => SetProperty(ref recentFiles, value);
        }
        #endregion

        #region Page Management Properties
        private int requestPage1 = 0;
        public int RequestPage1
        {
            get => requestPage1;
            set
            {
                SetProperty(ref requestPage1, value);

                if (LinkedPageMode)
                {
                    // Update the secondary page number, then render both
                    // pages in a single semaphore+dispatch for tight sync.
                    if (DualFileMode)
                        SetProperty(ref requestPage2, requestPage1, nameof(RequestPage2));
                    else
                        SetProperty(ref requestPage2, requestPage1 + 1, nameof(RequestPage2));
                    _ = SetLinkedPagesAsync();
                }
                else
                {
                    if (PageInRange(requestPage1))
                        _ = SetMainPageAsync();
                }
            }
        }

        private int requestPage2 = 0;
        public int RequestPage2
        {
            get => requestPage2;
            set
            {
                SetProperty(ref requestPage2, value);
                bool inRange = DualFileMode ? PageInRange2(requestPage2) : PageInRange(requestPage2);
                if (inRange)
                    _ = SetSecondaryPageAsync();
            }
        }

        private int currentPage1 = 0;
        public int CurrentPage1
        {
            get => currentPage1;
            set
            {
                if (SetProperty(ref currentPage1, value) && CurrentFile != null)
                    CurrentFile.DefaultPage = value;
            }
        }

        private int currentPage2 = 0;
        public int CurrentPage2
        {
            get => currentPage2;
            set => SetProperty(ref currentPage2, value);
        }

        private int pagecount = 0;
        public int Pagecount
        {
            get => pagecount;
            set
            {
                if (SetProperty(ref pagecount, value))
                    OnPropertyChanged(nameof(SecondaryPagecount));
            }
        }

        private int pagecount2 = 0;
        public int Pagecount2
        {
            get => pagecount2;
            set
            {
                if (SetProperty(ref pagecount2, value))
                    OnPropertyChanged(nameof(SecondaryPagecount));
            }
        }

        public int SecondaryPagecount => DualFileMode ? pagecount2 : pagecount;
        public bool ShowSecondaryControls => !linkedPageMode || dualFileMode;
        public bool ShowLinkedPageButton => twopageMode;

        /// <summary>
        /// Fires change notifications for all computed properties that depend
        /// on mode backing fields (dualFileMode, twopageMode, linkedPageMode,
        /// _diffOverlayActive). Call after directly setting any of these.
        /// Eliminates the "forgot to notify property X" bug class.
        /// </summary>
        private void NotifyModeChanged()
        {
            OnPropertyChanged(nameof(DualFileMode));
            OnPropertyChanged(nameof(TwopageMode));
            OnPropertyChanged(nameof(LinkedPageMode));
            OnPropertyChanged(nameof(SecondaryPagecount));
            OnPropertyChanged(nameof(ShowSecondaryControls));
            OnPropertyChanged(nameof(ShowLinkedPageButton));
            OnPropertyChanged(nameof(IsUserDualFileMode));
            OnPropertyChanged(nameof(CanToggleLayout));
            OnPropertyChanged(nameof(CanSearch));
            OnPropertyChanged(nameof(CanAnnotate));
        }
        #endregion

        #region View Mode Properties
        private bool twopageMode = false;
        public bool TwopageMode
        {
            get => twopageMode;
            set
            {
                // Block activation while annotating or diff is active; always allow deactivation.
                if (value && (AnnotationActive || _diffOverlayActive)) return;
                // When deactivating, hide the secondary renderer BEFORE the
                // property change collapses the grid column to 0px.  The
                // PDFRenderer throws if arranged at less than 1×1.
                if (!value && twopageMode && secondaryRenderer != null)
                {
                    secondaryRenderer.ReleaseResources();
                    secondaryRenderer.IsVisible = false;
                }
                SetProperty(ref twopageMode, value, () => _ = ToggleDualViewAsync());
            }
        }

        private bool dualFileMode = false;
        public bool DualFileMode
        {
            get => dualFileMode;
            set
            {
                // Block activation while annotating or diff is active; always allow deactivation.
                if (value && (AnnotationActive || _diffOverlayActive)) return;
                if (SetProperty(ref dualFileMode, value))
                {
                    OnPropertyChanged(nameof(SecondaryPagecount));
                    OnPropertyChanged(nameof(ShowSecondaryControls));
                    OnPropertyChanged(nameof(ShowLinkedPageButton));
                    OnPropertyChanged(nameof(IsUserDualFileMode));
                    OnPropertyChanged(nameof(CanSearch));
                    OnPropertyChanged(nameof(CanAnnotate));
                    // Auto-close search when entering a mode that blocks it
                    if (value && searchMode) SearchMode = false;
                    if (value)
                    {
                        if (twopageMode)
                            twopageMode = false; // reset backing field silently so TwopageMode = true below triggers ToggleDualViewAsync
                        if (secondaryRenderer != null)
                        {
                            secondaryRenderer.IsVisible = false;
                            secondaryRenderer.ReleaseResources();
                        }
                        requestPage2 = requestPage1; // pre-sync pages as a starting point
                        TwopageMode = true;
                        LinkedPageMode = false;
                    }
                    else
                    {
                        // Release and hide BEFORE collapsing the column (TwopageMode=false)
                        // to prevent ArrangeOverride creating 0-size WriteableBitmaps.
                        secondaryRenderer?.ReleaseResources();
                        if (secondaryRenderer != null)
                            secondaryRenderer.IsVisible = false;
                        CurrentFile2 = null;
                        Pagecount2 = 0;
                        TwopageMode = false; // triggers ToggleDualViewAsync ? reverts to single page layout
                        _ = DisposeSecondaryDocumentAsync();
                    }
                }
            }
        }

        private bool linkedPageMode = true;
        public bool LinkedPageMode
        {
            get => linkedPageMode;
            set => SetProperty(ref linkedPageMode, value, ToggleLinkedMode);
        }

        /// <summary>True when Dual-File mode was activated by the user (not by diff side-by-side).</summary>
        public bool IsUserDualFileMode => dualFileMode && !_diffOverlayActive;

        /// <summary>
        /// True when the user may toggle Dual-Page / Dual-File modes.
        /// Blocked during annotation mode and while a diff comparison is active
        /// (diff manages its own layout).
        /// </summary>
        public bool CanToggleLayout => !annotationActive && !_diffOverlayActive;

        /// <summary>
        /// True when search is available. Blocked during diff comparisons (the
        /// secondary document is not the current file) and in Dual-File mode
        /// (search targets the main file only, which is confusing in split view).
        /// </summary>
        public bool CanSearch => !_diffOverlayActive && !dualFileMode;

        /// <summary>
        /// True when the search panel is showing diff page results rather than
        /// text search results. Controls header text and hides the search input.
        /// </summary>
        private bool _diffPageListMode;
        public bool DiffPageListMode
        {
            get => _diffPageListMode;
            set => SetProperty(ref _diffPageListMode, value);
        }

        /// <summary>
        /// True when annotation mode may be activated. Blocked in Dual-File mode
        /// where there are two independent documents and annotation targets are
        /// ambiguous.
        /// </summary>
        public bool CanAnnotate => !dualFileMode;

        private double rotation = 0;
        public double Rotation
        {
            get => rotation;
            set => SetProperty(ref rotation, value);
        }

        private bool darkMode = false;
        public bool DarkMode
        {
            get => darkMode;
            set
            {
                if (SetProperty(ref darkMode, value))
                    OnPropertyChanged(nameof(PreviewBackground));
            }
        }

        private Color themeRegionColor = Colors.White;
        /// <summary>
        /// The current theme region/background color, set externally by MainViewModel
        /// when UI colors change. Used by <see cref="PreviewBackground"/> to compute
        /// the correct inverse for the Difference blend.
        /// </summary>
        public Color ThemeRegionColor
        {
            get => themeRegionColor;
            set
            {
                if (SetProperty(ref themeRegionColor, value))
                {
                    OnPropertyChanged(nameof(PreviewBackground));
                    OnPropertyChanged(nameof(UiBackground));
                }
            }
        }

        public IBrush UiBackground => new SolidColorBrush(ThemeRegionColor);

        /// <summary>
        /// Background for the preview area. When DarkMode is on, this returns the
        /// RGB-inverse of the actual theme background so that after the
        /// InvertColorControl's Difference blend the visible result matches
        /// the original theme color.
        /// </summary>
        public IBrush PreviewBackground
        {
            get
            {
                if (!DarkMode)
                    return Brushes.Transparent;

                // Compute the RGB inverse: the Difference blend does |dst - src|
                // with white (1,1,1), i.e. 1 - dst.  If we set dst = inverse(bg),
                // the result is 1 - (1 - bg) = bg — the original theme color.
                var inverted = Color.FromRgb(
                    (byte)(255 - ThemeRegionColor.R),
                    (byte)(255 - ThemeRegionColor.G),
                    (byte)(255 - ThemeRegionColor.B));

                return new SolidColorBrush(inverted);
            }
        }
        #endregion

        #region Renderer Properties
        private PDFRenderer? mainRenderer;
        private PDFRenderer? secondaryRenderer;
        #endregion

        #region Search Properties
        private Regex? regex = null;

        private AvaloniaList<int> searchPages;
        public AvaloniaList<int> SearchPages
        {
            get => searchPages;
            set => SetProperty(ref searchPages, value);
        }

        private AvaloniaList<string> searchPagesText;
        public AvaloniaList<string> SearchPagesText
        {
            get => searchPagesText;
            set => SetProperty(ref searchPagesText, value);
        }

        private int searchPageIndex = 0;
        public int SearchPageIndex
        {
            get => searchPageIndex;
            set => SetProperty(ref searchPageIndex, value, SetSearchPage);
        }

        private int searchItems = 0;
        public int SearchItems
        {
            get => searchItems;
            set => SetProperty(ref searchItems, value);
        }

        private bool searchMode = false;
        public bool SearchMode
        {
            get => searchMode;
            set
            {
                // Reject activation when search is blocked (diff/dual-file mode)
                if (value && !CanSearch) return;
                SetProperty(ref searchMode, value);
            }
        }

        /// <summary>
        /// When true, the view should not steal focus to the search TextBox.
        /// Set before activating SearchMode from automatic (indexed) searches
        /// so the FileGrid keeps focus for arrow-key navigation.
        /// </summary>
        internal bool SuppressSearchFocus { get; set; }

        private bool searchBusy = false;
        public bool SearchBusy
        {
            get => searchBusy;
            set => SetProperty(ref searchBusy, value);
        }
        #endregion

        #region Status Properties
        private bool fileWorkerBusy = false;
        public bool FileWorkerBusy
        {
            get => fileWorkerBusy;
            set
            {
                if (SetProperty(ref fileWorkerBusy, value))
                    OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }

        private bool diffBusy = false;
        public bool DiffBusy
        {
            get => diffBusy;
            set
            {
                if (SetProperty(ref diffBusy, value))
                    OnPropertyChanged(nameof(ShowDiffToolbar));
            }
        }

        private string statusMessage;
        public string StatusMessage
        {
            get => statusMessage;
            set => SetProperty(ref statusMessage, value);
        }

        private string? _cacheSourceIcon;
        /// <summary>
        /// FluentIcon symbol name indicating the source of the last file load.
        /// "Database" when served from local cache, "Cloud" when read from
        /// the network server, "ArrowSync" when a stale cache was refreshed,
        /// null when caching is off or no file loaded.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? CacheSourceIcon
        {
            get => _cacheSourceIcon;
            private set
            {
                if (SetProperty(ref _cacheSourceIcon, value))
                    OnPropertyChanged(nameof(CacheSourceTooltip));
            }
        }

        /// <summary>Human-readable tooltip for the cache source icon.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string? CacheSourceTooltip => _cacheSourceIcon switch
        {
            "Database" => "Loaded from local cache",
            "Cloud" => "Read from server",
            "ArrowSync" => "Cache updated — file changed on server",
            _ => null,
        };

        private int progress = 0;
        public int Progress
        {
            get => progress;
            set
            {
                if (SetProperty(ref progress, value))
                    OnPropertyChanged(nameof(IsProgressIndeterminate));
            }
        }

        /// <summary>
        /// True when <see cref="FileWorkerBusy"/> is active but no byte-level
        /// progress is available (e.g. file-path open). The view binds this to
        /// <c>ProgressBar.IsIndeterminate</c> for a marquee effect.
        /// Gated on <see cref="FileWorkerBusy"/> so the indeterminate animation
        /// stops when the overlay is hidden — Avalonia's DispatcherTimer keeps
        /// ticking on invisible controls and steals UI-thread time from the
        /// PDF renderer, causing pan/zoom stutter.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsProgressIndeterminate => fileWorkerBusy && progress == 0;

        #region Background Task Progress
        private bool _backgroundTaskActive;
        /// <summary>True when a background task (pre-caching, etc.) is running.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool BackgroundTaskActive
        {
            get => _backgroundTaskActive;
            set
            {
                if (SetProperty(ref _backgroundTaskActive, value))
                    OnPropertyChanged(nameof(BackgroundTaskCancellable));
            }
        }

        private string _backgroundTaskMessage = "";
        /// <summary>Status text for the background task (e.g. "Pre-caching 3/10…").</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string BackgroundTaskMessage
        {
            get => _backgroundTaskMessage;
            set => SetProperty(ref _backgroundTaskMessage, value);
        }

        private int _backgroundTaskProgress;
        /// <summary>Progress percentage (0–100) for the background task.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public int BackgroundTaskProgress
        {
            get => _backgroundTaskProgress;
            set => SetProperty(ref _backgroundTaskProgress, value);
        }

        /// <summary>True when the active background task supports cancellation.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool BackgroundTaskCancellable => _backgroundTaskActive && _backgroundTaskCts != null;

        /// <summary>
        /// Sets the CTS for the current background task so the UI cancel button can stop it.
        /// </summary>
        public void SetBackgroundTaskCts(CancellationTokenSource? cts) => _backgroundTaskCts = cts;

        /// <summary>Cancels the active background task if one is running.</summary>
        public void CancelBackgroundTask()
        {
            _backgroundTaskCts?.Cancel();
        }

        /// <summary>
        /// Cancels the current <see cref="SetFileAsync"/> operation (cache copy,
        /// document open, or render). Called from the loading overlay Cancel button.
        /// Immediately hides the loading overlay for instant visual feedback;
        /// the background operation cleans up via the cancellation token.
        /// </summary>
        public void CancelFileLoad()
        {
            FileWorkerBusy = false;
            Progress = 0;
            StatusMessage = "Cancelled";
            try { mainCts.Cancel(); } catch { }
            // Also cancel diff if it was the active operation
            _diffCts?.Cancel();
        }
        #endregion

        private bool whiteboardMode;
        public bool WhiteboardMode
        {
            get => whiteboardMode;
            set => SetProperty(ref whiteboardMode, value);
        }

        /// <summary>
        /// True while the user is actively annotating. Set by the view
        /// so the ViewModel can block dual-page/dual-file activation.
        /// </summary>
        private bool annotationActive;
        public bool AnnotationActive
        {
            get => annotationActive;
            set
            {
                if (SetProperty(ref annotationActive, value))
                    OnPropertyChanged(nameof(CanToggleLayout));
            }
        }

        /// <summary>
        /// Standalone annotation layers for whiteboard mode.
        /// Separate from any FileData so whiteboard strokes never
        /// interfere with file annotations.
        /// </summary>
        public ObservableCollection<AnnotationLayer> WhiteboardLayers { get; } = [];
        #endregion

        #region Cancellation Tokens
        private CancellationTokenSource mainCts;
        private CancellationTokenSource searchCts;
        #endregion

        #region Renderer Management
        public void GetRenderControl(PDFRenderer mainRenderer, PDFRenderer secondaryRenderer)
        {
            try
            {
                var background = new SolidColorBrush(Colors.White);

                if (this.mainRenderer != null)
                    this.mainRenderer.ReleaseResources();
                if (this.secondaryRenderer != null && this.secondaryRenderer != secondaryRenderer)
                    this.secondaryRenderer.ReleaseResources();

                this.mainRenderer = mainRenderer;
                this.secondaryRenderer = secondaryRenderer;

                this.mainRenderer.PageBackground = background;
                this.secondaryRenderer.PageBackground = background;

                if (CurrentFile != null)
                    _ = SetMainPageAsync();
                // Skip secondary init when diff mode is active — the View's
                // SyncDiffOverlay will set up the secondary renderer with the
                // correct layout (Toggle/SBS) after this method returns.
                if (!_diffOverlayActive && DualFileMode && CurrentFile2 != null)
                    _ = SetSecondaryPageAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error getting render control");
            }
        }

        public void ClearRenderer()
        {
            try
            {
                mainRenderer?.ReleaseResources();
                secondaryRenderer?.ReleaseResources();
                mainRenderer = null;
                secondaryRenderer = null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error clearing renderer");
            }
        }

        public void SetupPage(int page = 0)
        {
            RequestPage1 = Math.Max(0, page);
        }

        // Minimal blank A4 PDF (595 × 842 pt) used as the whiteboard canvas.
        private static readonly byte[] BlankA4Pdf = System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.0\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n" +
            "2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n" +
            "3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 842 595]>>endobj\n" +
            "xref\n0 4\n0000000000 65535 f \n0000000009 00000 n \n" +
            "0000000058 00000 n \n0000000115 00000 n \n" +
            "trailer<</Size 4/Root 1 0 R>>\nstartxref\n190\n%%EOF");

        /// <summary>
        /// Opens a blank A4 page in the previewer to use as a whiteboard.
        /// The caller is responsible for activating annotation mode after this returns.
        /// </summary>
        public async Task OpenWhiteboardAsync()
        {
            if (disposed) return;

            // Whiteboard is single-page only — collapse dual modes first.
            if (DualFileMode)
                DualFileMode = false;
            if (TwopageMode)
                TwopageMode = false;

            await DisposeCurrentDocumentAsync(CancellationToken.None).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var wbContext = new MuPDFContext();
                var wbDoc = new MuPDFDocument(wbContext, BlankA4Pdf, InputFileTypes.PDF);

                var prevDoc = MainPreviewFile;
                var prevCtx = context;

                MainPreviewFile = wbDoc;
                context = wbContext;
                Pagecount = 1;
                RequestFile = null;
                CurrentFile = null;
                WhiteboardMode = true;
                fileAvailable = true;

                prevDoc?.Dispose();
                prevCtx?.Dispose();

                StatusMessage = "Whiteboard";

                if (mainRenderer != null)
                {
                    mainRenderer.ReleaseResources();
                    mainRenderer.Initialize(MainPreviewFile, 1, 0, ZOOM_LEVEL);
                    mainRenderer.IsVisible = true;
                    CurrentPage1 = 0;
                    RequestPage1 = 0;
                }

                FileWorkerBusy = false;
            }).GetTask().ConfigureAwait(false);
        }
        #endregion

        #region File Operations
        public async Task SetFileAsync(string? search = null, CancellationToken cancellationToken = default, bool preserveDualFile = false)
        {
            if (disposed || RequestFile?.Sökväg == null)
                return;

            WhiteboardMode = false;

            // Close any active diff mode BEFORE disposing the document.
            // DisposeCurrentDocumentAsync skips the secondary renderer while
            // dualFileMode is true, so we must reset it synchronously first.
            // When preserveDualFile is set (View Left in Dual-File mode),
            // only close diff state — keep the dual-file layout intact.
            if (_diffOverlayActive)
            {
                await Dispatcher.UIThread.InvokeAsync(CloseDiffModeSync).GetTask().ConfigureAwait(false);
            }
            else if (dualFileMode && !preserveDualFile)
            {
                await Dispatcher.UIThread.InvokeAsync(CloseDiffModeSync).GetTask().ConfigureAwait(false);
            }

            int myGeneration = Interlocked.Increment(ref fileGeneration);

            // Cancel previous load
            try
            {
                await mainCts.CancelAsync().ConfigureAwait(false);
                mainCts.Dispose();
            }
            catch { }

            mainCts = new CancellationTokenSource();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, mainCts.Token);
            var token = linkedCts.Token;

            // Small debounce to coalesce rapid selection changes. This reduces
            // race conditions when users quickly toggle files and avoids rapidly
            // creating/disposing native MuPDF objects which can cause crashes.
            try
            {
                await Task.Delay(25, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A newer request or cancellation arrived — abort early.
                return;
            }

            StatusMessage = "Setting File";
            fileAvailable = false;
            FileWorkerBusy = true;
            var swTotal = Stopwatch.StartNew();

            try
            {
                // Cancel any running search immediately. The old document
                // stays alive while the new one is created on a background
                // thread — both are disposed/swapped atomically in a single
                // UI dispatch below, saving two round-trips.
                try
                {
                    await searchCts.CancelAsync().ConfigureAwait(false);
                    searchCts.Dispose();
                }
                catch { }
                searchCts = new CancellationTokenSource();
                SearchBusy = false;
                ClearSearch();

                if (IsStale(myGeneration)) return;

                string path = RequestFile.Sökväg;
                var reqFileRef = RequestFile;

                // Route through local cache when the file is explicitly cached
                // or when AutoCacheNetworkFiles is on and this is a network path.
                bool cacheHit = false;
                bool cachedLocally = false;
                bool wasStale = false;
                string originalPath = path;
                bool useCache = RequestFile.IsCached
                    || (_autoCacheNetworkFiles && LocalFileCache.IsNetworkPath(path));

                if (useCache)
                {
                    StatusMessage = "Caching…";
                    var result = await _fileCache.GetLocalPathAsync(path, token, p => Progress = p).ConfigureAwait(false);
                    Progress = 0;
                    cacheHit = result.WasCacheHit;
                    wasStale = result.WasStale;
                    cachedLocally = !string.Equals(result.Path, path, StringComparison.OrdinalIgnoreCase);
                    path = result.Path;
                }

                CacheSourceIcon = useCache
                    ? (wasStale ? "ArrowSync" : (cacheHit ? "Database" : "Cloud"))
                    : null;

                string openPath = path;

                // Mark the file as cached so the UI shows the cache indicator
                // and the setting persists for future sessions.
                if (useCache && cachedLocally && !reqFileRef.IsCached)
                    reqFileRef.IsCached = true;

                // Create MuPDF objects on the background thread — document
                // construction is pure native file I/O with no UI dependency.
                // Only the renderer (Initialize) requires the UI thread.
                StatusMessage = "Opening…";
                MuPDFContext previewContext;
                MuPDFDocument previewDoc;
                var sw = Stopwatch.StartNew();
                previewContext = new MuPDFContext();
                try
                {
                    if (_readBytesMode)
                    {
                        byte[] fileBytes = await File.ReadAllBytesAsync(openPath, token).ConfigureAwait(false);
                        previewDoc = new MuPDFDocument(previewContext, fileBytes, InputFileTypes.PDF);
                    }
                    else
                    {
                        previewDoc = new MuPDFDocument(previewContext, openPath);
                    }
                }
                catch
                {
                    previewContext.Dispose();
                    throw;
                }

                if (token.IsCancellationRequested || IsStale(myGeneration))
                {
                    previewDoc.Dispose();
                    previewContext.Dispose();
                    return;
                }

                // Pin/unpin cache paths before entering the UI dispatch
                if (cachedLocally)
                    _fileCache.Pin(path);
                UnpinMainCachePath();
                _mainPinnedCachePath = cachedLocally ? path : null;

                var reqFile = RequestFile!;
                int desired = Math.Clamp(reqFile.DefaultPage, 0,
                    Math.Max(0, previewDoc.Pages.Count - 1));

                // Atomic swap: one semaphore + one UI dispatch replaces the
                // previous three separate round-trips (dispose ? create ? render).
                await renderSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // --- Release old renderer resources ---
                        if (mainRenderer?.HighlightedRegions != null)
                            mainRenderer.HighlightedRegions = null;
                        if (!dualFileMode && secondaryRenderer?.HighlightedRegions != null)
                            secondaryRenderer.HighlightedRegions = null;
                        mainRenderer?.ReleaseResources();
                        if (!dualFileMode)
                            secondaryRenderer?.ReleaseResources();

                        // --- Dispose old, swap in new ---
                        var prevDoc = MainPreviewFile;
                        var prevCtx = context;
                        MainPreviewFile = previewDoc;
                        context = previewContext;
                        Pagecount = previewDoc.Pages.Count;
                        CurrentFile = reqFile;
                        fileAvailable = true;
                        prevDoc?.Dispose();
                        prevCtx?.Dispose();

                        // --- Page setup ---
                        if (!DualFileMode) LinkedPageMode = true;
                        if (Pagecount <= 1 && twopageMode) TwopageMode = false;
                        requestPage1 = desired;
                        OnPropertyChanged(nameof(RequestPage1));
                        currentPage1 = -1;
                        Rotation = 0;
                        if (!string.IsNullOrEmpty(search))
                        {
                            SuppressSearchFocus = true;
                            SearchMode = true;
                        }

                        // --- Render first page ---
                        if (mainRenderer != null)
                        {
                            mainRenderer.ReleaseResources();
                            mainRenderer.Initialize(MainPreviewFile!, 1, desired, ZOOM_LEVEL);
                            mainRenderer.IsVisible = true;
                            SetSearchResults();
                            CurrentPage1 = desired;
                        }

                        // --- Secondary page (two-page linked mode) ---
                        if (!DualFileMode && LinkedPageMode && TwopageMode
                            && PageInRange(desired + 1) && secondaryRenderer != null)
                        {
                            requestPage2 = desired + 1;
                            OnPropertyChanged(nameof(RequestPage2));
                            secondaryRenderer.ReleaseResources();
                            secondaryRenderer.Initialize(MainPreviewFile!, 1, requestPage2, ZOOM_LEVEL);
                            secondaryRenderer.IsVisible = true;
                            SetSecondarySearchResults();
                            CurrentPage2 = requestPage2;
                        }
                    }).GetTask().ConfigureAwait(false);
                }
                finally
                {
                    renderSemaphore.Release();
                }

                if (!string.IsNullOrEmpty(search))
                    _ = SearchAsync(search, token);

                sw.Stop();
                swTotal.Stop();
                StatusMessage = $"Opened in {swTotal.ElapsedMilliseconds} ms";
            }
            catch (OperationCanceledException)
            {
                Progress = 0;
                logger?.LogInformation("File load cancelled (gen {Generation})", myGeneration);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in SetFileAsync");
            }
            finally
            {
                if (!IsStale(myGeneration))
                    FileWorkerBusy = false;
            }
        }

        /// <summary>
        /// Returns true if a newer SetFileAsync call has superseded this one.
        /// </summary>
        private bool IsStale(int myGeneration)
            => Volatile.Read(ref fileGeneration) != myGeneration;

        /// <summary>Unpins the main document's cached path (if any) so the cache can clean it up.</summary>
        private void UnpinMainCachePath()
        {
            if (_mainPinnedCachePath != null)
            {
                _fileCache.Unpin(_mainPinnedCachePath);
                _mainPinnedCachePath = null;
            }
        }

        /// <summary>Unpins the secondary document's cached path (if any).</summary>
        private void UnpinSecondaryCachePath()
        {
            if (_secondaryPinnedCachePath != null)
            {
                _fileCache.Unpin(_secondaryPinnedCachePath);
                _secondaryPinnedCachePath = null;
            }
        }

        /// <summary>
        /// Quickly disposes the current document. Cancels search, releases
        /// renderer resources, and disposes MuPDF objects — all on the UI
        /// thread in a single dispatch (no polling loops).
        /// </summary>
        private async Task DisposeCurrentDocumentAsync(CancellationToken token)
        {
            // Cancel any running search without polling
            try
            {
                await searchCts.CancelAsync().ConfigureAwait(false);
                searchCts.Dispose();
            }
            catch { }
            searchCts = new CancellationTokenSource();
            SearchBusy = false;
            ClearSearch();

            if (MainPreviewFile == null) return;

            // Ensure no render is in progress while disposing native resources.
            // Acquire the render semaphore so RenderCurrentPageAsync / SetMainPageAsync
            // cannot start an Initialize while we dispose the document and context.
            await renderSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (mainRenderer?.HighlightedRegions != null)
                            mainRenderer.HighlightedRegions = null;
                        if (!dualFileMode && secondaryRenderer?.HighlightedRegions != null)
                            secondaryRenderer.HighlightedRegions = null;

                        mainRenderer?.ReleaseResources();
                        if (!dualFileMode)
                            secondaryRenderer?.ReleaseResources();
                        MainPreviewFile?.Dispose();
                        context?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error during quick dispose");
                    }
                }).GetTask().ConfigureAwait(false);

                fileAvailable = false;
                MainPreviewFile = null;
                context = null;

                // Unpin cached path now that the native handle is closed
                UnpinMainCachePath();
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

        private async Task DisposeSecondaryDocumentAsync(CancellationToken token = default)
        {
            if (secondaryFile == null) return;

            await renderSemaphore.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        secondaryRenderer?.ReleaseResources();
                        SwapSecondaryDocument(null, null);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error during secondary dispose");
                    }
                }).GetTask().ConfigureAwait(false);

                // Unpin cached path now that the native handle is closed
                UnpinSecondaryCachePath();
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

        public async Task SetFile2Async(FileData file, CancellationToken cancellationToken = default)
        {
            if (disposed || file.Sökväg == null) return;

            int myGen = Interlocked.Increment(ref secondaryFileGeneration);
            try
            {
                await secondaryCts.CancelAsync().ConfigureAwait(false);
                secondaryCts.Dispose();
            }
            catch { }
            secondaryCts = new CancellationTokenSource();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, secondaryCts.Token);
            var token = linkedCts.Token;

            try { await Task.Delay(25, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            bool IsStale2() => Volatile.Read(ref secondaryFileGeneration) != myGen;

            try
            {
                await DisposeSecondaryDocumentAsync(token).ConfigureAwait(false);

                if (IsStale2()) return;

                // Resolve through local cache when the file is marked for caching
                // or auto-cache is enabled for network paths.
                string filePath = file.Sökväg;
                bool secondaryCachedLocally = false;
                bool useCache2 = file.IsCached
                    || (_autoCacheNetworkFiles && LocalFileCache.IsNetworkPath(filePath));

                if (useCache2)
                {
                    var result = await _fileCache.GetLocalPathAsync(filePath, token).ConfigureAwait(false);
                    secondaryCachedLocally = !string.Equals(result.Path, filePath, StringComparison.OrdinalIgnoreCase);
                    filePath = result.Path;
                }

                // Create MuPDF objects on the background thread (no UI dependency).
                MuPDFContext newContext;
                MuPDFDocument newDoc;
                newContext = new MuPDFContext();
                try
                {
                    if (_readBytesMode)
                    {
                        byte[] fileBytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
                        newDoc = new MuPDFDocument(newContext, fileBytes, InputFileTypes.PDF);
                    }
                    else
                    {
                        newDoc = new MuPDFDocument(newContext, filePath);
                    }
                }
                catch
                {
                    newContext.Dispose();
                    throw;
                }

                if (IsStale2() || token.IsCancellationRequested)
                {
                    newDoc.Dispose();
                    newContext.Dispose();
                    return;
                }

                // Pin the cached path so LRU eviction won't delete it while
                // MuPDF holds a native file handle.
                if (secondaryCachedLocally)
                    _fileCache.Pin(filePath);

                UnpinSecondaryCachePath();
                _secondaryPinnedCachePath = secondaryCachedLocally ? filePath : null;

                SwapSecondaryDocument(newDoc, newContext);
                Pagecount2 = newDoc.Pages.Count;
                CurrentFile2 = file;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    DualFileMode = true; // also resets TwopageMode and defaults LinkedPageMode = false
                    requestPage2 = linkedPageMode ? requestPage1 : 0;
                    OnPropertyChanged(nameof(RequestPage2));
                    CurrentPage2 = requestPage2;
                    _ = SetSecondaryPageAsync();
                    if (secondaryRenderer?.Bounds is { Width: > 0, Height: > 0 })
                        secondaryRenderer.Contain();
                }).GetTask().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger?.LogInformation("Secondary file load cancelled (gen {Generation})", myGen);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in SetFile2Async");
            }
        }
        #endregion

        #region Recent Files Management
        public void AddRecentFile(FileData? file)
        {
            if (file?.Uppdrag == string.Empty || file == null) return;

            try
            {
                int index = RecentFiles.IndexOf(file);

                // Already at the top — nothing to do
                if (index == 0) return;

                if (index > 0)
                    RecentFiles.RemoveAt(index);

                RecentFiles.Insert(0, file);

                while (RecentFiles.Count > MAX_RECENT_FILES)
                    RecentFiles.RemoveAt(RecentFiles.Count - 1);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error adding recent file");
            }
        }
        #endregion

        #region Page Navigation & Default Page
        public void NextPage(bool secondPage = false)
        {
            int lastPage = Pagecount - 1;
            int lastPage2 = Math.Max(0, Pagecount2 - 1);
            if (!TwopageMode) { if (RequestPage1 < lastPage) RequestPage1++; }
            else if (DualFileMode)
            {
                if (LinkedPageMode)
                {
                    // Left file still has pages: advance both in sync via RequestPage1 setter.
                    // Left file exhausted but right still has pages: let right advance independently.
                    if (RequestPage1 < lastPage) RequestPage1++;
                    else if (RequestPage2 < lastPage2) RequestPage2++;
                }
                else if (!secondPage) { if (RequestPage1 < lastPage) RequestPage1++; }
                else { if (RequestPage2 < lastPage2) RequestPage2++; }
            }
            else if (LinkedPageMode) { if (RequestPage1 + 2 <= lastPage) RequestPage1 += 2; }
            else if (!secondPage) { if (RequestPage1 < lastPage) RequestPage1++; }
            else { if (RequestPage2 < lastPage) RequestPage2++; }
        }

        public void PrevPage(bool secondPage = false)
        {
            if (!TwopageMode) { if (RequestPage1 > 0) RequestPage1--; }
            else if (DualFileMode)
            {
                if (LinkedPageMode)
                {
                    // Right is ahead (scrolled past left's end): walk right back first.
                    // Once in sync, decrement left which re-syncs right via the setter.
                    if (RequestPage2 > RequestPage1) RequestPage2--;
                    else if (RequestPage1 > 0) RequestPage1--;
                }
                else if (!secondPage) { if (RequestPage1 > 0) RequestPage1--; }
                else { if (RequestPage2 > 0) RequestPage2--; }
            }
            else if (LinkedPageMode) { if (RequestPage1 >= 2) RequestPage1 -= 2; }
            else if (!secondPage) { if (RequestPage1 > 0) RequestPage1--; }
            else { if (RequestPage2 > 0) RequestPage2--; }
        }

        public bool PageInRange(int pageNr) => pageNr >= 0 && pageNr < Pagecount;
        public bool PageInRange2(int pageNr) => pageNr >= 0 && pageNr < Pagecount2;
        #endregion

        #region View Mode Methods
        public async Task ToggleDualViewAsync()
        {
            int gen = Interlocked.Increment(ref _dualViewGen);
            try
            {
                OnPropertyChanged(nameof(ShowLinkedPageButton));
                await Task.Delay(RENDER_DELAY).ConfigureAwait(false);

                if (Volatile.Read(ref _dualViewGen) != gen) return;

                if (!DualFileMode && TwopageMode && RequestPage1 % 2 != 0)
                    requestPage1 = RequestPage1 - 1;

                if (!DualFileMode)
                    requestPage2 = requestPage1 + 1;

                // In DualFileMode the main document hasn't changed — only the layout
                // split. Contain() handles the resize, so skip the redundant re-render.
                if (!DualFileMode)
                    await SetMainPageAsync().ConfigureAwait(false);

                if (Volatile.Read(ref _dualViewGen) != gen) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (Volatile.Read(ref _dualViewGen) != gen) return;
                    mainRenderer?.Contain();
                    if (TwopageMode)
                    {
                        if (secondaryRenderer != null)
                            secondaryRenderer.IsVisible = true;
                        // In DualFileMode, retry secondary page init — the initial call
                        // in OpenDiffSideBySideAsync may have been skipped due to zero
                        // bounds before layout settled. Now bounds are valid.
                        _ = SetSecondaryPageAsync();
                        if (secondaryRenderer?.Bounds is { Width: > 0, Height: > 0 })
                            secondaryRenderer.Contain();
                    }
                    else if (secondaryRenderer != null)
                    {
                        secondaryRenderer.IsVisible = false;
                    }
                    if (!LinkedPageMode && !DualFileMode)
                        LinkedPageMode = true;
                    // Restore focus after layout settles — the caller's
                    // immediate Focus() call gets lost when Contain() /
                    // IsVisible changes reshape the visual tree.
                    mainRenderer?.Focus();
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error toggling dual view");
            }
        }

        public void ToggleLinkedMode()
        {
            OnPropertyChanged(nameof(ShowSecondaryControls));
            if (LinkedPageMode)
            {
                if (DualFileMode)
                {
                    RequestPage2 = RequestPage1;
                }
                else
                {
                    if (CurrentPage1 % 2 != 0)
                        RequestPage1 = CurrentPage1 - 1;
                    RequestPage2 = CurrentPage1 + 1;
                }
            }
        }

        #endregion

        #region Page Rendering
        private async Task SetMainPageAsync()
        {
            if (disposed || FileWorkerBusy || SearchBusy || !PageInRange(RequestPage1) || mainRenderer == null)
                return;

            int targetPage = RequestPage1;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                // Skip stale requests: if the user scrolled past this page
                // while we waited for the semaphore, render the latest instead.
                if (RequestPage1 != targetPage)
                    targetPage = RequestPage1;
                if (!PageInRange(targetPage)) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (mainRenderer?.HighlightedRegions != null)
                        mainRenderer.HighlightedRegions = null;
                    try
                    {
                        if (MainPreviewFile != null && mainRenderer != null)
                        {
                            // Skip redundant re-render when already showing
                            // this page (e.g. DarkMode toggle, layout changes).
                            if (CurrentPage1 == targetPage && mainRenderer.IsViewerInitialized)
                                return;

                            mainRenderer.ReleaseResources();
                            mainRenderer.Initialize(MainPreviewFile, 1, targetPage, ZOOM_LEVEL);
                            if (SearchPages?.Count > 0) SetSearchResults();
                            CurrentPage1 = targetPage;
                        }
                    }
                    catch (NullReferenceException nre)
                    {
                        logger?.LogError(nre, "NullReference in SetMainPageAsync UI invoke");
                        Finn.Utils.ErrorLogger.Log(nre, "SetMainPageAsync.UI");
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error setting main page");
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

        private async Task SetSecondaryPageAsync()
        {
            bool inRange = DualFileMode ? PageInRange2(RequestPage2) : PageInRange(RequestPage2);
            // Allow rendering when TwopageMode is active (side-by-side) OR when
            // the A/B toggle overlay is active (secondary in column 0, no TwopageMode).
            bool rendererActive = TwopageMode || _diffShowingOriginal || (DiffOverlayActive && _diffViewMode == DiffViewMode.Toggle);
            if (disposed || FileWorkerBusy || SearchBusy || !inRange || !rendererActive || secondaryRenderer == null)
                return;

            int targetPage = RequestPage2;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                // Skip stale requests after semaphore wait
                if (RequestPage2 != targetPage)
                    targetPage = RequestPage2;
                inRange = DualFileMode ? PageInRange2(targetPage) : PageInRange(targetPage);
                if (!inRange) return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (secondaryRenderer?.HighlightedRegions != null)
                        secondaryRenderer.HighlightedRegions = null;
                    try
                    {
                        var doc = DualFileMode ? secondaryFile : MainPreviewFile;
                        if (doc != null && secondaryRenderer != null)
                        {
                            // Skip redundant re-render when already showing this page.
                            if (CurrentPage2 == targetPage && secondaryRenderer.IsViewerInitialized)
                                return;

                            // Guard: skip Initialize when the renderer has zero bounds
                            // (not yet in layout). ToggleDualViewAsync or Contain()
                            // will retry after layout completes.
                            if (secondaryRenderer.Bounds.Width > 0 && secondaryRenderer.Bounds.Height > 0)
                            {
                                secondaryRenderer.ReleaseResources();
                                secondaryRenderer.Initialize(doc, 1, targetPage, ZOOM_LEVEL);
                                if (!DualFileMode && SearchPages?.Count > 0) SetSecondarySearchResults();
                                CurrentPage2 = targetPage;
                            }
                        }
                    }
                    catch (NullReferenceException nre)
                    {
                        logger?.LogError(nre, "NullReference in SetSecondaryPageAsync UI invoke");
                        Finn.Utils.ErrorLogger.Log(nre, "SetSecondaryPageAsync.UI");
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error setting secondary page");
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

                        /// <summary>
                        /// Renders both the main and secondary pages in a single semaphore
                        /// acquisition and a single UI-thread dispatch so they update in
                        /// the same frame. Used by <see cref="RequestPage1"/> when
                        /// <see cref="LinkedPageMode"/> is active.
                        /// </summary>
                        private async Task SetLinkedPagesAsync()
                        {
                            if (disposed || FileWorkerBusy || SearchBusy || mainRenderer == null)
                                return;

                            int page1 = requestPage1;
                            int page2 = requestPage2;
                            bool mainInRange = PageInRange(page1);
                            bool secInRange = DualFileMode ? PageInRange2(page2) : PageInRange(page2);
                            bool rendererActive = TwopageMode || _diffShowingOriginal
                                || (DiffOverlayActive && _diffViewMode == DiffViewMode.Toggle);

                            if (!mainInRange && !secInRange) return;

                            await renderSemaphore.WaitAsync().ConfigureAwait(false);
                            try
                            {
                                // Pick up latest page if user scrolled while we waited
                                if (requestPage1 != page1) page1 = requestPage1;
                                if (requestPage2 != page2) page2 = requestPage2;
                                mainInRange = PageInRange(page1);
                                secInRange = DualFileMode ? PageInRange2(page2) : PageInRange(page2);
                                if (!mainInRange && !secInRange) return;

                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    // -- Main page --
                                    if (mainInRange && mainRenderer != null && MainPreviewFile != null)
                                    {
                                        // Skip redundant re-render when already showing this page.
                                        if (CurrentPage1 == page1 && mainRenderer.IsViewerInitialized)
                                            goto secondaryPage;

                                        if (mainRenderer.HighlightedRegions != null)
                                            mainRenderer.HighlightedRegions = null;
                                        try
                                        {
                                            mainRenderer.ReleaseResources();
                                            mainRenderer.Initialize(MainPreviewFile, 1, page1, ZOOM_LEVEL);
                                            if (SearchPages?.Count > 0) SetSearchResults();
                                            CurrentPage1 = page1;
                                        }
                                        catch (NullReferenceException nre)
                                        {
                                            logger?.LogError(nre, "NullReference in SetLinkedPagesAsync main");
                                            Finn.Utils.ErrorLogger.Log(nre, "SetLinkedPagesAsync.main");
                                        }
                                    }

                                    secondaryPage:
                                    // -- Secondary page --
                                    if (secInRange && rendererActive && secondaryRenderer != null)
                                    {
                                        // Skip redundant re-render when already showing this page.
                                        if (CurrentPage2 == page2 && secondaryRenderer.IsViewerInitialized)
                                            return;

                                        if (secondaryRenderer.HighlightedRegions != null)
                                            secondaryRenderer.HighlightedRegions = null;
                                        try
                                        {
                                            var doc = DualFileMode ? secondaryFile : MainPreviewFile;
                                            if (doc != null)
                                            {
                                                if (secondaryRenderer.Bounds.Width > 0 && secondaryRenderer.Bounds.Height > 0)
                                                {
                                                    secondaryRenderer.ReleaseResources();
                                                    secondaryRenderer.Initialize(doc, 1, page2, ZOOM_LEVEL);
                                                    if (!DualFileMode && SearchPages?.Count > 0) SetSecondarySearchResults();
                                                    CurrentPage2 = page2;
                                                }
                                            }
                                        }
                                        catch (NullReferenceException nre)
                                        {
                                            logger?.LogError(nre, "NullReference in SetLinkedPagesAsync secondary");
                                            Finn.Utils.ErrorLogger.Log(nre, "SetLinkedPagesAsync.secondary");
                                        }
                                    }
                                }).GetTask().ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, "Error in SetLinkedPagesAsync");
                            }
                            finally
                            {
                                renderSemaphore.Release();
                            }
                        }
                        #endregion

                        #region Clipboard Operations
        public void CopyToClipboard(Avalonia.Visual window)
        {
            if (CurrentFile != null && mainRenderer != null)
            {
                string selectedText = mainRenderer.GetSelectedText();
                if (!string.IsNullOrEmpty(selectedText))
                    TopLevel.GetTopLevel(window)?.Clipboard?.SetTextAsync(selectedText);
            }
        }
        #endregion

        #region Cleanup and Disposal
        protected override async ValueTask DisposeAsyncCore()
        {
            if (disposed) return;
            try
            {
                await DisposeCurrentDocumentAsync(CancellationToken.None).ConfigureAwait(false);
                await DisposeSecondaryDocumentAsync(CancellationToken.None).ConfigureAwait(false);

                await mainCts.CancelAsync().ConfigureAwait(false);
                await searchCts.CancelAsync().ConfigureAwait(false);
                await secondaryCts.CancelAsync().ConfigureAwait(false);
                _diffCts?.Cancel();
                _backgroundTaskCts?.Cancel();
                mainCts.Dispose();
                searchCts.Dispose();
                secondaryCts.Dispose();
                _diffCts?.Dispose();
                _backgroundTaskCts?.Dispose();
                renderSemaphore.Dispose();
                _fileCache.Dispose();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during async dispose");
            }
            finally
            {
                disposed = true;
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        public async Task SafeDisposeAsync()
        {
            if (disposed) return;
            await DisposeCurrentDocumentAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public async Task CloseRendererAsync()
        {
            int gen = Volatile.Read(ref fileGeneration);
            await StopSearchAsync().ConfigureAwait(false);
            ClearSearch();
            // If SetFileAsync started a new load while we were waiting, don't dispose
            // the document it just created — that would blank the preview after the
            // first search-result click.
            if (Volatile.Read(ref fileGeneration) != gen) return;
            await SafeDisposeAsync().ConfigureAwait(false);
        }

        #endregion

        /// <summary>
        /// Updates the theme region color and forces re-evaluation of
        /// <see cref="PreviewBackground"/>.
        /// </summary>
        public void UpdateThemeRegionColor(Color color)
            => ThemeRegionColor = color;
    }
}
