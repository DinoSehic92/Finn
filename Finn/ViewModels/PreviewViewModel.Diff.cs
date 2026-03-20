using Finn.Controls;
using Finn.Model;
using Finn.Services;
using Avalonia;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using MuPDFCore;
using MuPDFCore.MuPDFRenderer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.ViewModels
{
    public partial class PreviewViewModel
    {
        #region Diff State

        /// <summary>Diff results from the last comparison, keyed by page index.</summary>
        private List<DiffResultData>? _diffResults;
        /// <summary>Temp directory holding diff images.</summary>
        private string? _diffTempDir;
        /// <summary>PDF path of the original (A) file for side-by-side mode.</summary>
        private string? _diffOriginalPdfPath;
        /// <summary>PDF path of the revised (B) file, kept for re-running with new tolerance.</summary>
        private string? _diffRevisedPdfPath;
        /// <summary>
        /// The real FileData that owns version data. Stored separately because
        /// <see cref="CurrentFile"/> may be a stub created by PreviewVersionAsync
        /// that has no version information.
        /// </summary>
        private FileData? _diffSourceFile;
        /// <summary>Debounce timer for auto-recomputing diffs when tolerance changes.</summary>
        private CancellationTokenSource? _toleranceDebounceCts;
        /// <summary>Highlight color for diff pixels and annotation shapes.</summary>
        private Avalonia.Media.Color _diffHighlightColor = Avalonia.Media.Color.FromRgb(230, 60, 60);
        /// <summary>Auto-created diff annotation layer, tracked so it can be removed on mode switch.</summary>
        private AnnotationLayer? _diffAnnotationLayer;

        /// <summary>Sets the source file for version lookups during diff comparisons.</summary>
        public FileData? DiffSourceFile
        {
            get => _diffSourceFile;
            set
            {
                _diffSourceFile = value;
                OnPropertyChanged(nameof(CanCompareVersions));
            }
        }
        private bool _diffOverlayActive;
        private DiffViewMode _diffViewMode = DiffViewMode.Overlay;
        private int _diffTolerance = PdfDiffService.DefaultTolerance;
        private bool _diffRerunBusy;

        // ── A/B Toggle (renderer visibility swap) ──────────────────────
        private bool _diffShowingOriginal;

        #endregion

        #region Diff Properties

        /// <summary>Whether the diff overlay is currently showing.</summary>
        public bool DiffOverlayActive
        {
            get => _diffOverlayActive;
            set
            {
                if (SetProperty(ref _diffOverlayActive, value))
                {
                    OnPropertyChanged(nameof(ShowDiffToggle));
                    OnPropertyChanged(nameof(IsUserDualFileMode));
                    OnPropertyChanged(nameof(CanToggleLayout));
                    OnPropertyChanged(nameof(CanSearch));
                    // Auto-close text search when entering diff mode, but keep diff page list
                    if (value && searchMode && !_diffPageListMode) SearchMode = false;
                }
            }
        }

        /// <summary>Whether diff results are loaded (controls toggle button visibility).</summary>
        public bool HasDiffResults => _diffResults != null && _diffResults.Count > 0;

        /// <summary>Active diff view mode: Overlay, Toggle (A/B swap), or SideBySide.</summary>
        public DiffViewMode DiffViewMode
        {
            get => _diffViewMode;
            set
            {
                if (SetProperty(ref _diffViewMode, value))
                {
                    OnPropertyChanged(nameof(ShowDiffToggle));
                    OnPropertyChanged(nameof(IsOverlayMode));
                    OnPropertyChanged(nameof(IsToggleMode));
                    OnPropertyChanged(nameof(IsSideBySideMode));
                }
            }
        }

        // Bool properties for radio-style toggle buttons in the diff toolbar.
        // Bindings are OneWay — the View drives mode changes via Click handlers.
        public bool IsOverlayMode => _diffViewMode == DiffViewMode.Overlay;
        public bool IsToggleMode => _diffViewMode == DiffViewMode.Toggle;
        public bool IsSideBySideMode => _diffViewMode == DiffViewMode.SideBySide;

        /// <summary>Whether the A/B toggle button should be visible.</summary>
        public bool ShowDiffToggle => _diffOverlayActive && _diffViewMode == DiffViewMode.Toggle;

        /// <summary>Pixel-difference tolerance (0–1000). Higher = ignore smaller differences.</summary>
        public int DiffTolerance
        {
            get => _diffTolerance;
            set
            {
                if (SetProperty(ref _diffTolerance, Math.Clamp(value, 0, PdfDiffService.MaxTolerance)))
                    DebouncedRecomputeAsync();
            }
        }

        /// <summary>True while a tolerance re-run is in progress.</summary>
        public bool DiffRerunBusy
        {
            get => _diffRerunBusy;
            set => SetProperty(ref _diffRerunBusy, value);
        }

        /// <summary>Whether re-running the diff is possible (results loaded, paths known).</summary>
        public bool CanRerunDiff => _diffResults is { Count: > 0 } && !_diffRerunBusy;

        /// <summary>Predefined highlight colors the user can pick from.</summary>
        public static IReadOnlyList<Avalonia.Media.Color> DiffColorPresets { get; } =
        [
            Avalonia.Media.Color.FromRgb(230, 60, 60),   // Red
            Avalonia.Media.Color.FromRgb(59, 130, 217),  // Blue
            Avalonia.Media.Color.FromRgb(61, 163, 95),   // Green
            Avalonia.Media.Color.FromRgb(229, 168, 32),  // Orange
            Avalonia.Media.Color.FromRgb(155, 95, 192),  // Purple
        ];

        /// <summary>Highlight color used for diff pixel images and annotation layer shapes.</summary>
        public Avalonia.Media.Color DiffHighlightColor
        {
            get => _diffHighlightColor;
            set
            {
                if (SetProperty(ref _diffHighlightColor, value))
                {
                    if (_diffResults is { Count: > 0 })
                        DebouncedRecomputeAsync();
                }
            }
        }

        /// <summary>Whether we are currently showing the original (A) document in A/B toggle mode.</summary>
        public bool DiffShowingOriginal
        {
            get => _diffShowingOriginal;
            set => SetProperty(ref _diffShowingOriginal, value);
        }

        /// <summary>PDF path of the original (A) file for side-by-side mode.</summary>
        public string? DiffOriginalPdfPath => _diffOriginalPdfPath;

        /// <summary>Diff page count summary for UI display.</summary>
        public string DiffSummary
        {
            get
            {
                if (_diffResults == null || _diffResults.Count == 0)
                {
                    // In dual view without pixel results yet
                    if (_diffOverlayActive && _diffOriginalPdfPath != null)
                        return "Viewing A / B";
                    return "";
                }
                int changed = _diffResults.Count(r => r.HasDifferences);
                return $"{changed}/{_diffResults.Count} pages differ";
            }
        }

        public List<DiffPathChoice> DiffPathChoices { get; private set; } = [];
        public bool HasDiffPathChoices => DiffPathChoices.Count >= 2;

        public bool CanCompareVersions
        {
            get
            {
                var file = _diffSourceFile ?? currentFile;
                if (file is { HasVersions: true }) return true;
                if (file is { HasChildren: true }) return true;
                if (file is { IsAppendedFile: true }) return true;
                return !string.IsNullOrEmpty(_diffOriginalPdfPath) && !string.IsNullOrEmpty(_diffRevisedPdfPath);
            }
        }

        private DiffPathChoice? _diffChoiceA;
        public DiffPathChoice? DiffChoiceA
        {
            get => _diffChoiceA;
            set => SetProperty(ref _diffChoiceA, value);
        }

        private DiffPathChoice? _diffChoiceB;
        public DiffPathChoice? DiffChoiceB
        {
            get => _diffChoiceB;
            set => SetProperty(ref _diffChoiceB, value);
        }

        #endregion

        #region Diff Operations

        public async Task RerunDiffWithToleranceAsync()
        {
            if (_diffResults == null || _diffResults.Count == 0) return;
            DiffRerunBusy = true;
            OnPropertyChanged(nameof(CanRerunDiff));
            StatusMessage = "Recomputing diff…";
            try
            {
                await PdfDiffService.RecomputeDiffsAsync(_diffResults, _diffTolerance,
                    _diffHighlightColor.R, _diffHighlightColor.G, _diffHighlightColor.B);
                int diffCount = _diffResults.Count(r => r.HasDifferences);
                StatusMessage = diffCount == 0
                    ? $"{_diffResults.Count} pages — identical"
                    : $"{_diffResults.Count} pages — {diffCount} with differences";
                OnPropertyChanged(nameof(DiffSummary));
                PopulateDiffPageList();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Diff recompute failed: {ex.Message}";
            }
            finally
            {
                DiffRerunBusy = false;
                OnPropertyChanged(nameof(CanRerunDiff));
            }
        }

        /// <summary>
        /// Debounces tolerance changes: waits 500 ms after the last change before
        /// triggering a recompute. Cancels any previous pending recompute.
        /// </summary>
        private async void DebouncedRecomputeAsync()
        {
            if (_diffResults == null || _diffResults.Count == 0) return;

            _toleranceDebounceCts?.Cancel();
            _toleranceDebounceCts?.Dispose();
            _toleranceDebounceCts = new CancellationTokenSource();
            var ct = _toleranceDebounceCts.Token;

            try
            {
                await Task.Delay(500, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;
                await RerunDiffWithToleranceAsync().ConfigureAwait(false);
                // Notify the view to refresh the overlay for the current page.
                await Dispatcher.UIThread.InvokeAsync(() => OnPropertyChanged(nameof(DiffTolerance)))
                    .GetTask().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        public void LoadDiffResults(List<DiffResultData> results, string tempDir, string? originalPdfPath = null, string? revisedPdfPath = null, FileData? sourceFile = null)
        {
            CleanupDiffTempDir();
            _diffResults = results;
            _diffTempDir = tempDir;
            _diffOriginalPdfPath = originalPdfPath;
            _diffRevisedPdfPath = revisedPdfPath;
            _diffSourceFile = sourceFile;
            RefreshDiffPathChoices();
            DiffOverlayActive = results.Count > 0;
            OnPropertyChanged(nameof(HasDiffResults));
            OnPropertyChanged(nameof(DiffSummary));
            OnPropertyChanged(nameof(CanRerunDiff));
            OnPropertyChanged(nameof(CanCompareVersions));
            PopulateDiffPageList();
        }

        /// <summary>
        /// Populates the search panel with pages that have differences,
        /// allowing the user to navigate between them with prev/next.
        /// Opens the search panel in "diff results" mode (bypasses CanSearch guard).
        /// </summary>
        public void PopulateDiffPageList()
        {
            ClearSearch();
            DiffPageListMode = false;

            if (_diffResults == null || _diffResults.Count == 0) return;

            foreach (var result in _diffResults)
            {
                if (!result.HasDifferences) continue;
                SearchPages.Add(result.PageIndex);
                SearchPagesText.Add($"{result.Label} — differs");
            }

            SearchItems = SearchPages.Count;
            if (SearchItems > 0)
            {
                DiffPageListMode = true;
                SearchPageIndex = 0;
                // Open the panel — bypass the CanSearch guard since this is diff navigation
                SetProperty(ref searchMode, true, nameof(SearchMode));
            }
        }

        public void ClearDiffResults()
        {
            _toleranceDebounceCts?.Cancel();
            _toleranceDebounceCts?.Dispose();
            _toleranceDebounceCts = null;
            RemoveDiffAnnotationLayer();
            CleanupDiffTempDir();
            _diffResults = null;
            _diffTempDir = null;
            _diffOriginalPdfPath = null;
            _diffRevisedPdfPath = null;
            _diffSourceFile = null;
            _diffChoiceA = null;
            _diffChoiceB = null;
            DiffOverlayActive = false;
            // Close the diff page list if it was open
            if (searchMode)
            {
                ClearSearch();
                DiffPageListMode = false;
                SetProperty(ref searchMode, false, nameof(SearchMode));
            }
            OnPropertyChanged(nameof(HasDiffResults));
            OnPropertyChanged(nameof(CanRerunDiff));
            OnPropertyChanged(nameof(DiffSummary));
            OnPropertyChanged(nameof(DiffChoiceA));
            OnPropertyChanged(nameof(DiffChoiceB));
            DiffPathChoices = [];
            OnPropertyChanged(nameof(DiffPathChoices));
            OnPropertyChanged(nameof(HasDiffPathChoices));
        }

        public void CloseDiffModeSync()
        {
            if (!_diffOverlayActive && !dualFileMode) return;

            _diffShowingOriginal = false;

            secondaryRenderer?.ReleaseResources();
            if (secondaryRenderer != null)
                secondaryRenderer.IsVisible = false;

            twopageMode = false;
            dualFileMode = false;
            NotifyModeChanged();

            ClearDiffResults();
        }

        public AnnotationLayer? CreateDiffAnnotationLayer(string? comparedFileName = null)
        {
            if (_diffResults == null || _diffResults.Count == 0 || CurrentFile == null) return null;

            // Remove the previous auto-created diff layer to avoid stacking.
            RemoveDiffAnnotationLayer();

            string layerName = string.IsNullOrEmpty(comparedFileName)
                ? $"Diff {DateTime.Now:yyyy-MM-dd HH:mm}"
                : $"Diff vs {Path.GetFileNameWithoutExtension(comparedFileName)}";

            var layer = new AnnotationLayer
            {
                Name = layerName,
                Color = _diffHighlightColor
            };

            foreach (var result in _diffResults)
            {
                if (!result.HasDifferences) continue;

                // Prefer pre-computed regions; fall back to re-reading the diff image.
                var regions = result.Regions
                    ?? (result.DiffPath != null ? PdfDiffService.ExtractDiffRegions(result.DiffPath, PdfDiffService.ZOOM) : null);
                if (regions == null || regions.Count == 0) continue;

                var shapes = new List<ShapeAnnotation>();
                foreach (var r in regions)
                {
                    shapes.Add(new ShapeAnnotation
                    {
                        ShapeType = InlineAnnotationTool.Rectangle,
                        Start = new Avalonia.Point(r.X, r.Y),
                        End = new Avalonia.Point(r.X + r.Width, r.Y + r.Height),
                        Color = _diffHighlightColor,
                        StrokeWidth = 1.5,
                        Opacity = 0.35,
                        IsFilled = true
                    });
                }
                layer.PageShapes[result.PageIndex] = shapes;
            }

            layer.RecalculateCounts();
            CurrentFile.AnnotationLayers.Add(layer);
            _diffAnnotationLayer = layer;
            OnPropertyChanged("LayersChanged");
            return layer;
        }

        /// <summary>
        /// Removes the auto-created diff annotation layer from the current file.
        /// Called when switching diff modes or clearing diff results.
        /// </summary>
        public void RemoveDiffAnnotationLayer()
        {
            if (_diffAnnotationLayer != null && CurrentFile?.AnnotationLayers != null)
            {
                CurrentFile.AnnotationLayers.Remove(_diffAnnotationLayer);
                _diffAnnotationLayer = null;
                OnPropertyChanged("LayersChanged");
            }
        }

        private void CleanupDiffTempDir()
        {
            if (!string.IsNullOrEmpty(_diffTempDir) && Directory.Exists(_diffTempDir))
            {
                try { Directory.Delete(_diffTempDir, true); }
                catch { /* best effort */ }
            }
        }

        public string? GetDiffImagePath(int page)
        {
            var result = GetDiffResult(page);
            return result?.HasDifferences == true ? result.DiffPath : null;
        }

        public string? GetOriginalImagePath(int page)
            => GetDiffResult(page)?.OriginalPath;

        public string? GetRevisedImagePath(int page)
            => GetDiffResult(page)?.RevisedPath;

        public DiffResultData? GetDiffResult(int page)
        {
            if (_diffResults == null || page < 0) return null;
            if (page < _diffResults.Count && _diffResults[page].PageIndex == page)
                return _diffResults[page];
            return _diffResults.FirstOrDefault(r => r.PageIndex == page);
        }

        public List<DiffPathChoice> GetVersionChoicesForDialog(FileData? realFile = null, IEnumerable<FileData>? appendedFiles = null)
        {
            var choices = new List<DiffPathChoice>();
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var file = realFile ?? _diffSourceFile ?? currentFile;
            if (file != null)
            {
                string? origPath = file.HasVersions && !string.IsNullOrEmpty(file.OriginalPath)
                    ? file.OriginalPath
                    : file.Sökväg;
                if (!string.IsNullOrEmpty(origPath)
                    && origPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                    && addedPaths.Add(origPath))
                    choices.Add(new DiffPathChoice("Original", origPath));

                if (file.HasVersions)
                {
                    foreach (var v in file.Versions)
                    {
                        if (!string.IsNullOrEmpty(v.Sökväg)
                            && v.Sökväg.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                            && addedPaths.Add(v.Sökväg))
                            choices.Add(new DiffPathChoice(
                                string.IsNullOrEmpty(v.Label) ? Path.GetFileNameWithoutExtension(v.Sökväg) : v.Label,
                                v.Sökväg));
                    }
                }
            }

            if (appendedFiles != null)
            {
                foreach (var appended in appendedFiles)
                {
                    if (!string.IsNullOrEmpty(appended.Sökväg)
                        && appended.Sökväg.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                        && addedPaths.Add(appended.Sökväg))
                        choices.Add(new DiffPathChoice(
                            string.IsNullOrEmpty(appended.Namn) ? Path.GetFileNameWithoutExtension(appended.Sökväg) : appended.Namn,
                            appended.Sökväg));
                }
            }

            if (choices.Count < 2 && !string.IsNullOrEmpty(_diffOriginalPdfPath))
            {
                if (addedPaths.Add(_diffOriginalPdfPath))
                    choices.Insert(0, new DiffPathChoice(Path.GetFileNameWithoutExtension(_diffOriginalPdfPath), _diffOriginalPdfPath));
                if (!string.IsNullOrEmpty(_diffRevisedPdfPath) && addedPaths.Add(_diffRevisedPdfPath))
                    choices.Add(new DiffPathChoice(Path.GetFileNameWithoutExtension(_diffRevisedPdfPath), _diffRevisedPdfPath));
            }
            return choices;
        }

        public void RefreshDiffPathChoices()
        {
            var choices = GetVersionChoicesForDialog();
            DiffPathChoices = choices;

            _diffChoiceA = choices.FirstOrDefault(c =>
                string.Equals(c.Path, _diffOriginalPdfPath, StringComparison.OrdinalIgnoreCase))
                ?? choices.FirstOrDefault();
            _diffChoiceB = choices.FirstOrDefault(c =>
                string.Equals(c.Path, _diffRevisedPdfPath, StringComparison.OrdinalIgnoreCase));

            OnPropertyChanged(nameof(DiffPathChoices));
            OnPropertyChanged(nameof(HasDiffPathChoices));
            OnPropertyChanged(nameof(DiffChoiceA));
            OnPropertyChanged(nameof(DiffChoiceB));
        }

        public async Task CompareSelectedPathsAsync()
        {
            if (_diffChoiceA == null || _diffChoiceB == null) return;
            if (string.Equals(_diffChoiceA.Path, _diffChoiceB.Path, StringComparison.OrdinalIgnoreCase)) return;
            await RunDiffAsync(_diffChoiceA.Path, _diffChoiceB.Path, _diffChoiceA.Path, _diffSourceFile);
        }

        /// <summary>
        /// Enters diff dual-view mode immediately (Toggle or SideBySide) without
        /// running the slow pixel comparison. Sets the A/B paths so the secondary
        /// renderer can load the correct document. The user can run the pixel
        /// comparison later via the toolbar button.
        /// </summary>
        public void EnterDiffView(string pathA, string pathB, FileData? sourceFile = null)
        {
            _diffOriginalPdfPath = pathA;
            _diffRevisedPdfPath = pathB;
            _diffSourceFile = sourceFile;
            DiffOverlayActive = true;
            OnPropertyChanged(nameof(HasDiffResults));
            OnPropertyChanged(nameof(DiffSummary));
            OnPropertyChanged(nameof(CanRerunDiff));
            OnPropertyChanged(nameof(CanCompareVersions));
            OnPropertyChanged(nameof(DiffOriginalPdfPath));
        }

        private string? GetSecondaryDiffPath()
        {
            // The main renderer always shows A (original), secondary shows B (revised).
            return _diffRevisedPdfPath;
        }

        public void CancelDiff() => _diffCts?.Cancel();

        public async Task RunDiffAsync(string pathA, string pathB, string? originalPdfPath = null, FileData? sourceFile = null)
        {
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)) return;

            _diffCts?.Cancel();
            _diffCts?.Dispose();
            _diffCts = new CancellationTokenSource();
            var ct = _diffCts.Token;

            FileWorkerBusy = true;
            DiffBusy = true;
            StatusMessage = "Comparing…";
            try
            {
                var progress = new Progress<int>(p => StatusMessage = $"Comparing… {p}%");
                var (results, dir) = await PdfDiffService.CompareAsync(pathA, pathB, progress, ct, _diffTolerance,
                    _diffHighlightColor.R, _diffHighlightColor.G, _diffHighlightColor.B);
                LoadDiffResults(results, dir, originalPdfPath ?? pathA, pathB, sourceFile);
                int diffCount = results.Count(r => r.HasDifferences);
                StatusMessage = diffCount == 0
                    ? $"{results.Count} pages — identical"
                    : $"{results.Count} pages — {diffCount} with differences";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Comparison cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Diff failed: {ex.Message}";
            }
            finally
            {
                DiffBusy = false;
                FileWorkerBusy = false;
            }
        }

        /// <summary>
        /// Runs a word-level text diff between two PDFs. Produces
        /// <see cref="DiffResultData"/> entries with <see cref="DiffResultData.Regions"/>
        /// but no pixel images. The user can save the results as an annotation layer or
        /// navigate changed pages via the diff page list.
        /// </summary>
        public async Task RunTextDiffAsync(string pathA, string pathB, FileData? sourceFile = null)
        {
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)) return;

            _diffCts?.Cancel();
            _diffCts?.Dispose();
            _diffCts = new CancellationTokenSource();
            var ct = _diffCts.Token;

            FileWorkerBusy = true;
            DiffBusy = true;
            StatusMessage = "Text comparing…";
            try
            {
                var progress = new Progress<int>(p => StatusMessage = $"Text comparing… {p}%");
                var results = await TextDiffService.CompareAsync(pathA, pathB, progress, ct);
                // Text diff produces no images; create an empty temp dir for LoadDiffResults.
                string tempDir = Path.Combine(Path.GetTempPath(), "FinnTextDiff_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempDir);
                LoadDiffResults(results, tempDir, pathA, pathB, sourceFile);
                int diffCount = results.Count(r => r.HasDifferences);
                StatusMessage = diffCount == 0
                    ? $"{results.Count} pages — text identical"
                    : $"{results.Count} pages — {diffCount} with text differences";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Text comparison cancelled";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Text diff failed: {ex.Message}";
            }
            finally
            {
                DiffBusy = false;
                FileWorkerBusy = false;
            }
        }

        #endregion

        #region Diff Toggle & SideBySide

        public async Task OpenDiffToggleAsync()
        {
            var secondaryPath = GetSecondaryDiffPath();
            if (secondaryPath == null || !File.Exists(secondaryPath))
                return;
            if (secondaryRenderer == null) return;

            DiffShowingOriginal = false;
            int openStartGen = _secondaryCloseGen;

            try
            {
                if (secondaryFile == null)
                {
                    await DisposeSecondaryDocumentAsync().ConfigureAwait(false);
                    if (_secondaryCloseGen != openStartGen) return;

                    string path = secondaryPath;
                    MuPDFContext? newCtx = null;
                    MuPDFDocument? newDoc = null;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        newCtx = new MuPDFContext();
                        newDoc = new MuPDFDocument(newCtx, path);
                    }).GetTask().ConfigureAwait(false);

                    if (newDoc == null || newCtx == null) return;

                    if (_secondaryCloseGen != openStartGen)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => { newDoc.Dispose(); newCtx.Dispose(); }).GetTask().ConfigureAwait(false);
                        return;
                    }

                    secondaryFile = newDoc;
                    secondaryContext = newCtx;
                    Pagecount2 = newDoc.Pages.Count;
                    CurrentFile2 = null;
                }

                Pagecount2 = secondaryFile!.Pages.Count;
                dualFileMode = true;
                linkedPageMode = true;
                NotifyModeChanged();

                var docToInit = secondaryFile!;

                await renderSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_secondaryCloseGen != openStartGen) return;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_secondaryCloseGen != openStartGen) return;
                        int page = Math.Clamp(requestPage1, 0,
                            Math.Max(0, docToInit.Pages.Count - 1));
                        secondaryRenderer!.ReleaseResources();
                        secondaryRenderer.Initialize(docToInit, 1, page, ZOOM_LEVEL);
                        CurrentPage2 = page;
                        requestPage2 = page;
                    }).GetTask().ConfigureAwait(false);
                }
                finally { renderSemaphore.Release(); }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error opening diff toggle");
            }
        }

        public async Task CloseDiffToggleAsync(bool disposeDocument = true)
        {
            Interlocked.Increment(ref _secondaryCloseGen);
            DiffShowingOriginal = false;
            dualFileMode = false;
            NotifyModeChanged();
            if (disposeDocument)
                await DisposeSecondaryDocumentAsync().ConfigureAwait(false);
        }

        internal void CancelSecondaryOpen() => Interlocked.Increment(ref _secondaryCloseGen);

        public async Task<bool> OpenDiffSideBySideAsync()
        {
            var secondaryPath = GetSecondaryDiffPath();
            if (secondaryPath == null || !File.Exists(secondaryPath))
                return false;
            if (secondaryRenderer == null) return false;

            int openStartGen = _secondaryCloseGen;

            try
            {
                if (secondaryFile == null)
                {
                    await DisposeSecondaryDocumentAsync().ConfigureAwait(false);
                    if (_secondaryCloseGen != openStartGen) return false;

                    MuPDFContext? newCtx = null;
                    MuPDFDocument? newDoc = null;

                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        newCtx = new MuPDFContext();
                        newDoc = new MuPDFDocument(newCtx, secondaryPath);
                    }).GetTask().ConfigureAwait(false);

                    if (newDoc == null || newCtx == null) return false;

                    if (_secondaryCloseGen != openStartGen)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() => { newDoc.Dispose(); newCtx.Dispose(); }).GetTask().ConfigureAwait(false);
                        return false;
                    }

                    secondaryFile = newDoc;
                    secondaryContext = newCtx;
                    Pagecount2 = newDoc.Pages.Count;
                    CurrentFile2 = null;
                }

                Pagecount2 = secondaryFile!.Pages.Count;

                // Set up the dual layout on the UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    dualFileMode = true;
                    if (!twopageMode)
                    {
                        twopageMode = true;
                        Interlocked.Increment(ref _dualViewGen);
                    }
                    linkedPageMode = true;
                    NotifyModeChanged();
                    if (secondaryRenderer != null)
                        secondaryRenderer.IsVisible = true;
                    requestPage2 = requestPage1;
                    OnPropertyChanged(nameof(RequestPage2));
                    CurrentPage2 = requestPage2;
                }).GetTask().ConfigureAwait(false);

                if (_secondaryCloseGen != openStartGen) return false;

                // Wait for a layout pass so the secondary renderer gets valid bounds
                await Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Render).GetTask().ConfigureAwait(false);

                if (_secondaryCloseGen != openStartGen) return false;

                // Now initialize the secondary renderer with valid bounds
                await renderSemaphore.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_secondaryCloseGen != openStartGen) return false;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_secondaryCloseGen != openStartGen) return;
                        if (secondaryFile == null || secondaryRenderer == null) return;
                        int page = Math.Clamp(requestPage1, 0,
                            Math.Max(0, secondaryFile.Pages.Count - 1));
                        secondaryRenderer.ReleaseResources();
                        if (secondaryRenderer.Bounds is { Width: > 0, Height: > 0 })
                        {
                            secondaryRenderer.Initialize(secondaryFile, 1, page, ZOOM_LEVEL);
                            CurrentPage2 = page;
                            requestPage2 = page;
                        }
                    }).GetTask().ConfigureAwait(false);
                }
                finally { renderSemaphore.Release(); }

                return true;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error opening diff side-by-side");
                return false;
            }
        }

        public async Task CollapseSecondaryLayoutAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                secondaryRenderer?.ReleaseResources();
                if (secondaryRenderer != null)
                    secondaryRenderer.IsVisible = false;

                dualFileMode = false;
                if (twopageMode)
                {
                    twopageMode = false;
                    Interlocked.Increment(ref _dualViewGen);
                }
                CurrentFile2 = null;
                Pagecount2 = 0;
                NotifyModeChanged();
            }).GetTask().ConfigureAwait(false);
        }

        public async Task CloseDiffSideBySideAsync()
        {
            await CollapseSecondaryLayoutAsync().ConfigureAwait(false);
            await DisposeSecondaryDocumentAsync().ConfigureAwait(false);
        }

        #endregion
    }
}
