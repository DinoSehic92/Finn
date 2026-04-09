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
        /// <summary>
        /// Removes stale FinnDiff_* directories from the system temp folder
        /// that were left behind by a previous crash. Called once at startup.
        /// </summary>
        public static void CleanupStaleDiffTempDirs(TimeSpan maxAge = default)
        {
            if (maxAge == default) maxAge = TimeSpan.FromHours(1);
            try
            {
                var tempRoot = Path.GetTempPath();
                var cutoff = DateTime.UtcNow - maxAge;
                foreach (var dir in Directory.EnumerateDirectories(tempRoot, "FinnDiff_*"))
                {
                    try
                    {
                        if (Directory.GetLastWriteTimeUtc(dir) < cutoff)
                            Directory.Delete(dir, true);
                    }
                    catch { /* best effort per-directory */ }
                }
            }
            catch { /* best effort */ }
        }

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
        /// <summary>True when the last diff comparison was pixel-based (not text-based).</summary>
        private bool _lastDiffWasPixel;
        /// <summary>Auto-created diff annotation layer for A-side regions, tracked for auto-removal.</summary>
        private AnnotationLayer? _diffAnnotationLayer;
        /// <summary>The FileData that <see cref="_diffAnnotationLayer"/> was added to. Tracked so cleanup
        /// removes from the correct file even if <see cref="CurrentFile"/> has changed since.</summary>
        private FileData? _diffAnnotationLayerOwner;
        /// <summary>Auto-created diff annotation layer for B-side regions (revised document).</summary>
        private AnnotationLayer? _diffAnnotationLayerB;
        /// <summary>The FileData that <see cref="_diffAnnotationLayerB"/> was added to.</summary>
        private FileData? _diffAnnotationLayerBOwner;

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
        private DiffViewMode _diffViewMode = DiffViewMode.Toggle;
        private int _diffTolerance = PdfDiffService.DefaultTolerance;
        private bool _diffRerunBusy;
        private int _diffHeaderHeight = 0;
        private int _diffFooterHeight = 0;

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
                    OnPropertyChanged(nameof(ShowDiffToolbar));
                    OnPropertyChanged(nameof(ShowDiffToggle));
                    OnPropertyChanged(nameof(IsUserDualFileMode));
                    OnPropertyChanged(nameof(CanToggleLayout));
                    OnPropertyChanged(nameof(CanSearch));
                    // Auto-close text search when entering diff mode, but keep diff page list
                    if (value && searchMode && !_diffPageListMode) SearchMode = false;
                }
            }
        }

        /// <summary>
        /// True when the diff toolbar should be visible: either diff mode is active
        /// OR a comparison is running (so the toolbar doesn't flash away and back).
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ShowDiffToolbar => _diffOverlayActive || diffBusy;

        /// <summary>Whether diff results are loaded (controls toggle button visibility).</summary>
        public bool HasDiffResults => _diffResults != null && _diffResults.Count > 0;

        /// <summary>True when pixel-diff results are loaded (controls tolerance slider visibility).</summary>
        public bool HasPixelDiffResults => HasDiffResults && _lastDiffWasPixel;

        /// <summary>True when text-diff results are loaded (controls margin inputs visibility).</summary>
        public bool HasTextDiffResults => HasDiffResults && !_lastDiffWasPixel;

        /// <summary>Active diff view mode: Overlay, Toggle (A/B swap), or SideBySide.</summary>
        public DiffViewMode DiffViewMode
        {
            get => _diffViewMode;
            set
            {
                if (SetProperty(ref _diffViewMode, value))
                {
                    OnPropertyChanged(nameof(ShowDiffToggle));
                    OnPropertyChanged(nameof(IsToggleMode));
                    OnPropertyChanged(nameof(IsSideBySideMode));
                }
            }
        }

        // Bool properties for radio-style toggle buttons in the diff toolbar.
        // Bindings are OneWay — the View drives mode changes via Click handlers.
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

        /// <summary>
        /// Height in PDF points of the header zone to ignore during text diff.
        /// Words in this zone are stripped before comparison to avoid false
        /// positives from page numbers, section titles, etc. 0 = include
        /// everything (no stripping). Standard A4 page is 842pt tall.
        /// </summary>
        public int DiffHeaderHeight
        {
            get => _diffHeaderHeight;
            set => SetProperty(ref _diffHeaderHeight, Math.Clamp(value, 0, 400));
        }

        /// <summary>
        /// Height in PDF points of the footer zone to ignore during text diff.
        /// 0 = include everything (no stripping). Standard A4 page is 842pt tall.
        /// </summary>
        public int DiffFooterHeight
        {
            get => _diffFooterHeight;
            set => SetProperty(ref _diffFooterHeight, Math.Clamp(value, 0, 400));
        }

        /// <summary>Whether re-running the diff is possible (results loaded, paths known).</summary>
        public bool CanRerunDiff => _diffResults is { Count: > 0 } && !_diffRerunBusy;

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
                int totalChanged = _diffResults.Sum(r => r.ChangedWords);
                int totalWords = _diffResults.Sum(r => r.TotalWords);
                if (totalWords > 0 && totalChanged > 0)
                    return $"{changed}/{_diffResults.Count} pages — {totalChanged} words ({100.0 * totalChanged / totalWords:F0}%)";
                return $"{changed}/{_diffResults.Count} pages differ";
            }
        }

        public List<DiffPathChoice> DiffPathChoices { get; private set; } = [];
        public bool HasDiffPathChoices => DiffPathChoices.Count >= 2;

        public bool CanCompareVersions
        {
            get
            {
                var file = _diffSourceFile ?? _versionSourceFile ?? currentFile;
                if (file is { HasVersions: true }) return true;
                // A diff is also available when explicit A/B paths have been set
                // (e.g. from a previous comparison or version context menu).
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
                    DiffColorA.R, DiffColorA.G, DiffColorA.B);
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

        public async Task LoadDiffResultsAsync(List<DiffResultData> results, string tempDir, bool isPixel, string? originalPdfPath = null, string? revisedPdfPath = null, FileData? sourceFile = null)
        {
            CleanupDiffTempDir();
            _diffResults = results;
            _diffTempDir = tempDir;
            _diffOriginalPdfPath = originalPdfPath;
            _diffRevisedPdfPath = revisedPdfPath;
            _diffSourceFile = sourceFile;

            // Property notifications and search-panel updates must run on the
            // UI thread. This method may be called from a background thread
            // after ConfigureAwait(false) in RunDiffAsync / RunTextDiffAsync.
            if (Dispatcher.UIThread.CheckAccess())
                NotifyDiffResultsLoaded(results, isPixel);
            else
                await Dispatcher.UIThread.InvokeAsync(() => NotifyDiffResultsLoaded(results, isPixel)).GetTask().ConfigureAwait(false);
        }

        private void NotifyDiffResultsLoaded(List<DiffResultData> results, bool isPixel)
        {
            _lastDiffWasPixel = isPixel;

            RefreshDiffPathChoices();
            DiffOverlayActive = results.Count > 0;
            OnPropertyChanged(nameof(HasDiffResults));
            OnPropertyChanged(nameof(HasPixelDiffResults));
            OnPropertyChanged(nameof(HasTextDiffResults));
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
                string detail = result.TotalWords > 0
                    ? $"{result.Label} — {result.ChangedWords}/{result.TotalWords} words"
                    : $"{result.Label} — differs";
                SearchPagesText.Add(detail);
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
            ClearDiffResultsCore();
            _diffOriginalPdfPath = null;
            _diffRevisedPdfPath = null;
            _diffSourceFile = null;
            _diffChoiceA = null;
            _diffChoiceB = null;
            DiffOverlayActive = false;
            // Detach the secondary file so stale B-side annotation layers
            // cannot be re-attached by SyncSecondaryLayers / EnsureDiffBLayerOnSecondary.
            CurrentFile2 = null;
            OnPropertyChanged(nameof(CanCompareVersions));
            OnPropertyChanged(nameof(ShowDiffToolbar));
            OnPropertyChanged(nameof(ShowDiffToggle));
            OnPropertyChanged(nameof(DiffChoiceA));
            OnPropertyChanged(nameof(DiffChoiceB));
            DiffPathChoices = [];
            OnPropertyChanged(nameof(DiffPathChoices));
            OnPropertyChanged(nameof(HasDiffPathChoices));
        }

        /// <summary>
        /// Clears diff results and overlays but keeps the toolbar visible and
        /// path choices intact so the user can immediately run another comparison.
        /// </summary>
        public void ClearDiffResultsOnly()
        {
            ClearDiffResultsCore();
            OnPropertyChanged(nameof(DiffSummary));
        }

        /// <summary>Shared cleanup logic for diff results, layers, and temp files.</summary>
        private void ClearDiffResultsCore()
        {
            _toleranceDebounceCts?.Cancel();
            _toleranceDebounceCts?.Dispose();
            _toleranceDebounceCts = null;
            RemoveDiffAnnotationLayer();
            CleanupDiffTempDir();
            _diffResults = null;
            _diffTempDir = null;
            _diffShowingOriginal = false;
            _lastDiffWasPixel = false;
            // Close the diff page list if it was open
            if (searchMode)
            {
                ClearSearch();
                DiffPageListMode = false;
                SetProperty(ref searchMode, false, nameof(SearchMode));
            }
            OnPropertyChanged(nameof(HasDiffResults));
            OnPropertyChanged(nameof(HasPixelDiffResults));
            OnPropertyChanged(nameof(HasTextDiffResults));
            OnPropertyChanged(nameof(CanRerunDiff));
            OnPropertyChanged(nameof(DiffShowingOriginal));
            OnPropertyChanged(nameof(DiffSummary));
        }

        /// <summary>
        /// Toggles the diff page list panel (search panel in diff mode).
        /// </summary>
        public void ToggleDiffPageList()
        {
            if (searchMode && _diffPageListMode)
            {
                // Close the panel
                SetProperty(ref searchMode, false, nameof(SearchMode));
            }
            else if (HasDiffResults)
            {
                // Re-open with existing results
                if (!_diffPageListMode)
                    PopulateDiffPageList();
                else
                    SetProperty(ref searchMode, true, nameof(SearchMode));
            }
        }

        public void CloseDiffModeSync()
        {
            // Guard: nothing to clean up if diff isn't active.
            // Prevents redundant calls from releasing an active secondary
            // document that was just loaded by a new diff session.
            if (!_diffOverlayActive && !dualFileMode) return;

            // Hide the secondary renderer but don't ReleaseResources here —
            // the caller (OnCloseDiffMode / CloseDiffViews) is responsible
            // for disposing the secondary document to avoid double-dispose.
            if (secondaryRenderer != null)
                secondaryRenderer.IsVisible = false;

            if (twopageMode || dualFileMode)
            {
                twopageMode = false;
                dualFileMode = false;
                NotifyModeChanged();
            }

            ClearDiffResults();
        }

        /// <summary>
        /// Default color for A-side (removed) highlights — warm red.
        /// </summary>
        private static readonly Avalonia.Media.Color DiffColorA = Avalonia.Media.Color.FromRgb(220, 75, 75);
        /// <summary>
        /// Default color for B-side (added) highlights — cool blue.
        /// </summary>
        private static readonly Avalonia.Media.Color DiffColorB = Avalonia.Media.Color.FromRgb(60, 145, 220);

        /// <summary>
        /// Creates up to two annotation layers from the diff results:
        /// <list type="bullet">
        ///   <item><b>A-layer</b> (warm red) — regions of words removed from the original (A).</item>
        ///   <item><b>B-layer</b> (cool blue) — regions of words added in the revised (B).</item>
        /// </list>
        /// Pixel-diff regions (<see cref="DiffSide.Both"/>) go into both layers.
        /// The layers are added to <see cref="PreviewViewModel.CurrentFile"/> so the
        /// renderer draws them. Returns the A-layer (or the combined layer for pixel diffs).
        /// </summary>
        public AnnotationLayer? CreateDiffAnnotationLayer(string? comparedFileName = null)
        {
            if (_diffResults == null || _diffResults.Count == 0 || CurrentFile == null) return null;

            // Remove previous auto-created layers to avoid stacking.
            RemoveDiffAnnotationLayer();

            string labelA = _diffChoiceA?.Label ?? "A";
            string labelB = _diffChoiceB?.Label ?? "B";
            string baseName = string.IsNullOrEmpty(comparedFileName)
                ? $"Diff {DateTime.Now:yyyy-MM-dd HH:mm}"
                : $"Diff vs {Path.GetFileNameWithoutExtension(comparedFileName)}";

            // Determine whether we have side-tagged regions (text diff) or
            // untagged regions (pixel diff). Text diff gets A + B annotation
            // layers; pixel diff uses overlay images instead (different colors
            // on each renderer, handled by SyncDiffOverlay).
            bool hasTextSides = false;
            foreach (var result in _diffResults)
            {
                if (result.Regions == null) continue;
                foreach (var r in result.Regions)
                    if (r.Side != DiffSide.Both) { hasTextSides = true; break; }
                if (hasTextSides) break;
            }

            // Pixel diff: no annotation shapes — the overlay images handle it.
            if (!hasTextSides) return null;

            var layerA = new AnnotationLayer
            {
                Name = $"{baseName} — Removed (A)",
                Color = DiffColorA
            };
            var layerB = new AnnotationLayer
            {
                Name = $"{baseName} — Added (B)",
                Color = DiffColorB
            };

            foreach (var result in _diffResults)
            {
                // Actual 0-based page numbers in each document.
                // PageLabel is 1-based, so subtract 1. Use alignment index as fallback.
                int pageA = result.PageLabelA.HasValue ? result.PageLabelA.Value - 1 : result.PageIndex;
                int pageB = result.PageLabelB.HasValue ? result.PageLabelB.Value - 1 : result.PageIndex;

                // Add version label on EVERY aligned page so each page is
                // clearly identified, not just pages with differences.
                if (pageA >= 0)
                    AddPageLabel(layerA, pageA, $"A: {labelA}  (p.{result.PageLabelA ?? pageA + 1})", DiffColorA);
                if (pageB >= 0)
                    AddPageLabel(layerB, pageB, $"B: {labelB}  (p.{result.PageLabelB ?? pageB + 1})", DiffColorB);

                if (!result.HasDifferences) continue;

                var regions = result.Regions
                    ?? (result.DiffPath != null ? PdfDiffService.ExtractDiffRegions(result.DiffPath, PdfDiffService.ZOOM) : null);
                if (regions == null || regions.Count == 0) continue;

                foreach (var r in regions)
                {
                    switch (r.Side)
                    {
                        case DiffSide.RemovedFromA:
                        {
                            if (pageA < 0) break;
                            var shape = MakeShape(r, DiffColorA);
                            if (!layerA.PageShapes.TryGetValue(pageA, out var list))
                                layerA.PageShapes[pageA] = list = [];
                            list.Add(shape);
                            break;
                        }
                        case DiffSide.AddedToB:
                        {
                            if (pageB < 0) break;
                            var shape = MakeShape(r, DiffColorB);
                            if (!layerB.PageShapes.TryGetValue(pageB, out var list))
                                layerB.PageShapes[pageB] = list = [];
                            list.Add(shape);
                            break;
                        }
                    }
                }
            }

            // Add gray zone indicators for ignored header/footer areas.
            if (_diffHeaderHeight > 0 || _diffFooterHeight > 0)
            {
                AddZoneIndicators(layerA, _diffResults, true, _diffHeaderHeight, _diffFooterHeight);
                AddZoneIndicators(layerB, _diffResults, false, _diffHeaderHeight, _diffFooterHeight);
            }

            layerA.RecalculateCounts();
            CurrentFile.AnnotationLayers.Add(layerA);
            _diffAnnotationLayer = layerA;
            _diffAnnotationLayerOwner = CurrentFile;

            layerB.RecalculateCounts();

            // Place the B-layer on the secondary renderer (right side).
            // Create a lightweight stub if CurrentFile2 doesn't exist yet.
            if (CurrentFile2 == null)
                CurrentFile2 = new FileData
                {
                    Namn = "Diff B",
                    Sökväg = _diffRevisedPdfPath ?? ""
                };
            CurrentFile2.AnnotationLayers.Add(layerB);
            _diffAnnotationLayerB = layerB;
            _diffAnnotationLayerBOwner = CurrentFile2;

            OnPropertyChanged("LayersChanged");
            // Notify the view so the secondary renderer picks up the new layers.
            OnPropertyChanged(nameof(CurrentFile2));
            return layerA;
        }

        /// <summary>Creates a filled rectangle shape annotation from a diff region.</summary>
        private static ShapeAnnotation MakeShape(DiffRegion r, Avalonia.Media.Color color) => new()
        {
            ShapeType = InlineAnnotationTool.Rectangle,
            Start = new Avalonia.Point(r.X, r.Y),
            End = new Avalonia.Point(r.X + r.Width, r.Y + r.Height),
            StrokeWidth = 1.5,
            Opacity = 0.15 + 0.30 * r.Confidence,
            IsFilled = true,
            Color = color
        };

        /// <summary>
        /// Adds a version label at the top-centre of the specified page.
        /// Uses <see cref="TextAnnotation.IsLabel"/> so the renderer draws it as
        /// a simple text pill rather than a full textbox frame.
        /// </summary>
        private static void AddPageLabel(AnnotationLayer layer, int page, string text, Avalonia.Media.Color color)
        {
            var label = new TextAnnotation
            {
                Position = new Avalonia.Point(297, 6),
                Text = text,
                FontSize = 10,
                Color = color,
                Opacity = 0.9,
                IsLabel = true
            };
            if (!layer.PageTexts.TryGetValue(page, out var list))
                layer.PageTexts[page] = list = [];
            list.Add(label);
        }

        /// <summary>Gray color used for zone exclusion indicators.</summary>
        private static readonly Avalonia.Media.Color ZoneIndicatorColor = Avalonia.Media.Color.FromRgb(128, 128, 128);

        /// <summary>
        /// Adds semi-transparent gray rectangles on every page to visually
        /// indicate the header/footer zones that were excluded from comparison.
        /// </summary>
        private void AddZoneIndicators(
            AnnotationLayer layer, List<DiffResultData> results,
            bool isASide, int headerHeight, int footerHeight)
        {
            // Use the primary doc for A, secondary for B. Fall back to primary.
            var doc = isASide ? MainPreviewFile : (secondaryFile ?? MainPreviewFile);
            if (doc == null) return;

            var visited = new HashSet<int>();
            foreach (var result in results)
            {
                int page = isASide
                    ? (result.PageLabelA.HasValue ? result.PageLabelA.Value - 1 : result.PageIndex)
                    : (result.PageLabelB.HasValue ? result.PageLabelB.Value - 1 : result.PageIndex);
                if (page < 0 || !visited.Add(page)) continue;

                double pageWidth = 595.0, pageHeight = 842.0;
                if (page < doc.Pages.Count)
                {
                    var b = doc.Pages[page].Bounds;
                    pageWidth = Math.Abs(b.X1 - b.X0);
                    pageHeight = Math.Abs(b.Y1 - b.Y0);
                }

                if (!layer.PageShapes.TryGetValue(page, out var shapes))
                    layer.PageShapes[page] = shapes = [];

                if (headerHeight > 0)
                {
                    shapes.Add(new ShapeAnnotation
                    {
                        ShapeType = InlineAnnotationTool.Rectangle,
                        Start = new Avalonia.Point(0, 0),
                        End = new Avalonia.Point(pageWidth, headerHeight),
                        StrokeWidth = 0,
                        Opacity = 0.08,
                        IsFilled = true,
                        Color = ZoneIndicatorColor
                    });
                }
                if (footerHeight > 0)
                {
                    shapes.Add(new ShapeAnnotation
                    {
                        ShapeType = InlineAnnotationTool.Rectangle,
                        Start = new Avalonia.Point(0, pageHeight - footerHeight),
                        End = new Avalonia.Point(pageWidth, pageHeight),
                        StrokeWidth = 0,
                        Opacity = 0.08,
                        IsFilled = true,
                        Color = ZoneIndicatorColor
                    });
                }
            }
        }

        /// <summary>
        /// Removes the auto-created diff annotation layer from the current file.
        /// Called when switching diff modes or clearing diff results.
        /// </summary>
        public void RemoveDiffAnnotationLayer()
        {
            bool removed = false;
            if (_diffAnnotationLayer != null)
            {
                // Remove from the file it was actually added to, not CurrentFile
                // which may have changed since the diff was created.
                var owner = _diffAnnotationLayerOwner ?? CurrentFile;
                owner?.AnnotationLayers?.Remove(_diffAnnotationLayer);
                _diffAnnotationLayer = null;
                _diffAnnotationLayerOwner = null;
                removed = true;
            }
            if (_diffAnnotationLayerB != null)
            {
                var owner = _diffAnnotationLayerBOwner ?? CurrentFile2;
                owner?.AnnotationLayers?.Remove(_diffAnnotationLayerB);
                _diffAnnotationLayerB = null;
                _diffAnnotationLayerBOwner = null;
                removed = true;
            }
            if (removed)
                OnPropertyChanged("LayersChanged");
        }

        /// <summary>
        /// Detaches the tracked diff annotation layer so it won't be auto-removed
        /// when diff mode is closed. Call after the user explicitly saves the layer.
        /// </summary>
        public void DetachDiffAnnotationLayer()
        {
            _diffAnnotationLayer = null;
            _diffAnnotationLayerOwner = null;
            _diffAnnotationLayerB = null;
            _diffAnnotationLayerBOwner = null;
        }

        /// <summary>
        /// Ensures the B-side diff annotation layer is attached to
        /// <see cref="CurrentFile2"/>. The secondary document open/close flow
        /// resets <c>CurrentFile2 = null</c>, so this must be called each time
        /// the view needs to sync layers to the secondary renderer.
        /// </summary>
        public void EnsureDiffBLayerOnSecondary()
        {
            if (_diffAnnotationLayerB == null) return;
            if (CurrentFile2 == null)
                CurrentFile2 = new FileData
                {
                    Namn = "Diff B",
                    Sökväg = _diffRevisedPdfPath ?? ""
                };
            if (!CurrentFile2.AnnotationLayers.Contains(_diffAnnotationLayerB))
                CurrentFile2.AnnotationLayers.Add(_diffAnnotationLayerB);
            // Keep the owner reference in sync so RemoveDiffAnnotationLayer
            // always removes from the correct FileData instance.
            _diffAnnotationLayerBOwner = CurrentFile2;
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

        public string? GetDiffImagePathB(int page)
        {
            var result = GetDiffResult(page);
            return result?.HasDifferences == true ? result.DiffPathB : null;
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

        /// <summary>
        /// Restores choices and source file after a full close, rebuilding
        /// the path choices list so combo boxes show the current A/B selection.
        /// </summary>
        public void RestoreDiffChoices(DiffPathChoice choiceA, DiffPathChoice choiceB, FileData? sourceFile)
        {
            _diffSourceFile = sourceFile;
            _diffOriginalPdfPath = choiceA.Path;
            _diffRevisedPdfPath = choiceB.Path;
            RefreshDiffPathChoices();
            // RefreshDiffPathChoices matches by path — override with the exact
            // objects so the caller's references stay consistent.
            _diffChoiceA = DiffPathChoices.FirstOrDefault(c =>
                string.Equals(c.Path, choiceA.Path, StringComparison.OrdinalIgnoreCase)) ?? choiceA;
            _diffChoiceB = DiffPathChoices.FirstOrDefault(c =>
                string.Equals(c.Path, choiceB.Path, StringComparison.OrdinalIgnoreCase)) ?? choiceB;
            OnPropertyChanged(nameof(DiffChoiceA));
            OnPropertyChanged(nameof(DiffChoiceB));
            OnPropertyChanged(nameof(CanCompareVersions));
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
                // Resolve through local cache so diffs on cached files are fast
                string resolvedA = await ResolveCachedPathAsync(pathA, ct).ConfigureAwait(false);
                string resolvedB = await ResolveCachedPathAsync(pathB, ct).ConfigureAwait(false);

                var progress = new Progress<int>(p => StatusMessage = $"Comparing… {p}%");
                var (results, dir) = await PdfDiffService.CompareAsync(resolvedA, resolvedB, progress, ct, _diffTolerance,
                    DiffColorA.R, DiffColorA.G, DiffColorA.B);
                await LoadDiffResultsAsync(results, dir, isPixel: true, originalPdfPath ?? pathA, pathB, sourceFile);
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
                // Resolve through local cache so diffs on cached files are fast
                string resolvedA = await ResolveCachedPathAsync(pathA, ct).ConfigureAwait(false);
                string resolvedB = await ResolveCachedPathAsync(pathB, ct).ConfigureAwait(false);

                var progress = new Progress<int>(p => StatusMessage = $"Text comparing… {p}%");
                var results = await TextDiffService.CompareAsync(resolvedA, resolvedB, progress, ct,
                    _diffHeaderHeight, _diffFooterHeight);
                // Text diff produces no images; create an empty temp dir for LoadDiffResultsAsync.
                string tempDir = Path.Combine(Path.GetTempPath(), "FinnTextDiff_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempDir);
                await LoadDiffResultsAsync(results, tempDir, isPixel: false, pathA, pathB, sourceFile);
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

                    // Resolve through local cache
                    string path = await ResolveCachedPathAsync(secondaryPath).ConfigureAwait(false);
                    var (newDoc, newCtx) = CreateMuPDFDocument(path, false);

                    if (_secondaryCloseGen != openStartGen)
                    {
                        newDoc.Dispose();
                        newCtx.Dispose();
                        return;
                    }

                    SwapSecondaryDocument(newDoc, newCtx);
                    Pagecount2 = secondaryFile!.Pages.Count;
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

                    // Resolve through local cache
                    string resolvedPath = await ResolveCachedPathAsync(secondaryPath).ConfigureAwait(false);
                    var (newDoc, newCtx) = CreateMuPDFDocument(resolvedPath, false);

                    if (_secondaryCloseGen != openStartGen)
                    {
                        newDoc.Dispose();
                        newCtx.Dispose();
                        return false;
                    }

                    SwapSecondaryDocument(newDoc, newCtx);
                    Pagecount2 = secondaryFile!.Pages.Count;
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
