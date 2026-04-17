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
using Avalonia.Input.Platform;
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
        private volatile bool disposed = false;
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

        /// <summary>
        /// Starts a task without awaiting it. Exceptions are logged to
        /// <see cref="Utils.ErrorLogger"/> instead of being silently swallowed.
        /// </summary>
        private static async void FireAndForget(Task task, string caller = "")
        {
            try { await task.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Finn.Utils.ErrorLogger.Log(ex, $"FireAndForget({caller})"); }
        }

        #region PDF Document Properties
        private MuPDFDocument? mainPreviewFile = null;

        /// <summary>Visible in the toolbar when the current file is cached.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ShowForceServerRead => CurrentFile?.IsCached == true || RequestFile?.IsCached == true;

        /// <summary>Number of files currently in the local cache.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int CacheFileCount => _fileCache.CachedFileCount;

        /// <summary>
        /// When true, network files are automatically routed through the local
        /// cache before preview — even if not explicitly marked <c>IsCached</c>.
        /// Set by MainViewModel from <see cref="UISettingsViewModel.AutoCacheNetworkFiles"/>.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ReadBytesMode
        {
            get => _readBytesMode;
            set => SetProperty(ref _readBytesMode, value);
        }

        /// <summary>Total size in bytes of all cached files.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
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
            // Dispose in strict order: document first, then context.
            // If Dispose throws, suppress the finalizer so the GC doesn't
            // later attempt Finalize on a document whose context is gone
            // (LifetimeManagementException).
            try { oldDoc?.Dispose(); }
            catch { if (oldDoc != null) GC.SuppressFinalize(oldDoc); }
            try { oldCtx?.Dispose(); }
            catch { if (oldCtx != null) GC.SuppressFinalize(oldCtx); }
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
                    FireAndForget(SetLinkedPagesAsync(), nameof(SetLinkedPagesAsync));
                }
                else
                {
                    if (PageInRange(requestPage1))
                        FireAndForget(SetMainPageAsync(), nameof(SetMainPageAsync));
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
                    FireAndForget(SetSecondaryPageAsync(), nameof(SetSecondaryPageAsync));
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
        public bool ShowLinkedPageButton => twopageMode || (_diffOverlayActive && _diffViewMode != DiffViewMode.Overlay);

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
                SetProperty(ref twopageMode, value, () => FireAndForget(ToggleDualViewAsync(), nameof(ToggleDualViewAsync)));
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
                        FireAndForget(DisposeSecondaryDocumentAsync(), nameof(DisposeSecondaryDocumentAsync));
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

        private bool darkMode;
        public bool DarkMode
        {
            get => darkMode;
            set
            {
                if (SetProperty(ref darkMode, value))
                {
                    OnPropertyChanged(nameof(PreviewBackground));
                    OnPropertyChanged(nameof(LightPaperColor));
                }
            }
        }

        /// <summary>
        /// Available tint presets for dark mode.
        /// The tint colors the inverted paper via additive (Plus) + Multiply blending.
        /// </summary>
        public static IReadOnlyList<string> DarkModeTintOptions { get; } =
        [
            "None",
            // Cool
            "Blueprint",
            "Midnight",
            "Cobalt",
            "Steel",
            "Indigo",
            "Violet",
            "Lavender",
            "Cyan",
            "Teal",
            "Arctic",
            // Warm
            "Sepia",
            "Amber",
            "Mocha",
            "Copper",
            "Parchment",
            // Nature
            "Emerald",
            "Forest",
            "Sage",
            "Pine",
            // Red / Pink
            "Rose",
            "Burgundy",
            "Mauve",
            "Crimson",
            // Neutral
            "Slate",
            "Plum",
        ];

        private static Color TintColorForKey(string key) => key switch
        {
            // Cool
            "Blueprint"  => Color.FromRgb(8,  14, 28),
            "Midnight"   => Color.FromRgb(5,  8,  22),
            "Cobalt"     => Color.FromRgb(10, 16, 30),
            "Steel"      => Color.FromRgb(15, 18, 25),
            "Indigo"     => Color.FromRgb(12, 8,  30),
            "Violet"     => Color.FromRgb(18, 8,  28),
            "Lavender"   => Color.FromRgb(16, 12, 24),
            "Cyan"       => Color.FromRgb(8,  22, 28),
            "Teal"       => Color.FromRgb(8,  24, 22),
            "Arctic"     => Color.FromRgb(10, 20, 25),
            // Warm
            "Sepia"      => Color.FromRgb(22, 15, 8),
            "Amber"      => Color.FromRgb(25, 18, 6),
            "Mocha"      => Color.FromRgb(25, 18, 12),
            "Copper"     => Color.FromRgb(28, 14, 8),
            "Parchment"  => Color.FromRgb(28, 24, 16),
            // Nature
            "Emerald"    => Color.FromRgb(8,  25, 12),
            "Forest"     => Color.FromRgb(6,  22, 10),
            "Sage"       => Color.FromRgb(14, 22, 16),
            "Pine"       => Color.FromRgb(8,  28, 18),
            // Red / Pink
            "Rose"       => Color.FromRgb(25, 8,  14),
            "Burgundy"   => Color.FromRgb(28, 8,  12),
            "Mauve"      => Color.FromRgb(22, 12, 20),
            "Crimson"    => Color.FromRgb(28, 6,  10),
            // Neutral
            "Slate"      => Color.FromRgb(14, 16, 20),
            "Plum"       => Color.FromRgb(22, 10, 25),
            _            => Colors.Black, // no tint
        };

        private string darkModeTint = "None";
        /// <summary>
        /// The currently selected dark-mode tint preset key (e.g. "Blueprint").
        /// </summary>
        public string DarkModeTint
        {
            get => darkModeTint;
            set
            {
                if (SetProperty(ref darkModeTint, value ?? "None"))
                {
                    OnPropertyChanged(nameof(DarkModeTintColor));
                    OnPropertyChanged(nameof(PreviewBackground));
                }
            }
        }

        private int darkModeTintIntensity = 15;
        /// <summary>
        /// Strength of the Multiply pass that shifts colors toward the tint hue.
        /// 0 = no effect, 50 = maximum shift. Default 15.
        /// </summary>
        public int DarkModeTintIntensity
        {
            get => darkModeTintIntensity;
            set
            {
                if (SetProperty(ref darkModeTintIntensity, Math.Clamp(value, 0, 50)))
                {
                    OnPropertyChanged(nameof(PreviewBackground));
                }
            }
        }

        /// <summary>
        /// The resolved tint color for the current <see cref="DarkModeTint"/> key.
        /// Pushed to renderers via <c>SyncInversion</c>.
        /// </summary>
        public Color DarkModeTintColor => TintColorForKey(DarkModeTint);

        /// <summary>
        /// Available light-mode page background presets.
        /// These are subtle near-white colors that tint the PDF paper
        /// without a Skia overlay — set directly as the renderer's PageBackground.
        /// </summary>
        public static IReadOnlyList<string> LightPaperOptions { get; } =
        [
            "White",
            // Light
            "Eggshell",
            "Vanilla",
            "Ivory",
            "Cream",
            "Buttermilk",
            // Medium-light
            "Champagne",
            "Linen",
            "Bisque",
            "Parchment",
            "Bone",
            // Medium
            "Sand",
            "Wheat",
            "Honey",
            "Peach",
            "Apricot",
            // Medium-dark
            "Tan",
            "Khaki",
            "Amber",
            "Mocha",
            "Sepia",
        ];

        private static Color LightPaperColorForKey(string key) => key switch
        {
            // Light — barely tinted
            "Eggshell"   => Color.FromRgb(252, 249, 242),
            "Vanilla"    => Color.FromRgb(252, 248, 235),
            "Ivory"      => Color.FromRgb(255, 250, 230),
            "Cream"      => Color.FromRgb(255, 248, 225),
            "Buttermilk" => Color.FromRgb(255, 246, 218),
            // Medium-light — noticeable warmth
            "Champagne"  => Color.FromRgb(250, 240, 210),
            "Linen"      => Color.FromRgb(248, 235, 205),
            "Bisque"     => Color.FromRgb(250, 235, 200),
            "Parchment"  => Color.FromRgb(245, 232, 195),
            "Bone"       => Color.FromRgb(242, 230, 200),
            // Medium — clearly warm paper
            "Sand"       => Color.FromRgb(240, 225, 190),
            "Wheat"      => Color.FromRgb(238, 222, 182),
            "Honey"      => Color.FromRgb(242, 222, 175),
            "Peach"      => Color.FromRgb(245, 218, 185),
            "Apricot"    => Color.FromRgb(245, 215, 175),
            // Medium-dark — strong warm tint
            "Tan"        => Color.FromRgb(232, 210, 170),
            "Khaki"      => Color.FromRgb(228, 212, 172),
            "Amber"      => Color.FromRgb(235, 208, 158),
            "Mocha"      => Color.FromRgb(225, 205, 170),
            "Sepia"      => Color.FromRgb(220, 200, 162),
            _            => Colors.White,
        };

        private string lightPaper = "White";
        /// <summary>
        /// The currently selected light-mode paper preset key (e.g. "Cream").
        /// </summary>
        public string LightPaper
        {
            get => lightPaper;
            set
            {
                if (SetProperty(ref lightPaper, value ?? "White"))
                    OnPropertyChanged(nameof(LightPaperColor));
            }
        }

        private IBrush? _cachedPageBgBrush;
        private Color _cachedPageBgColor;
        private bool _cachedPageBgDarkMode;
        /// <summary>
        /// The resolved page background brush bound to both renderers' PageBackground.
        /// Returns white when dark mode is active so the inversion pipeline
        /// works on a clean base; applies the paper tint only in light mode.
        /// </summary>
        public IBrush LightPaperColor
        {
            get
            {
                var color = DarkMode ? Colors.White : LightPaperColorForKey(LightPaper);
                if (_cachedPageBgBrush == null || _cachedPageBgColor != color || _cachedPageBgDarkMode != DarkMode)
                {
                    _cachedPageBgColor = color;
                    _cachedPageBgDarkMode = DarkMode;
                    _cachedPageBgBrush = new SolidColorBrush(color).ToImmutable();
                }
                return _cachedPageBgBrush;
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

        private IBrush? _cachedUiBackground;
        private Color _cachedUiBgColor;
        public IBrush UiBackground
        {
            get
            {
                if (_cachedUiBackground == null || _cachedUiBgColor != ThemeRegionColor)
                {
                    _cachedUiBgColor = ThemeRegionColor;
                    _cachedUiBackground = new SolidColorBrush(ThemeRegionColor).ToImmutable();
                }
                return _cachedUiBackground;
            }
        }

        private IBrush? _cachedPreviewBg;
        private Color _cachedPreviewBgTheme;
        private Color _cachedPreviewBgTint;
        private int _cachedPreviewBgIntensity;
        private bool _cachedPreviewBgDark;
        /// <summary>
        /// Background for the preview area. When DarkMode is on, this returns
        /// a pre-computed color so that after the renderer's inline
        /// Difference + Plus + Multiply passes the visible result matches the
        /// original theme color.
        /// </summary>
        public IBrush PreviewBackground
        {
            get
            {
                if (!DarkMode)
                    return Brushes.Transparent;

                var tint = DarkModeTintColor;

                // Return cached brush if inputs haven't changed
                if (_cachedPreviewBg != null
                    && _cachedPreviewBgDark
                    && _cachedPreviewBgTheme == ThemeRegionColor
                    && _cachedPreviewBgTint == tint
                    && _cachedPreviewBgIntensity == darkModeTintIntensity)
                    return _cachedPreviewBg;

                Color result;
                bool hasTint = tint.R != 0 || tint.G != 0 || tint.B != 0;

                if (!hasTint)
                {
                    result = Color.FromRgb(
                        (byte)(255 - ThemeRegionColor.R),
                        (byte)(255 - ThemeRegionColor.G),
                        (byte)(255 - ThemeRegionColor.B));
                }
                else
                {
                    int maxT = Math.Max(tint.R, Math.Max(tint.G, tint.B));
                    int intensity = darkModeTintIntensity;

                    static byte Inv(byte theme, byte tintCh, int maxTint, int strength)
                    {
                        int mul = 255 - (maxTint - tintCh) * strength / maxTint;
                        if (mul <= 0) return 255;
                        int p = 255 + tintCh - (int)(theme * 255.0 / mul + 0.5);
                        return (byte)Math.Clamp(p, 0, 255);
                    }

                    result = Color.FromRgb(
                        Inv(ThemeRegionColor.R, tint.R, maxT, intensity),
                        Inv(ThemeRegionColor.G, tint.G, maxT, intensity),
                        Inv(ThemeRegionColor.B, tint.B, maxT, intensity));
                }

                _cachedPreviewBgDark = true;
                _cachedPreviewBgTheme = ThemeRegionColor;
                _cachedPreviewBgTint = tint;
                _cachedPreviewBgIntensity = darkModeTintIntensity;
                _cachedPreviewBg = new SolidColorBrush(result).ToImmutable();
                return _cachedPreviewBg;
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
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsProgressIndeterminate => fileWorkerBusy && progress == 0;

        #region Background Task Progress
        private bool _backgroundTaskActive;
        /// <summary>True when a background task (pre-caching, etc.) is running.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
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
        [System.Text.Json.Serialization.JsonIgnore]
        public string BackgroundTaskMessage
        {
            get => _backgroundTaskMessage;
            set => SetProperty(ref _backgroundTaskMessage, value);
        }

        private int _backgroundTaskProgress;
        /// <summary>Progress percentage (0–100) for the background task.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int BackgroundTaskProgress
        {
            get => _backgroundTaskProgress;
            set => SetProperty(ref _backgroundTaskProgress, value);
        }

        /// <summary>True when the active background task supports cancellation.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
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
                if (this.mainRenderer != null)
                    this.mainRenderer.ReleaseResources();
                if (this.secondaryRenderer != null && this.secondaryRenderer != secondaryRenderer)
                    this.secondaryRenderer.ReleaseResources();

                this.mainRenderer = mainRenderer;
                this.secondaryRenderer = secondaryRenderer;

                if (CurrentFile != null)
                    FireAndForget(SetMainPageAsync(), nameof(SetMainPageAsync));
                // Skip secondary init when diff mode is active — the View's
                // SyncDiffOverlay will set up the secondary renderer with the
                // correct layout (Toggle/SBS) after this method returns.
                if (!_diffOverlayActive && DualFileMode && CurrentFile2 != null)
                    FireAndForget(SetSecondaryPageAsync(), nameof(SetSecondaryPageAsync));
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

        /// <summary>
        /// Re-initializes the secondary renderer after an embedded/windowed swap
        /// while TwopageMode was already active. The caller must have already set
        /// IsVisible=true and deferred this call until after layout so that
        /// SetSecondaryPageAsync's bounds guard passes.
        /// </summary>
        public async Task ReinitSecondaryAfterSwapAsync()
        {
            try
            {
                await SetSecondaryPageAsync().ConfigureAwait(false);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (secondaryRenderer?.Bounds is { Width: > 0, Height: > 0 })
                        secondaryRenderer.Contain();
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error reinitializing secondary renderer after swap");
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

                try { prevDoc?.Dispose(); }
                catch { if (prevDoc != null) GC.SuppressFinalize(prevDoc); }
                try { prevCtx?.Dispose(); }
                catch { if (prevCtx != null) GC.SuppressFinalize(prevCtx); }

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
            // CloseDiffModeSync writes backing fields directly (bypassing the
            // DualFileMode property setter) so we must dispose the secondary
            // document explicitly — otherwise it's orphaned and the GC may
            // finalize its context first, causing LifetimeManagementException.
            if (_diffOverlayActive)
            {
                await Dispatcher.UIThread.InvokeAsync(CloseDiffModeSync).GetTask().ConfigureAwait(false);
                await DisposeSecondaryDocumentAsync().ConfigureAwait(false);
            }
            else if (dualFileMode && !preserveDualFile)
            {
                await Dispatcher.UIThread.InvokeAsync(CloseDiffModeSync).GetTask().ConfigureAwait(false);
                await DisposeSecondaryDocumentAsync().ConfigureAwait(false);
            }

            int myGeneration = Interlocked.Increment(ref fileGeneration);

            // Signal early that a file switch is in progress so page-change
            // methods (SetMainPageAsync etc.) bail out immediately instead of
            // competing for the render semaphore during the debounce window.
            FileWorkerBusy = true;

            // Cancel previous load — use synchronous Cancel() to avoid
            // blocking on callback completion (CancelAsync waits for all
            // registered callbacks which can be slow for cache/search I/O).
            try
            {
                mainCts.Cancel();
                mainCts.Dispose();
            }
            catch { }

            mainCts = new CancellationTokenSource();

            // Cancel any running search BEFORE the debounce so search batches
            // stop blocking the UI thread immediately. When a search is started
            // by the user (not by SetFileAsync), its token is only linked to
            // searchCts — mainCts.Cancel() above won't reach it. Without this,
            // search batches keep running on the UI thread for the entire
            // debounce window, freezing the UI.
            try
            {
                searchCts.Cancel();
                searchCts.Dispose();
            }
            catch { }
            searchCts = new CancellationTokenSource();
            SearchBusy = false;
            ClearSearch();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, mainCts.Token);
            var token = linkedCts.Token;

            // Debounce to coalesce rapid selection changes. This reduces
            // race conditions when users quickly toggle files and avoids rapidly
            // creating/disposing native MuPDF objects which can cause crashes.
            // 60ms is long enough to coalesce keyboard-repeat and rapid clicks
            // but short enough to feel responsive for intentional switches.
            try
            {
                await Task.Delay(60, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // A newer request or cancellation arrived — abort early.
                // Only clear FileWorkerBusy if no newer call will handle it.
                if (!IsStale(myGeneration))
                    FileWorkerBusy = false;
                return;
            }

            StatusMessage = "Setting File";
            fileAvailable = false;
            var swTotal = Stopwatch.StartNew();

            // Declared outside try so the finally block can dispose them
            // if an exception (e.g. OCE from semaphore wait) prevents the
            // atomic swap. Without this, leaked documents whose context was
            // disposed elsewhere crash in the GC finalizer with
            // MuPDFCore.LifetimeManagementException.
            MuPDFContext? previewContext = null;
            MuPDFDocument? previewDoc = null;

            try
            {
                if (IsStale(myGeneration)) return;

                string path = RequestFile.Sökväg;
                var reqFileRef = RequestFile;

                // Route through local cache when the file is explicitly cached
                // or when AutoCacheNetworkFiles is on and this is a network path.
                bool cacheHit = false;
                bool cachedLocally = false;
                bool wasStale = false;
                string originalPath = path;
                bool isNetworkPath = LocalFileCache.IsNetworkPath(path);
                // Always cache network files in ReadBytesMode — without this,
                // ReadAllBytesAsync reads the entire file over the network on
                // every switch. Multiple rapid switches pile up concurrent
                // multi-MB reads that saturate the network and thread pool.
                bool useCache = RequestFile.IsCached
                    || ((_autoCacheNetworkFiles || _readBytesMode) && isNetworkPath);

                if (useCache)
                {
                    StatusMessage = "Caching…";
                    var result = await _fileCache.GetLocalPathAsync(path, token, p => Progress = p).ConfigureAwait(false);
                    Progress = 0;
                    if (IsStale(myGeneration)) return;
                    cacheHit = result.WasCacheHit;
                    wasStale = result.WasStale;
                    cachedLocally = !string.Equals(result.Path, path, StringComparison.OrdinalIgnoreCase);
                    path = result.Path;
                }

                CacheSourceIcon = useCache
                    ? (wasStale ? "ArrowSync" : (cacheHit ? "Database" : "Cloud"))
                    : null;

                string openPath = path;

                // When AutoCacheNetworkFiles is on, mark the file so the
                // grid icon reflects cached state. ReadBytesMode is a
                // transparent optimization and should not permanently mark files.
                if (_autoCacheNetworkFiles && isNetworkPath && cachedLocally && !reqFileRef.IsCached)
                    reqFileRef.IsCached = true;

                // Create MuPDF objects on the background thread — document
                // construction is pure native file I/O with no UI dependency.
                // Only the renderer (Initialize) requires the UI thread.
                StatusMessage = "Opening…";

                // Final staleness check before the expensive, non-cancellable
                // native MuPDF call. Without this, if the user selects files
                // at intervals wider than the debounce (60ms), each selection
                // passes the debounce and starts a blocking native file read.
                // On slow servers this queues up many concurrent reads that
                // saturate the network and thread pool.
                if (IsStale(myGeneration))
                    return;

                var sw = Stopwatch.StartNew();
                if (_readBytesMode)
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(openPath, token).ConfigureAwait(false);
                    if (IsStale(myGeneration))
                        return;
                    (previewDoc, previewContext) = CreateMuPDFDocument(openPath, true, fileBytes);
                }
                else
                {
                    (previewDoc, previewContext) = CreateMuPDFDocument(openPath, false);
                }

                if (token.IsCancellationRequested || IsStale(myGeneration))
                {
                    previewDoc.Dispose();
                    previewContext.Dispose();
                    previewDoc = null;
                    previewContext = null;
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
                    // A newer SetFileAsync call may have arrived while we waited
                    // for the semaphore. Bail out before the expensive UI dispatch.
                    if (IsStale(myGeneration))
                    {
                        previewDoc.Dispose();
                        previewContext.Dispose();
                        previewDoc = null;
                        previewContext = null;
                        return;
                    }

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
                        // Ownership transferred — null the locals so the outer
                        // finally block won't double-dispose them.
                        previewDoc = null;
                        previewContext = null;
                        Pagecount = MainPreviewFile!.Pages.Count;
                        CurrentFile = reqFile;
                        fileAvailable = true;
                        try { prevDoc?.Dispose(); }
                        catch { if (prevDoc != null) GC.SuppressFinalize(prevDoc); }
                        try { prevCtx?.Dispose(); }
                        catch { if (prevCtx != null) GC.SuppressFinalize(prevCtx); }

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
                        // Final staleness check: skip the expensive Initialize
                        // if a newer request arrived while queued for the UI thread.
                        if (mainRenderer != null && !IsStale(myGeneration))
                        {
                            mainRenderer.ReleaseResources();
                            mainRenderer.Initialize(MainPreviewFile!, 1, desired, ZOOM_LEVEL);
                            mainRenderer.IsVisible = true;
                            SetSearchResults();
                            CurrentPage1 = desired;
                        }

                        // --- Secondary page (two-page linked mode) ---
                        if (!DualFileMode && LinkedPageMode && TwopageMode
                            && PageInRange(desired + 1) && secondaryRenderer != null
                            && !IsStale(myGeneration))
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
                    FireAndForget(SearchAsync(search, token), nameof(SearchAsync));

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
                // Safety net: dispose doc (before ctx!) if they were never
                // swapped into the ViewModel fields. This covers all exception
                // paths — e.g. OCE from renderSemaphore.WaitAsync, or any
                // unexpected throw between document creation and the swap.
                if (previewDoc != null)
                {
                    try { previewDoc.Dispose(); }
                    catch { GC.SuppressFinalize(previewDoc); }
                    try { previewContext?.Dispose(); }
                    catch { if (previewContext != null) GC.SuppressFinalize(previewContext); }
                }

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
        /// Safely creates a <see cref="MuPDFDocument"/> and its owning
        /// <see cref="MuPDFContext"/>. If the document constructor throws
        /// (file locked, corrupt, network error), the partially-constructed
        /// document's GC finalizer would crash with
        /// <c>LifetimeManagementException</c> if the context were disposed
        /// first. This helper keeps the context alive until the partial
        /// document has been finalized, preventing the crash.
        /// </summary>
        private static (MuPDFDocument doc, MuPDFContext ctx) CreateMuPDFDocument(
            string path, bool readBytesMode, byte[]? fileBytes = null)
        {
            var ctx = new MuPDFContext();
            try
            {
                var doc = readBytesMode && fileBytes != null
                    ? new MuPDFDocument(ctx, fileBytes, InputFileTypes.PDF)
                    : new MuPDFDocument(ctx, path);
                return (doc, ctx);
            }
            catch
            {
                // A partially-constructed MuPDFDocument is on the heap with
                // OwnerContext = ctx. Its GC finalizer checks
                // OwnerContext.disposedValue and crashes with
                // LifetimeManagementException if the context was disposed first.
                //
                // We cannot dispose or finalize the context safely because:
                // - Disposing it guarantees the crash (disposedValue = true).
                // - GC.Collect+WaitForPendingFinalizers is unreliable in debug
                //   mode and causes multi-hundred-ms freezes on large heaps.
                //
                // Instead, suppress the context's finalizer so its
                // disposedValue stays false permanently. When the GC later
                // finalizes the partial document, the lifetime check passes
                // and the native DisposeDocument(ctx, NativeDocument=0) call
                // is a safe no-op. The ~100 bytes of native context memory
                // leak on this exceptional error path.
                GC.SuppressFinalize(ctx);
                throw;
            }
        }

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
                searchCts.Cancel();
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

                        // Null the fields BEFORE disposing so the GC finalizer
                        // cannot race against explicit disposal. Capture the old
                        // references locally for strict document-before-context disposal.
                        var prevDoc = MainPreviewFile;
                        var prevCtx = context;
                        MainPreviewFile = null;
                        context = null;
                        fileAvailable = false;

                        try { prevDoc?.Dispose(); }
                        catch { if (prevDoc != null) GC.SuppressFinalize(prevDoc); }
                        try { prevCtx?.Dispose(); }
                        catch { if (prevCtx != null) GC.SuppressFinalize(prevCtx); }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error during quick dispose");
                    }
                }).GetTask().ConfigureAwait(false);

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
                secondaryCts.Cancel();
                secondaryCts.Dispose();
            }
            catch { }
            secondaryCts = new CancellationTokenSource();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, secondaryCts.Token);
            var token = linkedCts.Token;

            try { await Task.Delay(25, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }

            bool IsStale2() => Volatile.Read(ref secondaryFileGeneration) != myGen;

            MuPDFContext? newContext = null;
            MuPDFDocument? newDoc = null;

            try
            {
                await DisposeSecondaryDocumentAsync(token).ConfigureAwait(false);

                if (IsStale2()) return;

                // Resolve through local cache when the file is marked for caching
                // or auto-cache is enabled for network paths.
                string filePath = file.Sökväg;
                bool secondaryCachedLocally = false;
                bool useCache2 = file.IsCached
                    || ((_autoCacheNetworkFiles || _readBytesMode) && LocalFileCache.IsNetworkPath(filePath));

                if (useCache2)
                {
                    var result = await _fileCache.GetLocalPathAsync(filePath, token).ConfigureAwait(false);
                    secondaryCachedLocally = !string.Equals(result.Path, filePath, StringComparison.OrdinalIgnoreCase);
                    filePath = result.Path;
                }

                if (IsStale2()) return;

                // Create MuPDF objects on the background thread (no UI dependency).
                if (_readBytesMode)
                {
                    byte[] fileBytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
                    if (IsStale2()) return;
                    (newDoc, newContext) = CreateMuPDFDocument(filePath, true, fileBytes);
                }
                else
                {
                    (newDoc, newContext) = CreateMuPDFDocument(filePath, false);
                }

                if (IsStale2() || token.IsCancellationRequested)
                {
                    newDoc.Dispose();
                    newContext.Dispose();
                    newDoc = null;
                    newContext = null;
                    return;
                }

                // Pin the cached path so LRU eviction won't delete it while
                // MuPDF holds a native file handle.
                if (secondaryCachedLocally)
                    _fileCache.Pin(filePath);

                UnpinSecondaryCachePath();
                _secondaryPinnedCachePath = secondaryCachedLocally ? filePath : null;

                // Swap on the UI thread so the renderer cannot access the
                // old native document while we dispose it. Keep newDoc/newContext
                // non-null until the swap succeeds so the finally block can
                // clean up if InvokeAsync throws before the swap runs.
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SwapSecondaryDocument(newDoc, newContext);
                    Pagecount2 = secondaryFile!.Pages.Count;
                    CurrentFile2 = file;

                    DualFileMode = true; // also resets TwopageMode and defaults LinkedPageMode = false
                    requestPage2 = linkedPageMode ? requestPage1 : 0;
                    OnPropertyChanged(nameof(RequestPage2));
                    CurrentPage2 = requestPage2;
                    FireAndForget(SetSecondaryPageAsync(), nameof(SetSecondaryPageAsync));
                    if (secondaryRenderer?.Bounds is { Width: > 0, Height: > 0 })
                        secondaryRenderer.Contain();
                }).GetTask().ConfigureAwait(false);
                newDoc = null;     // ownership transferred — prevent finally from double-disposing
                newContext = null;
            }
            catch (OperationCanceledException)
            {
                logger?.LogInformation("Secondary file load cancelled (gen {Generation})", myGen);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in SetFile2Async");
            }
            finally
            {
                // Dispose doc before ctx if they were never swapped in.
                if (newDoc != null)
                {
                    try { newDoc.Dispose(); }
                    catch { GC.SuppressFinalize(newDoc); }
                    try { newContext?.Dispose(); }
                    catch { if (newContext != null) GC.SuppressFinalize(newContext); }
                }
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
            if (DualFileMode)
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
            else if (!TwopageMode) { if (RequestPage1 < lastPage) RequestPage1++; }
            else if (LinkedPageMode) { if (RequestPage1 + 2 <= lastPage) RequestPage1 += 2; }
            else if (!secondPage) { if (RequestPage1 < lastPage) RequestPage1++; }
            else { if (RequestPage2 < lastPage) RequestPage2++; }
        }

        public void PrevPage(bool secondPage = false)
        {
            if (DualFileMode)
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
            else if (!TwopageMode) { if (RequestPage1 > 0) RequestPage1--; }
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
                        FireAndForget(SetSecondaryPageAsync(), nameof(SetSecondaryPageAsync));
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
                if (!PageInRange(targetPage) || FileWorkerBusy) return;

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
                            // Exception: when search results arrived after the
                            // initial render, force re-Initialize so highlights
                            // are composited into the render pass — Search()
                            // alone doesn't repaint an already-rendered page.
                            bool needsSearchHighlight = SearchPages?.Count > 0
                                && SearchPages.Contains(targetPage) && regex != null;

                            if (CurrentPage1 == targetPage && mainRenderer.IsViewerInitialized
                                && !needsSearchHighlight)
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
                if (!inRange || FileWorkerBusy) return;

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
                                if ((!mainInRange && !secInRange) || FileWorkerBusy) return;

                                await Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    // -- Main page --
                                    if (mainInRange && mainRenderer != null && MainPreviewFile != null)
                                    {
                                        // Skip redundant re-render when already showing this page.
                                        if (!(CurrentPage1 == page1 && mainRenderer.IsViewerInitialized))
                                        {
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
                                    }

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
        public async void CopyToClipboard(Avalonia.Visual window)
        {
            if (CurrentFile != null && mainRenderer != null)
            {
                string selectedText = mainRenderer.GetSelectedText();
                if (!string.IsNullOrEmpty(selectedText))
                {
                    try
                    {
                        var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
                        if (clipboard != null)
                            await clipboard.SetTextAsync(selectedText);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Clipboard copy failed");
                    }
                }
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

                mainCts.Cancel();
                searchCts.Cancel();
                secondaryCts.Cancel();
                _diffCts?.Cancel();
                _backgroundTaskCts?.Cancel();
                _toleranceDebounceCts?.Cancel();
                mainCts.Dispose();
                searchCts.Dispose();
                secondaryCts.Dispose();
                _diffCts?.Dispose();
                _backgroundTaskCts?.Dispose();
                _toleranceDebounceCts?.Dispose();
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
