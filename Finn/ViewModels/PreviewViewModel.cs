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
        private const double ZOOM_LEVEL = 0.2;
        private const int BUFFER_SIZE = 64 * 1024; // 64 KB — larger buffer = fewer syscalls
        private const int PROGRESS_UPDATE_INTERVAL = 20;
        private const int RENDER_DELAY = 20;
        #endregion

        #region Fields
        private readonly SemaphoreSlim renderSemaphore = new(1, 1);
        private bool disposed = false;
        private int fileGeneration = 0;
        private int secondaryFileGeneration = 0;
        private CancellationTokenSource secondaryCts = new();
        private bool fastOpenMode; // Toggle for fast open (first pages only) vs full open
        private TaskCompletionSource? searchDone; // Signalled when SearchDocumentAsync finishes
        private CancellationTokenSource? _diffCts;
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
        /// <summary>
        /// If true, only the first pages of a PDF are loaded for fast preview.
        /// </summary>
        public bool FastOpenMode
        {
            get => fastOpenMode;
            set => SetProperty(ref fastOpenMode, value);
        }
        public MuPDFDocument? MainPreviewFile
        {
            get => mainPreviewFile;
            internal set => SetProperty(ref mainPreviewFile, value);
        }

        private MuPDFContext? context = null;
        private byte[]? bytes;
        private bool fileAvailable = false;
        private MuPDFDocument? secondaryFile = null;
        private MuPDFContext? secondaryContext = null;
        private int _secondaryCloseGen;
        private int _dualViewGen;
        #endregion

        #region File Properties
        private FileData? currentFile = null;
        public FileData? CurrentFile
        {
            get => currentFile;
            set
            {
                if (SetProperty(ref currentFile, value))
                    OnPropertyChanged(nameof(CanCompareVersions));
            }
        }

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
                {
                    OnPropertyChanged(nameof(PreviewBackground));
                    _ = SetMainPageAsync();
                }
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
            set => SetProperty(ref fileWorkerBusy, value);
        }

        private bool diffBusy = false;
        public bool DiffBusy
        {
            get => diffBusy;
            set => SetProperty(ref diffBusy, value);
        }

        private string statusMessage;
        public string StatusMessage
        {
            get => statusMessage;
            set => SetProperty(ref statusMessage, value);
        }

        private int progress = 0;
        public int Progress
        {
            get => progress;
            set => SetProperty(ref progress, value);
        }

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
                if (DualFileMode && CurrentFile2 != null)
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
                await Task.Delay(50, token).ConfigureAwait(false);
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
                // Dispose old document quickly on UI thread (no polling)
                await DisposeCurrentDocumentAsync(token).ConfigureAwait(false);

                if (IsStale(myGeneration)) return;

                string path = RequestFile.Sökväg;

                if (FastOpenMode)
                {
                    // FAST OPEN: open document by filepath rather than reading whole file into memory.
                    // Opening by path lets the native renderer perform on-demand reads which
                    // works better for slow servers and avoids large managed allocations.
                    MuPDFContext previewContext = null!;
                    MuPDFDocument previewDoc = null!;

                    // Create MuPDF objects on the UI thread for safety (native interop sometimes
                    // requires UI-thread affinity). Use Dispatcher to keep this async-friendly.
                    var sw = Stopwatch.StartNew();
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        previewContext = new MuPDFContext();
                        // Use file-based constructor when available — this delegates IO to native layer
                        // and avoids buffering the entire file in managed memory.
                        previewDoc = new MuPDFDocument(previewContext, path);
                    }).GetTask().ConfigureAwait(false);

                    if (token.IsCancellationRequested || IsStale(myGeneration))
                    {
                        // Another call superseded us or cancellation requested — dispose what we just created
                        try { previewDoc?.Dispose(); } catch { }
                        try { previewContext?.Dispose(); } catch { }
                        return;
                    }

                    // Swap fields atomically: capture old refs first
                    var prevDoc = MainPreviewFile;
                    var prevCtx = context;

                    MainPreviewFile = previewDoc;
                    context = previewContext;
                    Pagecount = previewDoc.Pages.Count;
                    CurrentFile = RequestFile;

                    // Dispose old refs in correct order (document before context)
                    prevDoc?.Dispose();
                    prevCtx?.Dispose();

                    fileAvailable = true;

                    if (IsStale(myGeneration))
                    {
                        try { previewDoc?.Dispose(); } catch { }
                        try { previewContext?.Dispose(); } catch { }
                        return;
                    }

                    await FinalizeOpenAsync(search, token).ConfigureAwait(false);

                    sw.Stop();
                    swTotal.Stop();
                    StatusMessage = $"Opened (file) create {sw.ElapsedMilliseconds} ms, first-render {swTotal.ElapsedMilliseconds} ms";

                    // Fast-open path complete
                    return;
                }

                // Default: full open (read into memory and create from bytes)
                var sw2 = Stopwatch.StartNew();
                bytes = await Task.Run(() => ReadFileBytes(path, token)).ConfigureAwait(false);

                if (IsStale(myGeneration) || bytes == null) return;

                var localBytes = bytes;

                // Create PDF document on the UI thread. Native MuPDF objects have
                // UI-thread affinity in some builds — constructing them on a
                // background thread can cause use-after-free / access violations
                // when documents are disposed or rendered concurrently.
                MuPDFContext localContext = null!;
                MuPDFDocument doc = null!;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    localContext = new MuPDFContext();
                    doc = new MuPDFDocument(localContext, localBytes, InputFileTypes.PDF);
                }).GetTask().ConfigureAwait(false);

                if (IsStale(myGeneration))
                {
                    // Another call superseded us — dispose what we just created
                    doc?.Dispose();
                    localContext?.Dispose();
                    return;
                }

                // Swap fields atomically: capture old refs first
                var oldDoc = MainPreviewFile;
                var oldCtx = context;

                MainPreviewFile = doc;
                context = localContext;
                Pagecount = doc.Pages.Count;
                CurrentFile = RequestFile;

                // Dispose old refs in correct order (document before context)
                oldDoc?.Dispose();
                oldCtx?.Dispose();

                fileAvailable = true;

                if (IsStale(myGeneration))
                {
                    doc?.Dispose();
                    localContext?.Dispose();
                    return;
                }

                await FinalizeOpenAsync(search, token).ConfigureAwait(false);

                sw2.Stop();
                swTotal.Stop();
                StatusMessage = $"Opened (memory) create {sw2.ElapsedMilliseconds} ms, first-render {swTotal.ElapsedMilliseconds} ms";
            }
            catch (OperationCanceledException)
            {
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

        /// <summary>
        /// Shared post-open logic: sets the default page on the UI thread,
        /// kicks off search if requested, and awaits the first-page render.
        /// </summary>
        private async Task FinalizeOpenAsync(string? search, CancellationToken token)
        {
            int desired = Math.Clamp(RequestFile!.DefaultPage, 0,
                Math.Max(0, MainPreviewFile!.Pages.Count - 1));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!DualFileMode)
                    LinkedPageMode = true;

                // Single-page files cannot use two-page mode — revert to single.
                if (Pagecount <= 1 && twopageMode)
                    TwopageMode = false;

                requestPage1 = desired;
                OnPropertyChanged(nameof(RequestPage1));
                // Reset the backing field to a sentinel so that
                // RenderCurrentPageAsync's "CurrentPage1 = requestPage1"
                // fires PropertyChanged AFTER Initialize completes.
                // Setting CurrentPage1 here (before Initialize) would trigger
                // OnBindingPwr ? SetStrokePage ? InvalidateVisual on a
                // released renderer, causing a blank preview.
                currentPage1 = -1;
                Rotation = 0;
                if (!string.IsNullOrEmpty(search))
                    SearchMode = true;
            }).GetTask().ConfigureAwait(false);

            if (!string.IsNullOrEmpty(search))
                _ = SearchAsync(search, token);

            await RenderCurrentPageAsync().ConfigureAwait(false);
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
                bytes = null; // release memory early
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
                        secondaryFile?.Dispose();
                        secondaryContext?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error during secondary dispose");
                    }
                }).GetTask().ConfigureAwait(false);

                secondaryFile = null;
                secondaryContext = null;
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

        /// <summary>
        /// Reads file bytes synchronously (called via Task.Run) with
        /// cancellation support and progress reporting.
        /// Returns null if cancelled or on error — caller checks for null.
        /// </summary>
        private byte[]? ReadFileBytes(string path, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested) return null;

                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists) return null;

                long total = fileInfo.Length;
                StatusMessage = $"Reading: {total / 1_000_000.0:F1} MB";
                Progress = 0;

                // Small files (< 10 MB): read all at once — fastest path
                if (total < 10_000_000)
                {
                    if (token.IsCancellationRequested) return null;
                    byte[] result = File.ReadAllBytes(path);
                    Progress = 0;
                    return token.IsCancellationRequested ? null : result;
                }

                // Large files: buffered read with progress
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, BUFFER_SIZE, FileOptions.SequentialScan);
                using var ms = new MemoryStream((int)Math.Min(total, int.MaxValue));

                byte[] buffer = new byte[BUFFER_SIZE];
                int steps = Math.Max(1, (int)(total / buffer.Length));
                int leap = Math.Max(1, steps / PROGRESS_UPDATE_INTERVAL);
                int i = 0;
                int bytesRead;

                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (token.IsCancellationRequested) return null;

                    ms.Write(buffer, 0, bytesRead);

                    if (i % leap == 0)
                        Progress = Math.Min(100, 100 * (i + 1) / steps);
                    i++;
                }

                Progress = 0;
                return token.IsCancellationRequested ? null : ms.ToArray();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger?.LogError(ex, "Error reading {Path}", path);
                return null;
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

            try { await Task.Delay(50, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            bool IsStale2() => Volatile.Read(ref secondaryFileGeneration) != myGen;

            try
            {
                await DisposeSecondaryDocumentAsync(token).ConfigureAwait(false);

                if (IsStale2()) return;

                MuPDFContext newContext = null!;
                MuPDFDocument newDoc = null!;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    newContext = new MuPDFContext();
                    newDoc = new MuPDFDocument(newContext, file.Sökväg);
                }).GetTask().ConfigureAwait(false);

                if (IsStale2() || token.IsCancellationRequested)
                {
                    try { newDoc?.Dispose(); } catch { }
                    try { newContext?.Dispose(); } catch { }
                    return;
                }

                secondaryFile = newDoc;
                secondaryContext = newContext;
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
        private async Task RenderCurrentPageAsync()
        {
            if (disposed || !PageInRange(requestPage1) || mainRenderer == null || MainPreviewFile == null)
                return;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        if (mainRenderer == null || MainPreviewFile == null)
                            return;

                        mainRenderer.IsVisible = false;
                        mainRenderer.HighlightedRegions = null;
                        mainRenderer.ReleaseResources();
                        mainRenderer.Initialize(MainPreviewFile, 1, requestPage1, ZOOM_LEVEL);
                        mainRenderer.IsVisible = true;
                        SetSearchResults();
                        CurrentPage1 = requestPage1;
                    }
                    catch (Exception ex)
                    {
                        // Defensive: log the exception and abort this render attempt
                        logger?.LogError(ex, "Exception in RenderCurrentPageAsync UI invoke");
                        Finn.Utils.ErrorLogger.Log(ex, "RenderCurrentPageAsync.UI");
                        return;
                    }

                    if (!DualFileMode && LinkedPageMode && TwopageMode && PageInRange(requestPage1 + 1) && secondaryRenderer != null)
                    {
                        try
                        {
                            if (MainPreviewFile == null)
                                return;

                            requestPage2 = requestPage1 + 1;
                            OnPropertyChanged(nameof(RequestPage2));
                            secondaryRenderer.IsVisible = false;
                            secondaryRenderer.HighlightedRegions = null;
                            secondaryRenderer.ReleaseResources();
                            secondaryRenderer.Initialize(MainPreviewFile, 1, requestPage2, ZOOM_LEVEL);
                            secondaryRenderer.IsVisible = true;
                            SetSecondarySearchResults();
                            CurrentPage2 = requestPage2;
                        }
                        catch (NullReferenceException nre)
                        {
                            logger?.LogError(nre, "NullReference in RenderCurrentPageAsync secondary UI invoke");
                            Finn.Utils.ErrorLogger.Log(nre, "RenderCurrentPageAsync.UI.secondary");
                            return;
                        }
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in RenderCurrentPageAsync");
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

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

        public void ToggleVisibility(bool isVisible)
        {
            if (mainRenderer != null) mainRenderer.IsVisible = isVisible;
            if (TwopageMode && !DualFileMode && secondaryRenderer != null) secondaryRenderer.IsVisible = isVisible;
        }

        #endregion

        #region Page Rendering
        private async Task SetMainPageAsync()
        {
            if (disposed || FileWorkerBusy || SearchBusy || !PageInRange(RequestPage1) || mainRenderer == null)
                return;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    mainRenderer.IsVisible = false;
                    mainRenderer.HighlightedRegions = null;
                    try
                    {
                        if (MainPreviewFile != null && mainRenderer != null)
                        {
                            mainRenderer.ReleaseResources();
                            mainRenderer.Initialize(MainPreviewFile, 1, RequestPage1, ZOOM_LEVEL);
                            mainRenderer.IsVisible = true;
                            SetSearchResults();
                            CurrentPage1 = RequestPage1;
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

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    secondaryRenderer.IsVisible = false;
                    secondaryRenderer.HighlightedRegions = null;
                    try
                    {
                        var doc = DualFileMode ? secondaryFile : MainPreviewFile;
                        if (doc != null && secondaryRenderer != null)
                        {
                            secondaryRenderer.ReleaseResources();
                            // Guard: skip Initialize when the renderer has zero bounds
                            // (not yet in layout). ToggleDualViewAsync or Contain()
                            // will retry after layout completes.
                            if (secondaryRenderer.Bounds.Width > 0 && secondaryRenderer.Bounds.Height > 0)
                            {
                                secondaryRenderer.Initialize(doc, 1, RequestPage2, ZOOM_LEVEL);
                                if (!DualFileMode) SetSecondarySearchResults();
                                CurrentPage2 = RequestPage2;
                            }
                            secondaryRenderer.IsVisible = true;
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
                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    // -- Main page --
                                    if (mainInRange && mainRenderer != null && MainPreviewFile != null)
                                    {
                                        mainRenderer.IsVisible = false;
                                        mainRenderer.HighlightedRegions = null;
                                        try
                                        {
                                            mainRenderer.ReleaseResources();
                                            mainRenderer.Initialize(MainPreviewFile, 1, page1, ZOOM_LEVEL);
                                            mainRenderer.IsVisible = true;
                                            SetSearchResults();
                                            CurrentPage1 = page1;
                                        }
                                        catch (NullReferenceException nre)
                                        {
                                            logger?.LogError(nre, "NullReference in SetLinkedPagesAsync main");
                                            Finn.Utils.ErrorLogger.Log(nre, "SetLinkedPagesAsync.main");
                                        }
                                    }

                                    // -- Secondary page --
                                    if (secInRange && rendererActive && secondaryRenderer != null)
                                    {
                                        secondaryRenderer.IsVisible = false;
                                        secondaryRenderer.HighlightedRegions = null;
                                        try
                                        {
                                            var doc = DualFileMode ? secondaryFile : MainPreviewFile;
                                            if (doc != null)
                                            {
                                                secondaryRenderer.ReleaseResources();
                                                if (secondaryRenderer.Bounds.Width > 0 && secondaryRenderer.Bounds.Height > 0)
                                                {
                                                    secondaryRenderer.Initialize(doc, 1, page2, ZOOM_LEVEL);
                                                    if (!DualFileMode) SetSecondarySearchResults();
                                                    CurrentPage2 = page2;
                                                }
                                                secondaryRenderer.IsVisible = true;
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
                mainCts.Dispose();
                searchCts.Dispose();
                secondaryCts.Dispose();
                _diffCts?.Dispose();
                renderSemaphore.Dispose();
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