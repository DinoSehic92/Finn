using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Finn.Model;
using MuPDFCore.MuPDFRenderer;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Finn.Controls;

/// <summary>
/// PDFRenderer subclass that draws ink annotation strokes directly in its
/// own Render() call — after the base PDF content.  Because the strokes
/// share the same DrawingContext and coordinate system as the PDF, they
/// are physically impossible to desync during pan or zoom.
///
/// MuPDFCore's renderer maps DisplayArea → Bounds proportionally:
///   screenX = (docX - DisplayArea.Left) / DisplayArea.Width  * Bounds.Width
///   screenY = (docY - DisplayArea.Top)  / DisplayArea.Height * Bounds.Height
/// We use the exact same formula for stroke rendering and input capture.
///
/// Strokes are organised into named layers that can be toggled on/off.
/// </summary>
public class AnnotatedPDFRenderer : PDFRenderer
{
    private InkStroke? _activeStroke;
    private ShapeAnnotation? _activeShape;
    private MeasurementAnnotation? _activeMeasurement;
    private Point? _measurementPreviewPoint;
    private Point? _lastPointerPdfPos;
    private InkStroke? _activePolyline;
    private Point? _polylinePreviewEnd;
    private int _currentPage;

    // Rubber-band marquee selection rectangle (PDF-space)
    private Point? _rubberBandStart;
    private Point? _rubberBandEnd;
    /// <summary>True when the current rubber-band drag was initiated right-to-left (crossing/touching mode).</summary>
    private bool _rubberBandCrossing;
    // When true the rubber-band is used for screenshot selection: render a dim veil
    // over the area outside the selection instead of the standard translucent fill.
    private bool _screenshotMode;

    // Eraser hover highlight: the item the eraser is hovering over
    private object? _eraserHoverItem;
    // Sticky-note hover: which note's popup to show
    private TextAnnotation? _stickyNoteHoverItem;
    private int _totalStrokeCount;
    private int _totalShapeCount;
    private int _totalTextCount;
    private int _totalMeasurementCount;

    // ── Cached rendering resources (avoid per-frame allocations) ────────
    private readonly List<TextOverlayDrawOp.TextItem> _textItemPool = new(64);
    // Double-buffered snapshot list: alternates between two pre-allocated lists
    // so the render thread reads one while the UI thread populates the other.
    private List<TextOverlayDrawOp.TextItem> _textSnapshotA = new(64);
    private List<TextOverlayDrawOp.TextItem> _textSnapshotB = new(64);
    private bool _useSnapshotA = true;
    // Cached SKTypeface lookups — FromFamilyName is expensive native interop
    private static readonly ConcurrentDictionary<string, SKTypeface> _typefaceCache = new();
    // Cached brushes / pens used every frame (static colors, scale-independent)
    private static readonly IBrush s_eraserHoverBrush =
        new SolidColorBrush(Color.FromArgb(60, 255, 50, 50)).ToImmutable();
    private static readonly IBrush s_eraserHoverPenBrush =
        new SolidColorBrush(Color.FromArgb(140, 255, 50, 50)).ToImmutable();
    private static readonly IBrush s_selectPenBrush =
        new SolidColorBrush(Color.FromArgb(190, 80, 110, 180)).ToImmutable();
    private static readonly IBrush s_vertexBrush =
        new SolidColorBrush(Color.FromRgb(255, 255, 255)).ToImmutable();
    private static readonly IBrush s_vertexPenBrush =
        new SolidColorBrush(Color.FromArgb(160, 60, 60, 60)).ToImmutable();
    private static readonly IBrush s_cornerBrush =
        new SolidColorBrush(Color.FromArgb(160, 80, 110, 180)).ToImmutable();
    private static readonly IBrush s_snapBrush =
        new SolidColorBrush(Color.FromArgb(180, 16, 185, 129)).ToImmutable();
    private static readonly IBrush s_snapVertexBrush =
        new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)).ToImmutable();
    private static readonly IBrush s_snapVertexPenBrush =
        new SolidColorBrush(Color.FromArgb(220, 16, 185, 129)).ToImmutable();
    private static readonly IBrush s_rubberBandFillBrush =
        new SolidColorBrush(Color.FromArgb(25, 59, 130, 217)).ToImmutable();
    private static readonly IBrush s_rubberBandBorderBrush =
        new SolidColorBrush(Color.FromArgb(160, 59, 130, 217)).ToImmutable();
    // Crossing selection (right-to-left drag) — green tint
    private static readonly IBrush s_rubberBandCrossingFillBrush =
        new SolidColorBrush(Color.FromArgb(25, 34, 160, 80)).ToImmutable();
    private static readonly IBrush s_rubberBandCrossingBorderBrush =
        new SolidColorBrush(Color.FromArgb(180, 34, 160, 80)).ToImmutable();
    private static readonly IBrush s_screenshotVeilBrush =
        new SolidColorBrush(Color.FromArgb(100, 0, 0, 0)).ToImmutable();
    private static readonly IBrush s_screenshotSelectionBrush =
        new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)).ToImmutable();
    private static readonly IBrush s_selectHoverBrush =
        new SolidColorBrush(Color.FromArgb(90, 232, 125, 47)).ToImmutable();
    private static readonly IBrush s_gridDotBrush =
        new SolidColorBrush(Color.FromArgb(40, 120, 120, 120)).ToImmutable();
    private static readonly DashStyle s_dashStyle4_3 = new([4, 3], 0);
    private static readonly DashStyle s_dashStyle5_4 = new([5, 4], 0);
    private static readonly DashStyle s_dashStyle3_3 = new([3, 3], 0);

    // ── Cached pens for render chrome (constant thickness, static brush) ──
    private static readonly IPen s_snapGuidePen =
        new Pen(new SolidColorBrush(Color.FromArgb(180, 16, 185, 129)).ToImmutable(),
            1.0, dashStyle: new DashStyle([3, 3], 0), lineCap: PenLineCap.Flat);
    private static readonly IPen s_snapVertexPen =
        new Pen(s_snapVertexPenBrush, 1.4, lineCap: PenLineCap.Round);
    private static readonly IBrush s_polarBgBrush =
        new SolidColorBrush(Color.FromArgb(200, 25, 30, 40)).ToImmutable();
    private static readonly IPen s_polarBorderPen =
        new Pen(new SolidColorBrush(Color.FromArgb(160, 16, 185, 129)).ToImmutable(), 0.8);
    private static readonly IPen s_rubberBandPen =
        new Pen(new SolidColorBrush(Color.FromArgb(160, 59, 130, 217)).ToImmutable(),
            1.0, dashStyle: new DashStyle([4, 3], 0), lineCap: PenLineCap.Flat);
    private static readonly IPen s_rubberBandCrossingPen =
        new Pen(new SolidColorBrush(Color.FromArgb(180, 34, 160, 80)).ToImmutable(),
            1.0, dashStyle: new DashStyle([4, 3], 0), lineCap: PenLineCap.Flat);
    private static readonly IPen s_screenshotBorderPen =
        new Pen(new SolidColorBrush(Color.FromArgb(220, 220, 50, 50)).ToImmutable(),
            1.5, dashStyle: new DashStyle([4, 3], 0), lineCap: PenLineCap.Flat);
    // Pens whose thickness depends on penScale are cached per-frame.
    private IPen? _cachedSelectHoverPen;
    private IPen? _cachedSelectionPen;
    private IPen? _cachedEraserHoverPen;
    private double _cachedChromePenScale;
    // Cached grid-dots geometry: rebuilt only when viewport or spacing changes.
    private StreamGeometry? _cachedGridGeometry;
    private Rect _cachedGridDa;
    private double _cachedGridSpacing;
    // Static group selection pen (constant style, no per-frame allocation).
    private static readonly IPen s_groupPen =
        new Pen(s_selectPenBrush, 1.2, dashStyle: s_dashStyle5_4, lineCap: PenLineCap.Round);
    // Constant-thickness vertex pen used by selection highlight (avoid per-frame allocation)
    private static readonly IPen s_vertexPen =
        new Pen(s_vertexPenBrush, 1.2, lineCap: PenLineCap.Round);
    // Polyline/arrow preview pens depend on user-chosen color/width.
    private IPen? _cachedPreviewPen;
    private Color _cachedPreviewColor;
    private double _cachedPreviewWidth;
    // Cursor preview brush and crosshair pen (depend on StrokeColor).
    private IBrush? _cachedCursorBrush;
    private IPen? _cachedCrosshairPen;
    private Color _cachedCursorColor;

    private enum UndoType { Stroke, Shape, Text, Measurement, ClearPage, Move, Delete, PropertyChange, ZOrder, GroupResize, GroupPropertyChange, GroupMove, GroupAdd }
    private readonly Stack<(UndoType type, int page, object? data, AnnotationLayer? layer)> _undoStack = new();
    private readonly Stack<(UndoType type, int page, object item, AnnotationLayer? layer)> _redoStack = new();

    /// <summary>Raised whenever annotations are added, removed, or modified.</summary>
    public event Action? AnnotationChanged;
    public void NotifyAnnotationChanged() => AnnotationChanged?.Invoke();

    private record ClearPageSnapshot(
        List<InkStroke>? Strokes, List<ShapeAnnotation>? Shapes,
        List<TextAnnotation>? Texts, List<MeasurementAnnotation>? Measurements);

    /// <summary>Items collected during a <see cref="BeginGroupAdd"/>/<see cref="EndGroupAdd"/> batch.</summary>
    private record GroupAddSnapshot(List<object> Items);

    // Batch-add support: when active, Place* methods append to this list instead of pushing individual undo entries.
    private bool _groupAddActive;
    private List<object>? _groupAddItems;

    // Cursor preview position for pen-size visualization
    private Point? _cursorPdfPos;

    /// <summary>Tool-type for the text placement ghost (Text, StickyNote, or ArrowText).</summary>
    private InlineAnnotationTool? _textPlacementPreviewTool;
    /// <summary>PDF-space position for the text placement ghost.</summary>
    private Point? _textPlacementPreviewPos;

    // Snap-to-alignment guides (PDF-space X/Y coordinates to draw as dotted lines)
    private double? _snapGuideX;
    private double? _snapGuideY;
    private const double DefaultMeasurementScale = 25.4 / 72.0;

    private enum SnapKind { None, Vertex, Endpoint, Center, Bounds, Grid, Midpoint }
    private SnapKind _snapKindX;
    private SnapKind _snapKindY;
    /// <summary>The vertex position being snapped (for indicator dot rendering).</summary>
    private Point? _snapVertexPos;

    // Polar tracking: live angle + distance shown near the cursor while drawing
    private double? _polarAngleDeg;
    private double? _polarDistancePdf;
    private Point? _polarCursorScreen; // screen-space cursor position for label placement

    private static int SnapPriority(SnapKind k) => k switch
    {
        SnapKind.Vertex => 1,
        SnapKind.Endpoint => 2,
        SnapKind.Center => 3,
        SnapKind.Midpoint => 4,
        SnapKind.Bounds => 5,
        SnapKind.Grid => 6,
        _ => 99
    };

    private static bool IsBetterSnap(double dist, double bestDist, SnapKind kind, SnapKind bestKind)
        => dist < bestDist || (Math.Abs(dist - bestDist) < 0.0001 && SnapPriority(kind) < SnapPriority(bestKind));

    private ObservableCollection<AnnotationLayer> _layers = [];

    /// <summary>
    /// The current layer collection being rendered. Ownership is external
    /// (FileData for PDFs, PreviewViewModel for whiteboard). The renderer
    /// is purely a rendering surface.
    /// </summary>
    public ObservableCollection<AnnotationLayer> Layers => _layers;

    /// <summary>
    /// Swap the layer collection the renderer draws from.
    /// Call this when switching files or entering/leaving whiteboard mode.
    /// </summary>
    public void SetLayers(ObservableCollection<AnnotationLayer>? layers)
    {
        _activeStroke = null;
        _activeShape = null;
        _activeMeasurement = null;
        _measurementPreviewPoint = null;
        _activePolyline = null;
        _polylinePreviewEnd = null;
        _rubberBandStart = null;
        _rubberBandEnd = null;
        _layers = layers ?? [];
        _annotationGroups.Clear();
        foreach (var layer in _layers) layer.RecalculateCounts();
        _activeLayer = _layers.Count > 0 ? _layers[0] : null;
        _undoStack.Clear();
        _redoStack.Clear();
        RecalculateStrokeCount();
        RestoreMeasurementScale();
        if (IsViewerInitialized)
            InvalidateVisual();
    }

    /// <summary>
    /// Picks up MeasurementScale from the first existing measurement in the
    /// current layer collection. Each MeasurementAnnotation stores the
    /// calibrated scale, so this restores the per-file value after a file switch.
    /// </summary>
    private void RestoreMeasurementScale()
    {
        foreach (var layer in _layers)
        {
            foreach (var list in layer.PageMeasurements.Values)
            {
                if (list.Count > 0)
                {
                    MeasurementScale = list[0].Scale;
                    return;
                }
            }
        }
        MeasurementScale = DefaultMeasurementScale;
    }

    private void RecalculateStrokeCount()
    {
        int totalStrokes = 0, totalShapes = 0, totalTexts = 0, totalMeasurements = 0;
        foreach (var layer in _layers)
        {
            totalStrokes += layer.StrokeCount;
            totalShapes += layer.ShapeCount;
            totalTexts += layer.TextCount;
            totalMeasurements += layer.MeasurementCount;
        }
        _totalStrokeCount = totalStrokes;
        _totalShapeCount = totalShapes;
        _totalTextCount = totalTexts;
        _totalMeasurementCount = totalMeasurements;
    }

    /// <summary>
    /// Call after externally modifying the layer collection (e.g. adding a
    /// diff annotation layer) so cached counts and visuals stay in sync.
    /// </summary>
    public void NotifyLayersChanged()
    {
        RecalculateStrokeCount();
        // Ensure the active layer still belongs to the current collection.
        // When diff annotation layers are removed, _activeLayer can become
        // an orphan — strokes drawn on it would be invisible because Render
        // only iterates _layers.
        if (_activeLayer != null && !_layers.Contains(_activeLayer))
            _activeLayer = _layers.Count > 0 ? _layers[0] : null;
        InvalidateVisual();
    }

    private AnnotationLayer? _activeLayer;
    public AnnotationLayer? ActiveLayer
    {
        get => _activeLayer;
        set { _activeLayer = value; InvalidateVisual(); }
    }

    /// <summary>True when the active layer is locked and should reject mutations.</summary>
    public bool IsActiveLayerLocked => _activeLayer is { IsLocked: true };

    public Color StrokeColor { get; set; } = Color.FromRgb(214, 64, 69);
    public double StrokeWidth { get; set; } = 2;
    public double StrokeOpacity { get; set; } = 1.0;
    public bool IsHighlighterMode { get; set; }
    public InlineAnnotationTool ActiveTool { get; set; } = InlineAnnotationTool.Draw;
    public double TextFontSize { get; set; } = 10;
    /// <summary>Font family name for new text annotations.</summary>
    public string TextFontFamily { get; set; } = "";
    /// <summary>When true, new shapes are rendered with a translucent fill.</summary>
    public bool IsFilledMode { get; set; }
    /// <summary>Dash pattern for new strokes and shapes.</summary>
    public LineDashPattern StrokeDashPattern { get; set; } = LineDashPattern.Solid;
    /// <summary>Corner radius in PDF points for new Rectangle shapes and Polyline vertices. 0 = sharp.</summary>
    public double ShapeCornerRadius { get; set; }

    /// <summary>When true, all placed/dragged points snap to the grid defined by <see cref="GridSpacing"/>.</summary>
    public bool SnapToGrid { get; set; }
    /// <summary>Grid cell size in PDF points (default 10 ≈ 3.5 mm).</summary>
    public double GridSpacing { get; set; } = 10;

    // Selection highlight: the items currently selected with the Select tool
    private readonly HashSet<object> _selectHighlightItems = new();

    // Annotation grouping: each set is a group of items that move/select together
    private readonly List<HashSet<object>> _annotationGroups = new();

    /// <summary>Creates a new group from the given annotations. Items are removed from any existing groups first.</summary>
    public void GroupAnnotations(IEnumerable<object> items)
    {
        var itemSet = new HashSet<object>(items);
        if (itemSet.Count < 2) return;
        UngroupItems(itemSet);
        _annotationGroups.Add(itemSet);
        InvalidateVisual();
    }

    /// <summary>Removes the given annotations from their groups.</summary>
    public void UngroupAnnotations(IEnumerable<object> items)
    {
        UngroupItems(new HashSet<object>(items));
        InvalidateVisual();
    }

    private void UngroupItems(HashSet<object> items)
    {
        for (int i = _annotationGroups.Count - 1; i >= 0; i--)
        {
            var group = _annotationGroups[i];
            if (group.Overlaps(items))
            {
                group.ExceptWith(items);
                if (group.Count < 2)
                    _annotationGroups.RemoveAt(i);
            }
        }
    }

    /// <summary>Returns the group containing the given item, or null if it's ungrouped.</summary>
    public HashSet<object>? GetGroup(object item)
    {
        foreach (var group in _annotationGroups)
        {
            if (group.Contains(item))
                return group;
        }
        return null;
    }

    /// <summary>Removes a deleted annotation from any group it belongs to.</summary>
    private void RemoveFromGroups(object item)
    {
        for (int i = _annotationGroups.Count - 1; i >= 0; i--)
        {
            var group = _annotationGroups[i];
            if (group.Remove(item) && group.Count < 2)
                _annotationGroups.RemoveAt(i);
        }
    }
    // Hover highlight: the item under the cursor in Select mode (for outline preview)
    private object? _selectHoverItem;

    /// <summary>
    /// Millimetres per PDF point used for measurement labels.
    /// Default = 25.4/72 (uncalibrated). Set via calibration workflow.
    /// </summary>
    public double MeasurementScale { get; set; } = DefaultMeasurementScale;
    public bool IsMeasurementCalibrated => Math.Abs(MeasurementScale - DefaultMeasurementScale) > 0.0001;

    // ── Diff overlay ───────────────────────────────────────────────
    /// <summary>
    /// GPU-friendly immutable image created from the diff bitmap on load.
    /// SKImage can be cached in GPU texture memory, avoiding costly
    /// CPU→GPU pixel uploads on every frame during pan/zoom.
    /// </summary>
    private SKImage? _diffOverlayImage;
    /// <summary>Previous diff overlay image kept alive for one render cycle
    /// so a deferred DiffOverlayDrawOp on the render thread can finish safely.</summary>
    private SKImage? _prevDiffOverlayImage;
    private int _diffOverlayPage = -1;
    private float _diffImageZoom = 1f;

    /// <summary>Opacity for the diff overlay image (0..1). Default 0.55.</summary>
    public double DiffOverlayOpacity { get; set; } = 0.55;

    /// <summary>Whether the diff overlay is currently visible.</summary>
    public bool DiffOverlayVisible { get; set; }

    // ── Dark-mode inversion (applied between PDF content and annotations) ──
    /// <summary>When true, PDF content is inverted via Difference + tint before annotations are drawn.</summary>
    public bool IsInverted { get; set; }
    /// <summary>Background color whose alpha channel is preserved through inversion.</summary>
    public Color InvertBackgroundColor { get; set; } = Colors.Transparent;
    /// <summary>Tint color applied additively after inversion. Black = no tint.</summary>
    public Color InvertTintColor { get; set; } = Colors.Black;
    /// <summary>Strength of the Multiply tint pass (0–50). Default 15.</summary>
    public int InvertTintIntensity { get; set; } = 15;

    /// <summary>
    /// Sets a diff-highlight image to render as a semi-transparent overlay
    /// between the PDF page and annotations. Pass null to clear.
    /// </summary>
    /// <param name="forceReload">Skip the cache check and reload from disk (e.g. after tolerance change).</param>
    public void SetDiffOverlay(string? imagePath, int page, float zoom = 1f, bool forceReload = false)
    {
        // Fast path: skip reload if already showing the same page's overlay.
        if (!forceReload && _diffOverlayImage != null && _diffOverlayPage == page && DiffOverlayVisible)
            return;

        // Dispose the N-2 image (safe — the render thread can only reference N-1 at most).
        // Then shift current → previous so it stays alive for any in-flight draw op.
        _prevDiffOverlayImage?.Dispose();
        _prevDiffOverlayImage = _diffOverlayImage;
        _diffOverlayImage = null;
        _diffOverlayPage = page;
        _diffImageZoom = zoom;

        if (imagePath != null && File.Exists(imagePath))
        {
            using var fs = File.OpenRead(imagePath);
            using var bitmap = SKBitmap.Decode(fs);
            // Create an immutable SKImage for GPU-cached rendering.
            // SKImage.FromBitmap is cheap (shares pixel data) but allows
            // Skia to cache the texture on GPU between frames.
            // The bitmap can be disposed immediately — SKImage retains
            // a copy of the pixel data.
            _diffOverlayImage = SKImage.FromBitmap(bitmap);
            DiffOverlayVisible = true;
        }
        InvalidateVisual();
    }

    /// <summary>Clears the diff overlay image and frees resources.</summary>
    public void ClearDiffOverlay()
    {
        // Skip if already cleared to avoid unnecessary InvalidateVisual
        if (_diffOverlayImage == null && !DiffOverlayVisible) return;
        _prevDiffOverlayImage?.Dispose();
        _prevDiffOverlayImage = _diffOverlayImage;
        _diffOverlayImage = null;
        _diffOverlayPage = -1;
        DiffOverlayVisible = false;
        InvalidateVisual();
    }

    private bool HasDiffOverlay => DiffOverlayVisible && _diffOverlayImage != null
                                    && _diffOverlayPage == _currentPage;

    /// <summary>Fast check: true when any annotation content or UI chrome needs rendering.</summary>
    public bool HasAnyStrokes
    {
        get
        {
            // Fast path: check counts first (single comparison, no field chain)
            if ((_totalStrokeCount | _totalShapeCount | _totalTextCount | _totalMeasurementCount) > 0)
                return true;
            // Slow path: check transient UI state only when counts are zero
            return _activeStroke != null || _activePolyline != null
                   || _activeShape != null
                   || _activeMeasurement != null || _arrowTextPreviewOrigin != null
                   || _eraserHoverItem != null || _cursorPdfPos != null
                   || _selectHighlightItems.Count > 0 || _stickyNoteHoverItem != null
                   || _textPlacementPreviewPos != null
                   || _snapGuideX != null || _snapGuideY != null
                   || _selectHoverItem != null
                   || _rubberBandStart != null
                   || SnapToGrid;
        }
    }

    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>True if there is at least one undo entry for the current page and active layer.</summary>
    public bool CanUndoCurrentPage
    {
        get
        {
            foreach (var entry in _undoStack)
                if (entry.page == _currentPage
                    && (entry.layer == null || entry.layer == ActiveLayer))
                    return true;
            return false;
        }
    }

    /// <summary>True if there is at least one redo entry for the current page and active layer.</summary>
    public bool CanRedoCurrentPage
    {
        get
        {
            foreach (var entry in _redoStack)
                if (entry.page == _currentPage
                    && (entry.layer == null || entry.layer == ActiveLayer))
                    return true;
            return false;
        }
    }

    /// <summary>The annotation object most recently committed (shape, text, stroke, measurement).
    /// Used by the view layer to auto-select newly created annotations.</summary>
    public object? LastPlacedAnnotation { get; private set; }

    /// <summary>Returns all annotation objects on the current page across all visible layers.</summary>
    public List<object> GetAllAnnotationsOnPage()
    {
        var result = new List<object>();
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageStrokes.TryGetValue(_currentPage, out var s)) result.AddRange(s);
            if (layer.PageShapes.TryGetValue(_currentPage, out var sh)) result.AddRange(sh);
            if (layer.PageTexts.TryGetValue(_currentPage, out var t)) result.AddRange(t);
            if (layer.PageMeasurements.TryGetValue(_currentPage, out var m)) result.AddRange(m);
        }
        return result;
    }

    /// <summary>Total annotation count on the current page across all visible layers.</summary>
    public int CurrentPageAnnotationCount
    {
        get
        {
            int count = 0;
            foreach (var layer in Layers)
            {
                if (!layer.IsVisible) continue;
                if (layer.PageStrokes.TryGetValue(_currentPage, out var s)) count += s.Count;
                if (layer.PageShapes.TryGetValue(_currentPage, out var sh)) count += sh.Count;
                if (layer.PageTexts.TryGetValue(_currentPage, out var t)) count += t.Count;
                if (layer.PageMeasurements.TryGetValue(_currentPage, out var m)) count += m.Count;
            }
            return count;
        }
    }

    public void SetStrokePage(int page)
    {
        if (page == _currentPage) return;
        _activeStroke = null;
        _activeShape = null;
        _activeMeasurement = null;
        _measurementPreviewPoint = null;
        _currentPage = page;
        if (IsViewerInitialized)
            InvalidateVisual();
    }

    #region Layer Management

    public AnnotationLayer AddLayer(string name, Color color)
    {
        var layer = new AnnotationLayer { Name = name, Color = color };
        Layers.Add(layer);
        ActiveLayer = layer;
        return layer;
    }

    public void RemoveLayer(AnnotationLayer layer)
    {
        _totalStrokeCount = Math.Max(0, _totalStrokeCount - layer.StrokeCount);
        _totalShapeCount = Math.Max(0, _totalShapeCount - layer.ShapeCount);
        _totalTextCount = Math.Max(0, _totalTextCount - layer.TextCount);
        _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - layer.MeasurementCount);
        Layers.Remove(layer);
        if (ActiveLayer == layer)
            ActiveLayer = Layers.Count > 0 ? Layers[0] : null;
        PurgeEntriesForLayer(layer);
        InvalidateVisual();
        NotifyAnnotationChanged();
    }

    public bool HasInconsistentMeasurementScales()
    {
        foreach (var layer in Layers)
        {
            foreach (var list in layer.PageMeasurements.Values)
                foreach (var m in list)
                    if (Math.Abs(m.Scale - MeasurementScale) > 0.0001)
                        return true;

            foreach (var list in layer.PageStrokes.Values)
                foreach (var s in list)
                    if (s.IsAreaMeasure && Math.Abs(s.AreaScale - MeasurementScale) > 0.0001)
                        return true;
        }
        return false;
    }

    public void NormalizeMeasurementScales()
    {
        foreach (var layer in Layers)
        {
            foreach (var list in layer.PageMeasurements.Values)
                foreach (var m in list)
                    m.Scale = MeasurementScale;
            foreach (var list in layer.PageStrokes.Values)
                foreach (var s in list)
                    if (s.IsAreaMeasure)
                    {
                        s.AreaScale = MeasurementScale;
                        s.InvalidatePen();
                    }
        }
        InvalidateVisual();
        NotifyAnnotationChanged();
    }

    /// <summary>Removes all undo/redo entries that reference a specific layer.</summary>
    private void PurgeEntriesForLayer(AnnotationLayer layer)
    {
        PurgeStack(_undoStack, layer);
        PurgeRedoStack(_redoStack, layer);
    }

    private static void PurgeStack(
        Stack<(UndoType type, int page, object? data, AnnotationLayer? layer)> stack,
        AnnotationLayer target)
    {
        if (stack.Count == 0) return;
        var keep = new Stack<(UndoType, int, object?, AnnotationLayer?)>();
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.layer != target) keep.Push(entry);
        }
        while (keep.Count > 0) stack.Push(keep.Pop());
    }

    private static void PurgeRedoStack(
        Stack<(UndoType type, int page, object item, AnnotationLayer? layer)> stack,
        AnnotationLayer target)
    {
        if (stack.Count == 0) return;
        var keep = new Stack<(UndoType, int, object, AnnotationLayer?)>();
        while (stack.Count > 0)
        {
            var entry = stack.Pop();
            if (entry.layer != target) keep.Push(entry);
        }
        while (keep.Count > 0) stack.Push(keep.Pop());
    }

    public void ClearLayer(AnnotationLayer layer)
    {
        _totalStrokeCount = Math.Max(0, _totalStrokeCount - layer.StrokeCount);
        _totalShapeCount = Math.Max(0, _totalShapeCount - layer.ShapeCount);
        _totalTextCount = Math.Max(0, _totalTextCount - layer.TextCount);
        _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - layer.MeasurementCount);
        layer.PageStrokes.Clear();
        layer.PageShapes.Clear();
        layer.PageTexts.Clear();
        layer.PageMeasurements.Clear();
        layer.StrokeCount = 0;
        layer.ShapeCount = 0;
        layer.TextCount = 0;
        layer.MeasurementCount = 0;
        layer.RefreshStatus();
        InvalidateVisual();
        NotifyAnnotationChanged();
    }

    /// <summary>Ensure at least one layer exists; create the default if empty.</summary>
    public void EnsureDefaultLayer()
    {
        if (Layers.Count == 0)
            AddLayer("Layer 1", Color.FromRgb(214, 64, 69));
    }

    /// <summary>
    /// Extracts the PDF's built-in outline (table of contents) and returns a flat list
    /// of (page number, title) tuples. Nested items are flattened recursively.
    /// Returns an empty list when no outline is present or no document is loaded.
    /// </summary>
    public List<(int Page, string Title)> GetOutlineBookmarks()
    {
        var result = new List<(int Page, string Title)>();
        if (Document == null) return result;

        try
        {
            var outline = Document.Outline;
            if (outline == null) return result;
            FlattenOutline(outline, result);
        }
        catch { /* outline unavailable */ }

        return result;
    }

    private static void FlattenOutline(IEnumerable<MuPDFCore.MuPDFOutlineItem> items, List<(int Page, string Title)> result)
    {
        foreach (var item in items)
        {
            if (item.Page >= 0 && !string.IsNullOrWhiteSpace(item.Title))
                result.Add((item.Page, item.Title));
            if (item.Children != null)
                FlattenOutline(item.Children, result);
        }
    }

    #endregion

    /// <summary>
    /// Returns a cached SKTypeface for the given family name.
    /// Avoids repeated expensive native SKTypeface.FromFamilyName lookups.
    /// </summary>
    private static SKTypeface GetCachedTypeface(string fontFamily)
    {
        if (string.IsNullOrEmpty(fontFamily)) return SKTypeface.Default;
        return _typefaceCache.GetOrAdd(fontFamily,
            f => SKTypeface.FromFamilyName(f) ?? SKTypeface.Default);
    }

    #region Coordinate Transform

    /// <summary>
    /// When Shift is held, constrain the endpoint so the line from
    /// <paramref name="origin"/> snaps to the nearest 0°/45°/90° axis.
    /// For rectangles/ellipses this produces perfect squares/circles.
    /// </summary>
    /// <summary>
    /// When Shift is held on lines/arrows, constrain the endpoint so the line
    /// snaps to the nearest 15° increment (0°, 15°, 30°, 45°, …).
    /// </summary>
    internal static Point ConstrainToFineAngle(Point origin, Point end)
    {
        double dx = end.X - origin.X;
        double dy = end.Y - origin.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return end;

        double angle = Math.Atan2(dy, dx);
        double snapped = Math.Round(angle / (Math.PI / 12)) * (Math.PI / 12);
        return new Point(
            origin.X + len * Math.Cos(snapped),
            origin.Y + len * Math.Sin(snapped));
    }

    /// <summary>
    /// Constrains a shape endpoint so rectangle/ellipse becomes square/circle.
    /// </summary>
    internal static Point ConstrainToSquare(Point origin, Point end)
    {
        double dx = end.X - origin.X;
        double dy = end.Y - origin.Y;
        double side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new Point(
            origin.X + Math.Sign(dx) * side,
            origin.Y + Math.Sign(dy) * side);
    }

    /// <summary>
    /// Soft magnetic snap: when the line is within ~7° of a 0°/45°/90° axis,
    /// snap to that axis. Returns the original point if not near any axis.
    /// </summary>
    private static Point MagneticSnap(Point origin, Point end, double thresholdDeg = 7.0)
    {
        double dx = end.X - origin.X;
        double dy = end.Y - origin.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 3) return end;

        double angle = Math.Atan2(dy, dx);
        // Snap to nearest 15° increment for finer magnetic attraction
        double snapped = Math.Round(angle / (Math.PI / 12)) * (Math.PI / 12);
        double diff = Math.Abs(angle - snapped);
        if (diff <= thresholdDeg * Math.PI / 180.0)
            return new Point(origin.X + len * Math.Cos(snapped), origin.Y + len * Math.Sin(snapped));
        return end;
    }

    public Point? ScreenToPdf(Point screenPos)
    {
        if (!IsViewerInitialized) return null;

        var da = DisplayArea;
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0 ||
            da.Width  <= 0 || da.Height <= 0) return null;

        return new Point(
            screenPos.X / bounds.Width  * da.Width  + da.X,
            screenPos.Y / bounds.Height * da.Height + da.Y);
    }

    private Point PdfToScreen(Point pdfPos, Rect da, Size boundsSize)
    {
        return new Point(
            (pdfPos.X - da.X) / da.Width  * boundsSize.Width,
            (pdfPos.Y - da.Y) / da.Height * boundsSize.Height);
    }

    /// <summary>Fast overload using precomputed scale/offset (avoids per-call division).</summary>
    private static Point PdfToScreen(Point pdfPos, double offsetX, double offsetY, double scaleX, double scaleY)
        => new((pdfPos.X - offsetX) * scaleX, (pdfPos.Y - offsetY) * scaleY);

    /// <summary>Converts a screen-pixel distance to PDF-unit distance at the current zoom level.
    /// Use for zoom-adaptive hit-test radii so handles stay a constant screen size.</summary>
    public double ScreenToPdfDistance(double screenPixels)
    {
        var da = DisplayArea;
        var bounds = Bounds;
        if (bounds.Width <= 0 || da.Width <= 0) return screenPixels;
        return screenPixels * da.Width / bounds.Width;
    }

    #endregion

    #region Drawing API

    public void BeginStroke(Point pdfPoint)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        _activeStroke = new InkStroke
        {
            Color = StrokeColor,
            Width = StrokeWidth,
            Opacity = StrokeOpacity,
            IsHighlighter = IsHighlighterMode,
            DashPattern = StrokeDashPattern
        };
        _activeStroke.Points.Add(pdfPoint);
    }

    // Minimum distance in screen pixels between consecutive captured points.
    // Converted to PDF units at the current zoom level so the threshold
    // adapts: zoomed-in captures finer detail, zoomed-out rejects more jitter.
    private const double MinScreenPixels = 3.0;

    public void AddPoint(Point pdfPoint)
    {
        if (_activeStroke == null) return;
        var pts = _activeStroke.Points;
        if (pts.Count > 0)
        {
            var last = pts[^1];
            double dx = pdfPoint.X - last.X;
            double dy = pdfPoint.Y - last.Y;
            double distSq = dx * dx + dy * dy;

            // Convert screen-pixel threshold to PDF units using current zoom
            var da = DisplayArea;
            var bounds = Bounds;
            double pdfPerPx = bounds.Width > 0 ? da.Width / bounds.Width : 1.0;
            double minPdf = MinScreenPixels * pdfPerPx;
            if (distSq < minPdf * minPdf) return;
        }
        pts.Add(pdfPoint);
        InvalidateVisual();
    }

    public void EndStroke()
    {
        if (_activeStroke != null && _activeStroke.Points.Count > 1 && ActiveLayer != null)
        {
            // Smooth the raw points before committing to reduce jaggedness
            _activeStroke.Points = SmoothPoints(_activeStroke.Points);

            if (!ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            {
                strokes = [];
                ActiveLayer.PageStrokes[_currentPage] = strokes;
            }
            strokes.Add(_activeStroke);
            ActiveLayer.StrokeCount++;
            _totalStrokeCount++;
            _undoStack.Push((UndoType.Stroke, _currentPage, null, ActiveLayer));
            _redoStack.Clear();
            LastPlacedAnnotation = _activeStroke;
            ActiveLayer.RefreshStatus();
            NotifyAnnotationChanged();
        }
        _activeStroke = null;
        InvalidateVisual();
    }

    // ── Polyline (multi-click straight-line segments) ─────────────────

    public void BeginPolyline(Point pdfPoint, bool asAreaMeasure = false)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        pdfPoint = ComputeVertexSnap(null!, pdfPoint);
        _activePolyline = new InkStroke
        {
            Color = StrokeColor,
            Width = StrokeWidth,
            Opacity = StrokeOpacity,
            IsPolyline = true,
            DashPattern = asAreaMeasure ? LineDashPattern.Dashed : StrokeDashPattern,
            CornerRadius = ShapeCornerRadius,
            IsAreaMeasure = asAreaMeasure,
            IsFilled = asAreaMeasure,
            AreaScale = MeasurementScale
        };
        _activePolyline.Points.Add(pdfPoint);
        _polylinePreviewEnd = pdfPoint;
        InvalidateVisual();
    }

    public void AddPolylinePoint(Point pdfPoint, bool constrainAxis = false)
    {
        if (_activePolyline == null) return;
        if (constrainAxis && _activePolyline.Points.Count > 0)
            pdfPoint = ConstrainToFineAngle(_activePolyline.Points[^1], pdfPoint);
        else
            pdfPoint = ComputeVertexSnap(_activePolyline, pdfPoint);
        _activePolyline.Points.Add(pdfPoint);
        _polylinePreviewEnd = pdfPoint;
        InvalidateVisual();
    }

    public void UpdatePolylinePreview(Point pdfPoint, bool constrainAxis = false)
    {
        if (constrainAxis && _activePolyline != null && _activePolyline.Points.Count > 0)
            pdfPoint = ConstrainToFineAngle(_activePolyline.Points[^1], pdfPoint);
        else if (_activePolyline != null)
            pdfPoint = ComputeVertexSnap(_activePolyline, pdfPoint);
        _polylinePreviewEnd = pdfPoint;
        // Polar tracking: distance and angle from the last committed point
        if (_activePolyline != null && _activePolyline.Points.Count > 0)
            ComputePolar(_activePolyline.Points[^1], pdfPoint);
        else
            _polarAngleDeg = _polarDistancePdf = null;
        InvalidateVisual();
    }

    /// <summary>Removes the last committed point from the active polyline, used to discard
    /// the spurious point added by the first click of a double-click finish.</summary>
    public void RemoveLastPolylinePoint()
    {
        if (_activePolyline == null || _activePolyline.Points.Count <= 1) return;
        _activePolyline.Points.RemoveAt(_activePolyline.Points.Count - 1);
        _activePolyline.InvalidateGeometry();
    }

    public void EndPolyline(bool close = false)
    {
        if (_activePolyline != null && _activePolyline.Points.Count >= 2 && ActiveLayer != null)
        {
            // Area-measure polylines are always closed shapes.
            if (_activePolyline.IsAreaMeasure && _activePolyline.Points.Count >= 3)
                close = true;
            if (close && _activePolyline.Points.Count >= 3)
            {
                // Just mark as closed — the geometry builder uses EndFigure(true) which
                // draws the closing segment automatically. Adding first point to the list
                // would create a duplicate node on top of Points[0].
                _activePolyline.IsClosed = true;
                // Area-measure strokes are always filled when closed.
                if (_activePolyline.IsAreaMeasure)
                    _activePolyline.IsFilled = true;
            }
            if (!ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            {
                strokes = [];
                ActiveLayer.PageStrokes[_currentPage] = strokes;
            }
            strokes.Add(_activePolyline);
            ActiveLayer.StrokeCount++;
            _totalStrokeCount++;
            _undoStack.Push((UndoType.Stroke, _currentPage, null, ActiveLayer));
            _redoStack.Clear();
            LastPlacedAnnotation = _activePolyline;
            ActiveLayer.RefreshStatus();
            NotifyAnnotationChanged();
        }
        _activePolyline = null;
        _polylinePreviewEnd = null;
        _snapGuideX = null;
        _snapGuideY = null;
        _snapVertexPos = null;
        _polarAngleDeg = null; _polarDistancePdf = null; _polarCursorScreen = null;
        InvalidateVisual();
    }

    /// <summary>Toggles the IsClosed state of an existing polyline.</summary>
    public void TogglePolylineClosed(InkStroke polyline)
    {
        if (!polyline.IsPolyline || polyline.Points.Count < 3) return;
        if (polyline.IsClosed)
        {
            // Open: remove any legacy duplicate closing point (last == first) that may
            // have been stored before the no-duplicate-point policy was introduced.
            var first = polyline.Points[0];
            var last = polyline.Points[^1];
            if (Math.Abs(first.X - last.X) < 0.5 && Math.Abs(first.Y - last.Y) < 0.5
                && polyline.Points.Count > 2)
                polyline.Points.RemoveAt(polyline.Points.Count - 1);
            polyline.IsClosed = false;
        }
        else
        {
            // Close: just mark as closed. The geometry builder uses EndFigure(true) which draws
            // the closing segment automatically. Adding the first point would create a duplicate
            // node that appears when vertices are moved.
            polyline.IsClosed = true;
        }
        polyline.InvalidatePen();
        InvalidateVisual();
        NotifyAnnotationChanged();
    }

    public void CancelPolyline()
    {
        _activePolyline = null;
        _polylinePreviewEnd = null;
        _snapGuideX = null;
        _snapGuideY = null;
        _snapVertexPos = null;
        InvalidateVisual();
    }

    public bool HasActivePolyline => _activePolyline != null;
    public int ActivePolylinePointCount => _activePolyline?.Points.Count ?? 0;
    public bool IsActivePolylineAreaMeasure => _activePolyline?.IsAreaMeasure ?? false;

    public bool TryGetActivePolylineStart(out Point start)
    {
        if (_activePolyline != null && _activePolyline.Points.Count > 0)
        {
            start = _activePolyline.Points[0];
            return true;
        }
        start = default;
        return false;
    }

    public bool ShouldAutoCloseActivePolyline(Point pdfPoint, double threshold)
    {
        if (_activePolyline == null || _activePolyline.Points.Count < 3) return false;
        var start = _activePolyline.Points[0];
        double dx = pdfPoint.X - start.X;
        double dy = pdfPoint.Y - start.Y;
        return dx * dx + dy * dy <= threshold * threshold;
    }

    public bool HasActiveShape => _activeShape != null;
    public bool HasActiveMeasurement => _activeMeasurement != null;

    public (int FixedCount, int RemovedCount) ValidateAndRepairAnnotations()
    {
        int fixedCount = 0;
        int removedCount = 0;
        bool changedAny = false;

        foreach (var layer in Layers)
        {
            foreach (var kv in layer.PageStrokes)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var s = list[i];
                    bool changed = false;

                    if (s.IsPolyline && s.Points.Count > 1)
                    {
                        // Remove consecutive duplicate vertices.
                        for (int p = s.Points.Count - 1; p >= 1; p--)
                        {
                            if (NearlySamePoint(s.Points[p], s.Points[p - 1]))
                            {
                                s.Points.RemoveAt(p);
                                changed = true;
                            }
                        }

                        // Remove legacy duplicated closing point (last == first).
                        if (s.Points.Count > 2 && NearlySamePoint(s.Points[0], s.Points[^1]))
                        {
                            s.Points.RemoveAt(s.Points.Count - 1);
                            changed = true;
                        }

                        // Closed polyline needs at least 3 vertices.
                        if (s.IsClosed && s.Points.Count < 3)
                        {
                            s.IsClosed = false;
                            s.IsFilled = false;
                            changed = true;
                        }

                        // Area-measure polyline must be closed and filled when valid.
                        if (s.IsAreaMeasure && s.Points.Count >= 3 && (!s.IsClosed || !s.IsFilled))
                        {
                            s.IsClosed = true;
                            s.IsFilled = true;
                            changed = true;
                        }
                    }

                    if (s.Points.Count < 2)
                    {
                        list.RemoveAt(i);
                        removedCount++;
                        changedAny = true;
                        continue;
                    }

                    if (changed)
                    {
                        s.InvalidatePen();
                        fixedCount++;
                        changedAny = true;
                    }
                }
            }

            foreach (var kv in layer.PageShapes)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var sh = list[i];
                    bool remove = false;
                    switch (sh.ShapeType)
                    {
                        case InlineAnnotationTool.Line:
                        case InlineAnnotationTool.Arrow:
                            remove = NearlySamePoint(sh.Start, sh.End);
                            break;
                        case InlineAnnotationTool.Rectangle:
                        case InlineAnnotationTool.Ellipse:
                        case InlineAnnotationTool.RevisionCloud:
                            remove = Math.Abs(sh.End.X - sh.Start.X) < 0.01
                                || Math.Abs(sh.End.Y - sh.Start.Y) < 0.01;
                            break;
                    }

                    if (remove)
                    {
                        list.RemoveAt(i);
                        removedCount++;
                        changedAny = true;
                    }
                }
            }

            foreach (var kv in layer.PageMeasurements)
            {
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var m = list[i];
                    if (m.Points.Count < 2 || NearlySamePoint(m.Points[0], m.Points[1]))
                    {
                        list.RemoveAt(i);
                        removedCount++;
                        changedAny = true;
                        continue;
                    }

                    if (m.Scale <= 0)
                    {
                        m.Scale = MeasurementScale;
                        fixedCount++;
                        changedAny = true;
                    }
                }
            }
        }

        if (changedAny)
        {
            foreach (var layer in Layers)
                layer.RecalculateCounts();
            RecalculateStrokeCount();
            InvalidateVisual();
            NotifyAnnotationChanged();
        }

        return (fixedCount, removedCount);
    }

    /// <summary>
    /// Two-pass Chaikin corner-cutting subdivision to produce a smooth curve
    /// from raw freehand input. Each pass inserts midpoints at 25%/75%
    /// between consecutive points, rounding off sharp corners while keeping
    /// the stroke close to the original path.
    /// </summary>
    private static List<Point> SmoothPoints(List<Point> raw)
    {
        if (raw.Count < 3) return raw;

        var pts = raw;
        for (int pass = 0; pass < 2; pass++)
        {
            var smooth = new List<Point>(pts.Count * 2) { pts[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[i];
                var p1 = pts[i + 1];
                smooth.Add(new Point(
                    p0.X * 0.75 + p1.X * 0.25,
                    p0.Y * 0.75 + p1.Y * 0.25));
                smooth.Add(new Point(
                    p0.X * 0.25 + p1.X * 0.75,
                    p0.Y * 0.25 + p1.Y * 0.75));
            }
            smooth.Add(pts[^1]);
            pts = smooth;
        }
        return pts;
    }

    public void CancelStroke()
    {
        _activeStroke = null;
        _activeShape = null;
        _activeMeasurement = null;
        _measurementPreviewPoint = null;
        _activePolyline = null;
        _polylinePreviewEnd = null;
        _rubberBandStart = null;
        _rubberBandEnd = null;
        _snapGuideX = null;
        _snapGuideY = null;
        _snapVertexPos = null;
        InvalidateVisual();
    }

    #endregion

    #region Shape API

    public void BeginShape(Point pdfPoint)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        pdfPoint = ComputeVertexSnap(null!, pdfPoint);
        _activeShape = new ShapeAnnotation
        {
            ShapeType = ActiveTool,
            Start = pdfPoint,
            End = pdfPoint,
            Color = StrokeColor,
            StrokeWidth = StrokeWidth,
            Opacity = StrokeOpacity,
            IsFilled = IsFilledMode,
            DashPattern = StrokeDashPattern,
            CornerRadius = ShapeCornerRadius
        };
        InvalidateVisual();
    }

    public void UpdateShape(Point pdfPoint, bool constrainAxis = false)
    {
        if (_activeShape == null) return;
        if (constrainAxis)
        {
            pdfPoint = _activeShape.ShapeType is InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse or InlineAnnotationTool.RevisionCloud
                ? ConstrainToSquare(_activeShape.Start, pdfPoint)
                : ConstrainToFineAngle(_activeShape.Start, pdfPoint);
        }
        else
        {
            pdfPoint = ComputeVertexSnap(_activeShape, pdfPoint);
        }
        _activeShape.End = pdfPoint;
        // Polar tracking for line/arrow
        if (_activeShape.ShapeType is InlineAnnotationTool.Line or InlineAnnotationTool.Arrow
                                   or InlineAnnotationTool.MeasureDistance)
            ComputePolar(_activeShape.Start, pdfPoint);
        else
            _polarAngleDeg = _polarDistancePdf = null;
        InvalidateVisual();
    }

    public void EndShape()
    {
        if (_activeShape != null && ActiveLayer != null)
        {
            var s = _activeShape;
            double dx = s.End.X - s.Start.X;
            double dy = s.End.Y - s.Start.Y;
            // Only commit if the shape has a meaningful size
            if (dx * dx + dy * dy > 4)
            {
                if (!ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes))
                {
                    shapes = [];
                    ActiveLayer.PageShapes[_currentPage] = shapes;
                }
                shapes.Add(_activeShape);
                ActiveLayer.ShapeCount++;
                _totalShapeCount++;
                _undoStack.Push((UndoType.Shape, _currentPage, null, ActiveLayer));
                _redoStack.Clear();
                LastPlacedAnnotation = _activeShape;
                ActiveLayer.RefreshStatus();
                NotifyAnnotationChanged();
            }
        }
        _activeShape = null;
        _snapGuideX = null;
        _snapGuideY = null;
        _snapVertexPos = null;
        _polarAngleDeg = null; _polarDistancePdf = null; _polarCursorScreen = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Places a filled dot (circle) at the given PDF-space point.
    /// Stored as a ShapeAnnotation with ShapeType == Dot and Start == End.
    /// </summary>
    public void PlaceDot(Point pdfPoint)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        pdfPoint = ComputeVertexSnap(null!, pdfPoint);
        var dot = new ShapeAnnotation
        {
            ShapeType = InlineAnnotationTool.Dot,
            Start = pdfPoint,
            End = pdfPoint,
            Color = StrokeColor,
            StrokeWidth = StrokeWidth,
            Opacity = StrokeOpacity,
            IsFilled = true
        };
        if (!ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes))
        {
            shapes = [];
            ActiveLayer.PageShapes[_currentPage] = shapes;
        }
        shapes.Add(dot);
        ActiveLayer.ShapeCount++;
        _totalShapeCount++;
        _undoStack.Push((UndoType.Shape, _currentPage, null, ActiveLayer));
        _redoStack.Clear();
        LastPlacedAnnotation = dot;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        _snapGuideX = null;
        _snapGuideY = null;
        _snapVertexPos = null;
        InvalidateVisual();
    }

    #endregion

    /// <summary>
    /// Preview state: when set, draws an arrow from this anchor to the cursor
    /// while the user is placing an ArrowText annotation (first click done).
    /// </summary>
    private Point? _arrowTextPreviewOrigin;

    public void SetArrowTextPreview(Point origin)
    {
        _arrowTextPreviewOrigin = origin;
        InvalidateVisual();
    }

    public void ClearArrowTextPreview()
    {
        _arrowTextPreviewOrigin = null;
        _lastPointerPdfPos = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Update the last known pointer position (PDF-space) for ArrowText preview line.
    /// </summary>
    public void UpdateArrowTextPreview(Point pdfPos)
    {
        if (_arrowTextPreviewOrigin == null) return;
        _lastPointerPdfPos = pdfPos;
        InvalidateVisual();
    }

    /// <summary>Default max width (PDF units) for new text annotations. 0 = no wrapping.</summary>
    public double TextMaxWidth { get; set; } = 150;

    /// <summary>
    /// Measures the text annotation content and shrinks <see cref="TextAnnotation.MaxWidth"/>
    /// to the minimum needed to fit the longest explicit line, so there is no excess whitespace
    /// to the right of the text. Never expands beyond the current MaxWidth so word-wrapping
    /// set at placement time is always preserved.
    /// </summary>
    internal void AutoSizeTextWidth(TextAnnotation t)
    {
        if (t == null || string.IsNullOrWhiteSpace(t.Text)) return;
        if (t.IsStickyNote || t.MaxWidth <= 0) return;

        var typeface = GetCachedTypeface(t.FontFamily ?? "");
        using var skFont = new SKFont(typeface, (float)t.FontSize);

        double maxLineWidth = 0;
        foreach (var paragraph in t.Text.Split('\n'))
        {
            if (string.IsNullOrEmpty(paragraph)) continue;
            double lineW = skFont.MeasureText(paragraph);
            if (lineW > maxLineWidth)
                maxLineWidth = lineW;
        }

        const double padding = 4;
        double needed = maxLineWidth + padding;

        // Only shrink — never expand beyond the intended wrap boundary.
        if (needed < t.MaxWidth)
            t.MaxWidth = needed;
    }

    public void PlaceText(Point pdfPoint, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;

        var annotation = new TextAnnotation
        {
            Position = pdfPoint,
            Text = text,
            FontSize = TextFontSize,
            Color = StrokeColor,
            Opacity = StrokeOpacity,
            FontFamily = TextFontFamily,
            MaxWidth = TextMaxWidth
        };
        AutoSizeTextWidth(annotation);

        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
        {
            texts = [];
            ActiveLayer.PageTexts[_currentPage] = texts;
        }
        texts.Add(annotation);
        ActiveLayer.TextCount++;
        _totalTextCount++;
        _undoStack.Push((UndoType.Text, _currentPage, null, ActiveLayer));
        _redoStack.Clear();
        LastPlacedAnnotation = annotation;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    public void PlaceStickyNote(Point pdfPoint, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;

        var annotation = new TextAnnotation
        {
            Position = pdfPoint,
            Text = text,
            FontSize = TextFontSize,
            Color = Color.FromRgb(255, 235, 59),  // always notepad yellow
            Opacity = StrokeOpacity,
            FontFamily = TextFontFamily,
            IsStickyNote = true
        };

        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
        {
            texts = [];
            ActiveLayer.PageTexts[_currentPage] = texts;
        }
        texts.Add(annotation);
        ActiveLayer.TextCount++;
        _totalTextCount++;
        _undoStack.Push((UndoType.Text, _currentPage, null, ActiveLayer));
        _redoStack.Clear();
        LastPlacedAnnotation = annotation;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    public void PlaceArrowText(Point arrowOrigin, Point textPosition, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;

        var annotation = new TextAnnotation
        {
            Position = textPosition,
            Text = text,
            FontSize = TextFontSize,
            Color = StrokeColor,
            Opacity = StrokeOpacity,
            FontFamily = TextFontFamily,
            ArrowOrigin = arrowOrigin,
            MaxWidth = TextMaxWidth
        };
        AutoSizeTextWidth(annotation);

        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
        {
            texts = [];
            ActiveLayer.PageTexts[_currentPage] = texts;
        }
        texts.Add(annotation);
        ActiveLayer.TextCount++;
        _totalTextCount++;
        _undoStack.Push((UndoType.Text, _currentPage, null, ActiveLayer));
        _redoStack.Clear();
        LastPlacedAnnotation = annotation;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        ClearArrowTextPreview();
    }

    /// <summary>Place a pre-built shape annotation (used by paste).</summary>
    public void PlaceShape(ShapeAnnotation shape)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        if (!ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes))
        { shapes = []; ActiveLayer.PageShapes[_currentPage] = shapes; }
        shapes.Add(shape);
        ActiveLayer.ShapeCount++;
        _totalShapeCount++;
        if (_groupAddActive) { _groupAddItems?.Add(shape); }
        else { _undoStack.Push((UndoType.Shape, _currentPage, null, ActiveLayer)); _redoStack.Clear(); }
        LastPlacedAnnotation = shape;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    /// <summary>Place a pre-built ink stroke (used by paste).</summary>
    public void PlaceStroke(InkStroke stroke)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        if (!ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes))
        { strokes = []; ActiveLayer.PageStrokes[_currentPage] = strokes; }
        strokes.Add(stroke);
        ActiveLayer.StrokeCount++;
        _totalStrokeCount++;
        if (_groupAddActive) { _groupAddItems?.Add(stroke); }
        else { _undoStack.Push((UndoType.Stroke, _currentPage, null, ActiveLayer)); _redoStack.Clear(); }
        LastPlacedAnnotation = stroke;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    /// <summary>Place a pre-built measurement (used by paste).</summary>
    public void PlaceMeasurement(MeasurementAnnotation measurement)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        if (!ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var ms))
        { ms = []; ActiveLayer.PageMeasurements[_currentPage] = ms; }
        ms.Add(measurement);
        ActiveLayer.MeasurementCount++;
        _totalMeasurementCount++;
        if (_groupAddActive) { _groupAddItems?.Add(measurement); }
        else { _undoStack.Push((UndoType.Measurement, _currentPage, null, ActiveLayer)); _redoStack.Clear(); }
        LastPlacedAnnotation = measurement;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    /// <summary>Place a pre-built text annotation (used by paste to preserve all style properties).</summary>
    public void PlaceTextAnnotation(TextAnnotation annotation)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
        { texts = []; ActiveLayer.PageTexts[_currentPage] = texts; }
        texts.Add(annotation);
        ActiveLayer.TextCount++;
        _totalTextCount++;
        if (_groupAddActive) { _groupAddItems?.Add(annotation); }
        else { _undoStack.Push((UndoType.Text, _currentPage, null, ActiveLayer)); _redoStack.Clear(); }
        LastPlacedAnnotation = annotation;
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    /// <summary>
    /// Begins a group-add batch. All <c>Place*</c> calls until <see cref="EndGroupAdd"/> are
    /// collected into a single <see cref="UndoType.GroupAdd"/> undo entry so that Ctrl+Z
    /// removes all pasted items at once.
    /// </summary>
    public void BeginGroupAdd()
    {
        _groupAddActive = true;
        _groupAddItems = [];
    }

    /// <summary>
    /// Ends a group-add batch and pushes a single undo entry for all items collected since
    /// <see cref="BeginGroupAdd"/>. Does nothing if no items were placed.
    /// </summary>
    public void EndGroupAdd()
    {
        _groupAddActive = false;
        var items = _groupAddItems;
        _groupAddItems = null;
        if (items == null || items.Count == 0) return;
        _undoStack.Push((UndoType.GroupAdd, _currentPage, new GroupAddSnapshot(items), ActiveLayer));
        _redoStack.Clear();
    }

    /// <summary>
    /// Find a non-sticky text annotation at the given PDF-space point (for edit-on-click).
    /// Searches the active layer on the current page, topmost first.
    /// </summary>
    public TextAnnotation? FindTextAt(Point pdfPoint)
    {
        if (ActiveLayer == null) return null;
        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts)) return null;
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            if (!texts[i].IsStickyNote && HitTestText(texts[i], pdfPoint))
                return texts[i];
        }
        return null;
    }

    /// <summary>
    /// Find a sticky note annotation at the given PDF-space point.
    /// Searches the active layer on the current page, topmost first.
    /// </summary>
    public TextAnnotation? FindStickyNoteAt(Point pdfPoint)
    {
        if (ActiveLayer == null) return null;
        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts)) return null;
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            if (texts[i].IsStickyNote && HitTestStickyNote(texts[i], pdfPoint))
                return texts[i];
        }
        return null;
    }

    private static bool HitTestStickyNote(TextAnnotation t, Point pt)
    {
        // The icon occupies a 13×13 PDF-unit square at the placement position
        const double iconSize = 13.0;
        const double pad = 3.0;
        return new Rect(t.Position.X - pad, t.Position.Y - pad,
                        iconSize + pad * 2, iconSize + pad * 2).Contains(pt);
    }

    /// <summary>
    /// Find an ArrowText annotation whose arrow origin (tip) is near the given point.
    /// Returns null if no arrow tip is close enough.
    /// </summary>
    public TextAnnotation? FindArrowOriginAt(Point pdfPoint, double threshold = 8.0)
    {
        if (ActiveLayer == null) return null;
        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts)) return null;
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var t = texts[i];
            if (!t.ArrowOrigin.HasValue) continue;
            double dx = pdfPoint.X - t.ArrowOrigin.Value.X;
            double dy = pdfPoint.Y - t.ArrowOrigin.Value.Y;
            if (dx * dx + dy * dy <= threshold * threshold)
                return t;
        }
        return null;
    }

    #region Measurement API

    public void BeginMeasurement(Point pdfPoint)
    {
        if (IsActiveLayerLocked) return;
        EnsureDefaultLayer();
        pdfPoint = ComputeVertexSnap(null!, pdfPoint);
        _activeMeasurement = new MeasurementAnnotation
        {
            Color = Color.FromRgb(214, 64, 69),
            Scale = MeasurementScale,
            Points = [pdfPoint, pdfPoint]
        };
        InvalidateVisual();
    }

    public void UpdateMeasurementPreview(Point pdfPoint, bool constrainAxis = false)
    {
        _measurementPreviewPoint = pdfPoint;
        if (_activeMeasurement != null && _activeMeasurement.Points.Count >= 2)
        {
            Point target;
            if (constrainAxis)
                target = ConstrainToFineAngle(_activeMeasurement.Points[0], pdfPoint);
            else
                target = ComputeVertexSnap(_activeMeasurement, pdfPoint);
            _activeMeasurement.Points[^1] = target;
            ComputePolar(_activeMeasurement.Points[0], target);
        }
        else
        {
            _polarAngleDeg = _polarDistancePdf = null;
        }
        InvalidateVisual();
    }

    /// <summary>Stores the current screen-space cursor position so the polar readout
    /// label can be placed near the cursor during drawing.</summary>
    public void SetPolarCursorScreen(Point screenPt) => _polarCursorScreen = screenPt;

    private void ComputePolar(Point origin, Point end)
    {
        double dx = end.X - origin.X;
        double dy = end.Y - origin.Y;
        _polarDistancePdf = Math.Sqrt(dx * dx + dy * dy);
        // 0° = right, positive = counter-clockwise (negate PDF's downward Y)
        double angleDeg = Math.Atan2(-dy, dx) * 180.0 / Math.PI;
        if (angleDeg < 0) angleDeg += 360.0;
        _polarAngleDeg = angleDeg;
    }

    public void EndMeasurement()
    {
        _polarAngleDeg = null; _polarDistancePdf = null; _polarCursorScreen = null;
        if (_activeMeasurement != null && ActiveLayer != null && _activeMeasurement.Points.Count >= 2)
        {
            // Keep measurement scale consistent with calibrated project scale.
            _activeMeasurement.Scale = MeasurementScale;
            if (!ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var measurements))
            {
                measurements = [];
                ActiveLayer.PageMeasurements[_currentPage] = measurements;
            }
            measurements.Add(_activeMeasurement);
            ActiveLayer.MeasurementCount++;
            _totalMeasurementCount++;
            _undoStack.Push((UndoType.Measurement, _currentPage, null, ActiveLayer));
            _redoStack.Clear();
            LastPlacedAnnotation = _activeMeasurement;
            ActiveLayer.RefreshStatus();
            NotifyAnnotationChanged();
        }
        _activeMeasurement = null;
        _measurementPreviewPoint = null;
        _snapGuideX = null;
        _snapGuideY = null;
        _snapVertexPos = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Calibrate the measurement scale
    /// The user specifies the real-world distance in mm for that measurement,
    /// and all future (and existing) measurements are rescaled accordingly.
    /// </summary>
    public void CalibrateFromLastMeasurement(double realDistanceMm)
    {        // Find the last committed measurement on the current page.
        // Prefer the active layer (where the user just drew), then fall back to any layer.
        MeasurementAnnotation? last = null;
        if (ActiveLayer != null
            && ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var activeMs)
            && activeMs.Count > 0)
        {
            last = activeMs[^1];
        }
        else
        {
            foreach (var layer in Layers)
            {
                if (layer.PageMeasurements.TryGetValue(_currentPage, out var ms) && ms.Count > 0)
                    last = ms[^1];
            }
        }
        if (last == null || last.Points.Count < 2 || realDistanceMm <= 0) return;

        // Compute raw PDF-point distance
        double dx = last.Points[1].X - last.Points[0].X;
        double dy = last.Points[1].Y - last.Points[0].Y;
        double pdfDist = Math.Sqrt(dx * dx + dy * dy);
        if (pdfDist < 0.01) return;

        double newScale = realDistanceMm / pdfDist;
        MeasurementScale = newScale;

        // Apply the new scale to all existing measurements and area-measure strokes
        foreach (var layer in Layers)
        {
            foreach (var list in layer.PageMeasurements.Values)
            {
                foreach (var m in list)
                    m.Scale = newScale;
            }
            foreach (var list in layer.PageStrokes.Values)
            {
                foreach (var s in list)
                {
                    if (s.IsAreaMeasure)
                    {
                        s.AreaScale = newScale;
                        s.InvalidatePen(); // clears cached label geometry
                    }
                }
            }
        }
        InvalidateVisual();
        NotifyAnnotationChanged();
    }

    /// <summary>Calibrates the measurement scale using a specific existing measurement annotation
    /// as the reference, rather than the last committed one.</summary>
    public void CalibrateFromMeasurement(MeasurementAnnotation reference, double realDistanceMm)
    {
        if (reference.Points.Count < 2 || realDistanceMm <= 0) return;
        double dx = reference.Points[1].X - reference.Points[0].X;
        double dy = reference.Points[1].Y - reference.Points[0].Y;
        double pdfDist = Math.Sqrt(dx * dx + dy * dy);
        if (pdfDist < 0.01) return;

        double newScale = realDistanceMm / pdfDist;
        MeasurementScale = newScale;

        foreach (var layer in Layers)
        {
            foreach (var list in layer.PageMeasurements.Values)
                foreach (var m in list)
                    m.Scale = newScale;
            foreach (var list in layer.PageStrokes.Values)
                foreach (var s in list)
                    if (s.IsAreaMeasure) { s.AreaScale = newScale; s.InvalidatePen(); }
        }
        InvalidateVisual();
        NotifyAnnotationChanged();
    }

    #endregion

    #region Eraser

    /// <summary>
    /// Erases the topmost stroke or shape under the given PDF-space point.
    /// Returns true if something was removed.
    /// </summary>
    public bool EraseAt(Point pdfPoint)
    {
        if (ActiveLayer == null || IsActiveLayerLocked) return false;

        double threshold = StrokeWidth * 3;

        // Check text annotations first
        if (ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
        {
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                bool hit = texts[i].IsStickyNote
                    ? HitTestStickyNote(texts[i], pdfPoint)
                    : HitTestText(texts[i], pdfPoint);
                if (hit)
                {
                    var erased = texts[i];
                    texts.RemoveAt(i);
                    ActiveLayer.TextCount = Math.Max(0, ActiveLayer.TextCount - 1);
                    _totalTextCount = Math.Max(0, _totalTextCount - 1);
                    _undoStack.Push((UndoType.Delete, _currentPage, erased, ActiveLayer));
                    _redoStack.Clear();
                    ActiveLayer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return true;
                }
            }
        }

        if (ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var measurements))
        {
            for (int i = measurements.Count - 1; i >= 0; i--)
            {
                if (HitTestMeasurement(measurements[i], pdfPoint, threshold))
                {
                    var erased = measurements[i];
                    measurements.RemoveAt(i);
                    ActiveLayer.MeasurementCount = Math.Max(0, ActiveLayer.MeasurementCount - 1);
                    _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - 1);
                    _undoStack.Push((UndoType.Delete, _currentPage, erased, ActiveLayer));
                    _redoStack.Clear();
                    ActiveLayer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return true;
                }
            }
        }

        if (ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes))
        {
            for (int i = shapes.Count - 1; i >= 0; i--)
            {
                if (HitTestShape(shapes[i], pdfPoint, threshold))
                {
                    var erased = shapes[i];
                    shapes.RemoveAt(i);
                    ActiveLayer.ShapeCount = Math.Max(0, ActiveLayer.ShapeCount - 1);
                    _totalShapeCount = Math.Max(0, _totalShapeCount - 1);
                    _undoStack.Push((UndoType.Delete, _currentPage, erased, ActiveLayer));
                    _redoStack.Clear();
                    ActiveLayer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return true;
                }
            }
        }

        if (ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes))
        {
            for (int i = strokes.Count - 1; i >= 0; i--)
            {
                if (HitTestStroke(strokes[i], pdfPoint, threshold))
                {
                    var erased = strokes[i];
                    strokes.RemoveAt(i);
                    ActiveLayer.StrokeCount = Math.Max(0, ActiveLayer.StrokeCount - 1);
                    _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1);
                    _undoStack.Push((UndoType.Delete, _currentPage, erased, ActiveLayer));
                    _redoStack.Clear();
                    ActiveLayer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Updates the eraser hover highlight. Call on pointer-move when in Eraser mode.
    /// </summary>
    public void UpdateEraserHover(Point pdfPoint)
    {
        object? hit = FindTopmostAt(pdfPoint);
        if (hit != _eraserHoverItem)
        {
            _eraserHoverItem = hit;
            InvalidateVisual();
        }
    }

    public void ClearEraserHover()
    {
        if (_eraserHoverItem != null)
        {
            _eraserHoverItem = null;
            InvalidateVisual();
        }
    }

    /// <summary>Set the item to draw selection handles around (Select tool). Replaces existing selection.</summary>
    public void SetSelectHighlight(object? item)
    {
        _selectHighlightItems.Clear();
        if (item != null) _selectHighlightItems.Add(item);
        InvalidateVisual();
    }

    /// <summary>Add an item to the selection highlight set (Shift+Click).</summary>
    public void AddSelectHighlight(object item)
    {
        _selectHighlightItems.Add(item);
        InvalidateVisual();
    }

    /// <summary>Remove an item from the selection highlight set.</summary>
    public void RemoveSelectHighlight(object item)
    {
        if (_selectHighlightItems.Remove(item)) InvalidateVisual();
    }

    public void ClearSelectHighlight()
    {
        if (_selectHighlightItems.Count > 0)
        {
            _selectHighlightItems.Clear();
            InvalidateVisual();
        }
    }

    /// <summary>Update the hover-outline item in Select mode. Null clears it.</summary>
    public void UpdateSelectHover(object? item)
    {
        if (_selectHoverItem != item)
        {
            _selectHoverItem = item;
            InvalidateVisual();
        }
    }

    #region Rubber-band marquee selection

    /// <summary>Enable or disable screenshot-mode dim-veil rendering for the rubber-band.</summary>
    public void SetScreenshotMode(bool active)
    {
        if (_screenshotMode == active) return;
        _screenshotMode = active;
        if (active)
            PointerMoved += OnScreenshotPointerMovedCursorFix;
        else
            PointerMoved -= OnScreenshotPointerMovedCursorFix;
        InvalidateVisual();
    }

    /// <summary>
    /// Re-applies the Cross cursor after the base PDFRenderer's PointerMoved
    /// handler overwrites it with Arrow. Runs as a Bubble handler registered
    /// after the base class handler so it fires last.
    /// </summary>
    private void OnScreenshotPointerMovedCursorFix(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (_screenshotMode)
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross);
    }

    /// <summary>Update the rubber-band rectangle (PDF coordinates).
    /// <paramref name="crossing"/> is true when the drag is right-to-left (crossing/touching mode).</summary>
    public void SetRubberBand(Point start, Point end, bool crossing = false)
    {
        _rubberBandStart = start;
        _rubberBandEnd = end;
        _rubberBandCrossing = crossing;
        InvalidateVisual();
    }

    /// <summary>Clear the rubber-band rectangle.</summary>
    public void ClearRubberBand()
    {
        if (_rubberBandStart != null)
        {
            _rubberBandStart = null;
            _rubberBandEnd = null;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Find all annotations on the current page (active layer) whose bounds
    /// intersect the given PDF-space rectangle.
    /// </summary>
    /// <param name="crossing">When true, includes annotations that are even partially inside the rectangle (crossing mode).
    /// When false (default), only fully-enclosed annotations are selected (window mode).</param>
    public List<object> FindAnnotationsInRect(Rect pdfRect, bool crossing = false)
    {
        var results = new List<object>();
        if (_activeLayer == null) return results;
        int page = _currentPage;

        bool Matches(Rect b) => crossing ? pdfRect.Intersects(b) : pdfRect.Contains(b);

        if (_activeLayer.PageStrokes.TryGetValue(page, out var strokes))
        {
            foreach (var s in strokes)
                if (GetAnnotationBounds(s) is { Width: > 0 } b && Matches(b))
                    results.Add(s);
        }
        if (_activeLayer.PageShapes.TryGetValue(page, out var shapes))
        {
            foreach (var s in shapes)
                if (GetAnnotationBounds(s) is var b && b != default && Matches(b))
                    results.Add(s);
        }
        if (_activeLayer.PageTexts.TryGetValue(page, out var texts))
        {
            foreach (var t in texts)
                if (GetAnnotationBounds(t) is var b && b != default && Matches(b))
                    results.Add(t);
        }
        if (_activeLayer.PageMeasurements.TryGetValue(page, out var measurements))
        {
            foreach (var m in measurements)
                if (GetAnnotationBounds(m) is var b && b != default && Matches(b))
                    results.Add(m);
        }
        return results;
    }

    #endregion

    /// <summary>
    /// Record for storing the pre-drag state of an annotation so moves/stretches can be undone.
    /// </summary>
    private record MoveSnapshot(object Item, Point[]? Points, Point? Position, Point? ArrowOrigin,
                                 Point? ShapeStart, Point? ShapeEnd);

    private record GroupMoveSnapshot(List<MoveSnapshot> Items);

    /// <summary>
    /// Captures the current position state of an annotation before a drag begins.
    /// Call at drag-start; the returned snapshot is pushed to undo on drag-end.
    /// </summary>
    public object? CapturePreDragSnapshot(object item) => item switch
    {
        TextAnnotation t => new MoveSnapshot(t, null, t.Position, t.ArrowOrigin, null, null),
        ShapeAnnotation s => new MoveSnapshot(s, null, null, null, s.Start, s.End),
        MeasurementAnnotation m => new MoveSnapshot(m, [.. m.Points], null, null, null, null),
        InkStroke ink => new MoveSnapshot(ink, [.. ink.Points], null, null, null, null),
        _ => null
    };

    /// <summary>
    /// Pushes a Move undo entry using a previously captured snapshot.
    /// Call on drag-end.
    /// </summary>
    public void PushMoveUndo(object snapshot)
    {
        _undoStack.Push((UndoType.Move, _currentPage, snapshot, ActiveLayer));
        _redoStack.Clear();
    }

    public void PushGroupMoveUndo(List<object> snapshots)
    {
        if (snapshots.Count == 0) return;
        var list = new List<MoveSnapshot>(snapshots.Count);
        foreach (var s in snapshots)
            if (s is MoveSnapshot ms)
                list.Add(ms);
        if (list.Count == 0) return;
        _undoStack.Push((UndoType.GroupMove, _currentPage, new GroupMoveSnapshot(list), ActiveLayer));
        _redoStack.Clear();
    }

    /// <summary>
    /// Snapshot of all visual properties of an annotation, used for property-change undo.
    /// </summary>
    private record PropertySnapshot(
        object Item,
        Color Color, double Opacity,
        // InkStroke / ShapeAnnotation
        double StrokeWidth, LineDashPattern DashPattern,
        // ShapeAnnotation
        bool IsFilled, double CornerRadius,
        // TextAnnotation
        double FontSize, string Text, double MaxWidth);

    /// <summary>
    /// Captures a snapshot of all visual properties of an annotation.
    /// Call before changing color, width, dash, opacity, fill, font size, or text.
    /// </summary>
    public object? CapturePropertySnapshot(object item) => item switch
    {
        InkStroke s => new PropertySnapshot(s, s.Color, s.Opacity, s.Width, s.DashPattern, false, s.CornerRadius, 0, "", 0),
        ShapeAnnotation sh => new PropertySnapshot(sh, sh.Color, sh.Opacity, sh.StrokeWidth, sh.DashPattern, sh.IsFilled, sh.CornerRadius, 0, "", 0),
        TextAnnotation t => new PropertySnapshot(t, t.Color, t.Opacity, 0, LineDashPattern.Solid, false, 0, t.FontSize, t.Text, t.MaxWidth),
        MeasurementAnnotation m => new PropertySnapshot(m, m.Color, 1.0, 0, LineDashPattern.Solid, false, 0, 0, "", 0),
        _ => null
    };

    /// <summary>Restores all visual properties from a PropertySnapshot.</summary>
    private static void RestorePropertySnapshot(PropertySnapshot snap)
    {
        switch (snap.Item)
        {
            case InkStroke s:
                s.Color = snap.Color; s.Opacity = snap.Opacity;
                s.Width = snap.StrokeWidth; s.DashPattern = snap.DashPattern;
                s.CornerRadius = snap.CornerRadius;
                s.InvalidatePen();
                break;
            case ShapeAnnotation sh:
                sh.Color = snap.Color; sh.Opacity = snap.Opacity;
                sh.StrokeWidth = snap.StrokeWidth; sh.DashPattern = snap.DashPattern;
                sh.IsFilled = snap.IsFilled;
                sh.CornerRadius = snap.CornerRadius;
                sh.InvalidatePen();
                break;
            case TextAnnotation t:
                t.Color = snap.Color; t.Opacity = snap.Opacity;
                t.FontSize = snap.FontSize; t.Text = snap.Text;
                t.MaxWidth = snap.MaxWidth;
                break;
            case MeasurementAnnotation m:
                m.Color = snap.Color;
                break;
        }
    }

    /// <summary>
    /// Pushes a property-change undo entry using a previously captured snapshot.
    /// </summary>
    public void PushPropertyUndo(object snapshot)
    {
        _undoStack.Push((UndoType.PropertyChange, _currentPage, snapshot, ActiveLayer));
        _redoStack.Clear();
    }

    /// <summary>
    /// Pushes a single grouped undo entry covering property changes to multiple annotations.
    /// Call with snapshots captured BEFORE the changes are applied.
    /// </summary>
    public void PushGroupPropertyUndo(List<object> snapshots)
    {
        if (snapshots.Count == 0) return;
        _undoStack.Push((UndoType.GroupPropertyChange, _currentPage, snapshots, ActiveLayer));
        _redoStack.Clear();
    }

    /// <summary>Record for z-order undo: stores the item and its index before the move.</summary>
    private record ZOrderSnapshot(object Item, int OldIndex);

    /// <summary>Captures the current ZIndex of an annotation for undo.</summary>
    public object? CaptureZOrderSnapshot(object item)
    {
        int z = item switch
        {
            InkStroke s => s.ZIndex,
            ShapeAnnotation sh => sh.ZIndex,
            TextAnnotation t => t.ZIndex,
            MeasurementAnnotation m => m.ZIndex,
            _ => int.MinValue
        };
        return z != int.MinValue ? new ZOrderSnapshot(item, z) : null;
    }

    /// <summary>Restores an annotation's ZIndex from a previously captured snapshot.</summary>
    private void RestoreZOrder(ZOrderSnapshot snap)
    {
        switch (snap.Item)
        {
            case InkStroke s: s.ZIndex = snap.OldIndex; break;
            case ShapeAnnotation sh: sh.ZIndex = snap.OldIndex; break;
            case TextAnnotation t: t.ZIndex = snap.OldIndex; break;
            case MeasurementAnnotation m: m.ZIndex = snap.OldIndex; break;
        }
    }

    /// <summary>Snapshot of all position and size properties for a group resize undo.</summary>
    public record GroupResizeSnapshot(object Item, Point[]? Points, Point? Position, Point? ArrowOrigin,
        Point? ShapeStart, Point? ShapeEnd, double FontSize, double MaxWidth, double StrokeWidth, double InkWidth);

    /// <summary>Captures position and size properties of an annotation for group resize undo.</summary>
    internal static GroupResizeSnapshot CaptureGroupResizeSnapshot(object item) => item switch
    {
        TextAnnotation t => new GroupResizeSnapshot(t, null, t.Position, t.ArrowOrigin, null, null, t.FontSize, t.MaxWidth, 0, 0),
        ShapeAnnotation s => new GroupResizeSnapshot(s, null, null, null, s.Start, s.End, 0, 0, s.StrokeWidth, 0),
        MeasurementAnnotation m => new GroupResizeSnapshot(m, [.. m.Points], null, null, null, null, 0, 0, 0, 0),
        InkStroke ink => new GroupResizeSnapshot(ink, [.. ink.Points], null, null, null, null, 0, 0, 0, ink.Width),
        _ => new GroupResizeSnapshot(item, null, null, null, null, null, 0, 0, 0, 0)
    };

    /// <summary>Restores all position and size properties from a GroupResizeSnapshot.</summary>
    internal static void RestoreGroupResizeSnapshot(GroupResizeSnapshot s)
    {
        switch (s.Item)
        {
            case TextAnnotation t:
                if (s.Position.HasValue) t.Position = s.Position.Value;
                t.ArrowOrigin = s.ArrowOrigin;
                t.FontSize = s.FontSize;
                t.MaxWidth = s.MaxWidth;
                break;
            case ShapeAnnotation sh:
                if (s.ShapeStart.HasValue) sh.Start = s.ShapeStart.Value;
                if (s.ShapeEnd.HasValue) sh.End = s.ShapeEnd.Value;
                sh.StrokeWidth = s.StrokeWidth;
                sh.InvalidatePen();
                break;
            case MeasurementAnnotation m:
                if (s.Points != null)
                    for (int i = 0; i < s.Points.Length && i < m.Points.Count; i++)
                        m.Points[i] = s.Points[i];
                break;
            case InkStroke ink:
                if (s.Points != null)
                {
                    ink.Points.Clear();
                    ink.Points.AddRange(s.Points);
                }
                ink.Width = s.InkWidth;
                ink.InvalidatePen();
                break;
        }
    }

    /// <summary>
    /// Pushes a single group-resize undo entry that captures position + size for all items.
    /// Call with the pre-resize snapshots; the method captures post-resize state internally.
    /// </summary>
    public void PushGroupResizeUndo(List<GroupResizeSnapshot> preStates)
    {
        _undoStack.Push((UndoType.GroupResize, _currentPage, preStates, ActiveLayer));
        _redoStack.Clear();
    }

    /// <summary>
    /// Computes snap-to-alignment for a dragged annotation's bounding-box center.
    /// Compares against all other annotations on the current page and returns a
    /// snap-corrected delta plus sets guide lines for rendering.
    /// </summary>
    public (double dx, double dy) ComputeSnapDelta(object dragging, double rawDx, double rawDy, double threshold = 5.0)
    {
        _snapGuideX = null;
        _snapGuideY = null;
        _snapKindX = SnapKind.None;
        _snapKindY = SnapKind.None;
        if (ActiveLayer == null) return (rawDx, rawDy);

        var db = GetAnnotationBounds(dragging);
        bool isDot = dragging is ShapeAnnotation { ShapeType: InlineAnnotationTool.Dot };

        // Grid snap: snap center for dots, top-left corner for everything else.
        if (SnapToGrid && GridSpacing > 0)
        {
            double g = GridSpacing;
            if (isDot)
            {
                double cx = db.Left + db.Width / 2 + rawDx;
                double cy = db.Top + db.Height / 2 + rawDy;
                double snappedCx = Math.Round(cx / g) * g;
                double snappedCy = Math.Round(cy / g) * g;
                _snapGuideX = snappedCx;
                _snapGuideY = snappedCy;
                _snapKindX = SnapKind.Grid;
                _snapKindY = SnapKind.Grid;
                return (rawDx + (snappedCx - cx), rawDy + (snappedCy - cy));
            }
            double newL = db.Left + rawDx;
            double newT = db.Top + rawDy;
            double snappedL = Math.Round(newL / g) * g;
            double snappedT = Math.Round(newT / g) * g;
            double dx = snappedL - db.Left;
            double dy = snappedT - db.Top;
            _snapGuideX = snappedL;
            _snapGuideY = snappedT;
            _snapKindX = SnapKind.Grid;
            _snapKindY = SnapKind.Grid;
            return (dx, dy);
        }

        // For dots, only snap on the center point (not corners).
        double dL, dR, dCx, dT, dB, dCy;
        if (isDot)
        {
            dCx = (db.Left + db.Right) / 2 + rawDx;
            dCy = (db.Top + db.Bottom) / 2 + rawDy;
            dL = dCx; dR = dCx; dT = dCy; dB = dCy;
        }
        else
        {
            dL = db.Left + rawDx; dR = db.Right + rawDx; dCx = (db.Left + db.Right) / 2 + rawDx;
            dT = db.Top + rawDy; dB = db.Bottom + rawDy; dCy = (db.Top + db.Bottom) / 2 + rawDy;
        }

        double bestDx = rawDx, bestDy = rawDy;
        double bestSnapDistX = threshold, bestSnapDistY = threshold;

        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            CollectSnapTargets(layer, dragging, dL, dCx, dR, dT, dCy, dB,
                rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY);
        }

        return (bestDx, bestDy);
    }

    public void ClearSnapGuides()
    {
        if (_snapGuideX != null || _snapGuideY != null || _snapVertexPos != null)
        {
            _snapGuideX = null;
            _snapGuideY = null;
            _snapVertexPos = null;
            _snapKindX = SnapKind.None;
            _snapKindY = SnapKind.None;
            InvalidateVisual();
        }
    }

    /// <summary>Draws a small icon
    /// square = Bounds/Center, diamond = Vertex, triangle = Midpoint, circle = Grid.</summary>
    private static void DrawSnapIcon(DrawingContext ctx, Point center, SnapKind kind,
        double r, IBrush fill, IPen pen)
    {
        switch (kind)
        {
            case SnapKind.Vertex:
                // Diamond
                var dg = new StreamGeometry();
                using (var dgc = dg.Open())
                {
                    dgc.BeginFigure(new Point(center.X, center.Y - r), true);
                    dgc.LineTo(new Point(center.X + r, center.Y));
                    dgc.LineTo(new Point(center.X, center.Y + r));
                    dgc.LineTo(new Point(center.X - r, center.Y));
                    dgc.EndFigure(true);
                }
                ctx.DrawGeometry(fill, pen, dg);
                break;
            case SnapKind.Midpoint:
                // Upward-pointing triangle
                var tg = new StreamGeometry();
                using (var tgc = tg.Open())
                {
                    tgc.BeginFigure(new Point(center.X, center.Y - r), true);
                    tgc.LineTo(new Point(center.X + r, center.Y + r));
                    tgc.LineTo(new Point(center.X - r, center.Y + r));
                    tgc.EndFigure(true);
                }
                ctx.DrawGeometry(fill, pen, tg);
                break;
            case SnapKind.Grid:
                // Small circle
                ctx.DrawEllipse(fill, pen, center, r * 0.75, r * 0.75);
                break;
            default:
                // Square for Bounds / Center / None
                ctx.DrawRectangle(fill, pen,
                    new Rect(center.X - r, center.Y - r, r * 2, r * 2));
                break;
        }
    }

    private void CollectSnapTargets(AnnotationLayer layer, object dragging,
        double dL, double dCx, double dR, double dT, double dCy, double dB,
        double rawDx, double rawDy,
        ref double bestDx, ref double bestDy,
        ref double bestSnapDistX, ref double bestSnapDistY)
    {
        if (layer.PageTexts.TryGetValue(_currentPage, out var texts))
            foreach (var t in texts) { if (!ReferenceEquals(t, dragging)) SnapAgainst(t, dL, dCx, dR, dT, dCy, dB, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
        if (layer.PageShapes.TryGetValue(_currentPage, out var shapes))
            foreach (var s in shapes) { if (!ReferenceEquals(s, dragging)) SnapAgainst(s, dL, dCx, dR, dT, dCy, dB, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
        if (layer.PageMeasurements.TryGetValue(_currentPage, out var ms))
            foreach (var m in ms) { if (!ReferenceEquals(m, dragging)) SnapAgainst(m, dL, dCx, dR, dT, dCy, dB, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
        if (layer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            foreach (var ink in strokes) { if (!ReferenceEquals(ink, dragging)) SnapAgainst(ink, dL, dCx, dR, dT, dCy, dB, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
    }

    /// <summary>
    /// Updates snap guide visuals for the given cursor position without committing any point.
    /// Used to give the user first-click snapping feedback during idle hover over creation tools.
    /// </summary>
    public void UpdateSnapPreview(Point pdfPoint)
    {
        ComputeVertexSnap(null!, pdfPoint);
        InvalidateVisual();
    }

    /// <summary>Rounds a point to the nearest grid intersection when <see cref="SnapToGrid"/> is active.</summary>
    public Point SnapPointToGrid(Point pt)
    {
        if (!SnapToGrid || GridSpacing <= 0) return pt;
        double g = GridSpacing;
        return new Point(Math.Round(pt.X / g) * g, Math.Round(pt.Y / g) * g);
    }

    /// <summary>
    /// Snap a single vertex position against other annotations' edges and centers.
    /// Also snaps against the active polyline's own committed points (self-snap)
    /// and applies grid-snap when <see cref="SnapToGrid"/> is enabled.
    /// Returns the snapped position. Sets _snapGuideX/_snapGuideY for guide rendering.
    /// </summary>
    public Point ComputeVertexSnap(object owner, Point vertex, double threshold = -1)
    {
        // -1 = auto: compute a zoom-adaptive threshold from a fixed screen-pixel radius.
        // This keeps the snap "bubble" a constant visual size regardless of zoom level,
        // so snapping feels less aggressive when zoomed out and more precise when zoomed in.
        if (threshold < 0)
        {
            const double snapScreenPx  = 8.0;  // desired snap radius in screen pixels
            const double snapMinPdfPts = 1.5;  // never go below this in PDF units (prevents snap disappearing when very zoomed out)
            threshold = Math.Max(snapMinPdfPts, ScreenToPdfDistance(snapScreenPx));
        }
        _snapGuideX = null;
        _snapGuideY = null;
        _snapKindX = SnapKind.None;
        _snapKindY = SnapKind.None;

        // Grid snap takes priority — snap to grid first, then refine with alignment snap
        if (SnapToGrid && GridSpacing > 0)
        {
            vertex = SnapPointToGrid(vertex);
            _snapGuideX = vertex.X;
            _snapGuideY = vertex.Y;
            _snapKindX = SnapKind.Grid;
            _snapKindY = SnapKind.Grid;
            _snapVertexPos = vertex;
            return vertex;
        }

        double bestDistX = threshold, bestDistY = threshold;
        double snapX = vertex.X, snapY = vertex.Y;

        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageTexts.TryGetValue(_currentPage, out var texts))
                foreach (var t in texts)
                    if (!ReferenceEquals(t, owner)) SnapVertexAgainst(t, vertex.X, vertex.Y, ref snapX, ref snapY, ref bestDistX, ref bestDistY);
            if (layer.PageShapes.TryGetValue(_currentPage, out var shapes))
                foreach (var s in shapes)
                    if (!ReferenceEquals(s, owner)) SnapVertexAgainst(s, vertex.X, vertex.Y, ref snapX, ref snapY, ref bestDistX, ref bestDistY);
            if (layer.PageMeasurements.TryGetValue(_currentPage, out var ms))
                foreach (var m in ms)
                    if (!ReferenceEquals(m, owner)) SnapVertexAgainst(m, vertex.X, vertex.Y, ref snapX, ref snapY, ref bestDistX, ref bestDistY);
            if (layer.PageStrokes.TryGetValue(_currentPage, out var strokes))
                foreach (var ink in strokes)
                    if (!ReferenceEquals(ink, owner)) SnapVertexAgainst(ink, vertex.X, vertex.Y, ref snapX, ref snapY, ref bestDistX, ref bestDistY);
        }

        // Polyline self-snap: snap against the polyline's own committed points
        // (so users can close shapes or align to earlier vertices).
        if (_activePolyline != null && _activePolyline.Points.Count >= 1)
        {
            for (int i = 0; i < _activePolyline.Points.Count; i++)
            {
                var pp = _activePolyline.Points[i];
                double dx = Math.Abs(vertex.X - pp.X);
                if (IsBetterSnap(dx, bestDistX, SnapKind.Vertex, _snapKindX)) { bestDistX = dx; snapX = pp.X; _snapGuideX = pp.X; _snapKindX = SnapKind.Vertex; }
                double dy = Math.Abs(vertex.Y - pp.Y);
                if (IsBetterSnap(dy, bestDistY, SnapKind.Vertex, _snapKindY)) { bestDistY = dy; snapY = pp.Y; _snapGuideY = pp.Y; _snapKindY = SnapKind.Vertex; }
            }
        }

        var result = new Point(snapX, snapY);
        _snapVertexPos = (_snapGuideX.HasValue || _snapGuideY.HasValue) ? result : null;
        return result;
    }

    private void SnapVertexAgainst(object target, double vx, double vy,
        ref double snapX, ref double snapY, ref double bestDistX, ref double bestDistY)
    {
        // For polylines, snap to every individual vertex point AND segment midpoints.
        if (target is InkStroke ink && ink.IsPolyline && ink.Points.Count > 0)
        {
            foreach (var pt in ink.Points)
            {
                double dx = Math.Abs(vx - pt.X);
                if (IsBetterSnap(dx, bestDistX, SnapKind.Vertex, _snapKindX)) { bestDistX = dx; snapX = pt.X; _snapGuideX = pt.X; _snapKindX = SnapKind.Vertex; }
                double dy = Math.Abs(vy - pt.Y);
                if (IsBetterSnap(dy, bestDistY, SnapKind.Vertex, _snapKindY)) { bestDistY = dy; snapY = pt.Y; _snapGuideY = pt.Y; _snapKindY = SnapKind.Vertex; }
            }
            // Midpoints of each segment
            int count = ink.IsClosed ? ink.Points.Count : ink.Points.Count - 1;
            for (int i = 0; i < count; i++)
            {
                var a = ink.Points[i]; var b = ink.Points[(i + 1) % ink.Points.Count];
                double mx = (a.X + b.X) * 0.5, my = (a.Y + b.Y) * 0.5;
                double dx = Math.Abs(vx - mx);
                if (IsBetterSnap(dx, bestDistX, SnapKind.Midpoint, _snapKindX)) { bestDistX = dx; snapX = mx; _snapGuideX = mx; _snapKindX = SnapKind.Midpoint; }
                double dy = Math.Abs(vy - my);
                if (IsBetterSnap(dy, bestDistY, SnapKind.Midpoint, _snapKindY)) { bestDistY = dy; snapY = my; _snapGuideY = my; _snapKindY = SnapKind.Midpoint; }
            }
            return;
        }

        var tb = GetAnnotationBounds(target);
        var tc = GetAnnotationCenter(target);

        // Inline iteration over {Left, CenterX, Right} to avoid allocating new[]
        double tx = tb.Left;
        for (int i = 0; i < 3; i++)
        {
            if (i == 1) tx = tc.X; else if (i == 2) tx = tb.Right;
            double dist = Math.Abs(vx - tx);
            var kind = i == 1 ? SnapKind.Center : SnapKind.Bounds;
            if (IsBetterSnap(dist, bestDistX, kind, _snapKindX)) { bestDistX = dist; snapX = tx; _snapGuideX = tx; _snapKindX = kind; }
        }
        double ty = tb.Top;
        for (int i = 0; i < 3; i++)
        {
            if (i == 1) ty = tc.Y; else if (i == 2) ty = tb.Bottom;
            double dist = Math.Abs(vy - ty);
            var kind = i == 1 ? SnapKind.Center : SnapKind.Bounds;
            if (IsBetterSnap(dist, bestDistY, kind, _snapKindY)) { bestDistY = dist; snapY = ty; _snapGuideY = ty; _snapKindY = kind; }
        }
        }

    private void SnapAgainst(object target, double dL, double dCx, double dR, double dT, double dCy, double dB,
        double rawDx, double rawDy, ref double bestDx, ref double bestDy,
        ref double bestSnapDistX, ref double bestSnapDistY)
    {
        // For polylines, snap to every individual vertex rather than the bounding box.
        if (target is InkStroke { IsPolyline: true } poly && poly.Points.Count > 0)
        {
            Span<double> dxVals = [dL, dCx, dR];
            Span<double> dyVals = [dT, dCy, dB];
            foreach (var pt in poly.Points)
            {
                for (int di = 0; di < 3; di++)
                {
                    double distX = Math.Abs(dxVals[di] - pt.X);
                    var sourceKindX = di == 1 ? SnapKind.Center : SnapKind.Bounds;
                    var targetKindX = SnapKind.Vertex;
                    var kindX = SnapPriority(targetKindX) < SnapPriority(sourceKindX) ? targetKindX : sourceKindX;
                    if (IsBetterSnap(distX, bestSnapDistX, kindX, _snapKindX))
                    { bestSnapDistX = distX; bestDx = rawDx + (pt.X - dxVals[di]); _snapGuideX = pt.X; _snapKindX = kindX; }
                    double distY = Math.Abs(dyVals[di] - pt.Y);
                    var sourceKindY = di == 1 ? SnapKind.Center : SnapKind.Bounds;
                    var targetKindY = SnapKind.Vertex;
                    var kindY = SnapPriority(targetKindY) < SnapPriority(sourceKindY) ? targetKindY : sourceKindY;
                    if (IsBetterSnap(distY, bestSnapDistY, kindY, _snapKindY))
                    { bestSnapDistY = distY; bestDy = rawDy + (pt.Y - dyVals[di]); _snapGuideY = pt.Y; _snapKindY = kindY; }
                }
            }
            return;
        }

        var tb = GetAnnotationBounds(target);
        var tc = GetAnnotationCenter(target);
        double tL = tb.Left, tR = tb.Right, tCx = tc.X;
        double tT = tb.Top, tB = tb.Bottom, tCy = tc.Y;

        // Inline 3×3 iteration to avoid allocating new[] arrays each call
        Span<double> dxVs = [dL, dCx, dR];
        Span<double> txVals = [tL, tCx, tR];
        for (int di = 0; di < 3; di++)
            for (int ti = 0; ti < 3; ti++)
            {
                double dist = Math.Abs(dxVs[di] - txVals[ti]);
                var sourceKind = di == 1 ? SnapKind.Center : SnapKind.Bounds;
                var targetKind = ti == 1 ? SnapKind.Center : SnapKind.Bounds;
                var kind = SnapPriority(sourceKind) <= SnapPriority(targetKind) ? sourceKind : targetKind;
                if (IsBetterSnap(dist, bestSnapDistX, kind, _snapKindX))
                { bestSnapDistX = dist; bestDx = rawDx + (txVals[ti] - dxVs[di]); _snapGuideX = txVals[ti]; _snapKindX = kind; }
            }
        Span<double> dyVs = [dT, dCy, dB];
        Span<double> tyVals = [tT, tCy, tB];
        for (int di = 0; di < 3; di++)
            for (int ti = 0; ti < 3; ti++)
            {
                double dist = Math.Abs(dyVs[di] - tyVals[ti]);
                var sourceKind = di == 1 ? SnapKind.Center : SnapKind.Bounds;
                var targetKind = ti == 1 ? SnapKind.Center : SnapKind.Bounds;
                var kind = SnapPriority(sourceKind) <= SnapPriority(targetKind) ? sourceKind : targetKind;
                if (IsBetterSnap(dist, bestSnapDistY, kind, _snapKindY))
                { bestSnapDistY = dist; bestDy = rawDy + (tyVals[ti] - dyVs[di]); _snapGuideY = tyVals[ti]; _snapKindY = kind; }
            }
    }

    private static Point GetAnnotationCenter(object item) => item switch
    {
        TextAnnotation t => t.Position,
        ShapeAnnotation s => new Point((s.Start.X + s.End.X) / 2, (s.Start.Y + s.End.Y) / 2),
        MeasurementAnnotation m when m.Points.Count >= 2 =>
            new Point((m.Points[0].X + m.Points[1].X) / 2, (m.Points[0].Y + m.Points[1].Y) / 2),
        InkStroke ink when ink.Points.Count > 0 => GetStrokeCenter(ink),
        _ => default
    };

    private static Point GetStrokeCenter(InkStroke ink)
    {
        double sumX = 0, sumY = 0;
        int count = ink.Points.Count;
        for (int i = 0; i < count; i++)
        {
            sumX += ink.Points[i].X;
            sumY += ink.Points[i].Y;
        }
        return new Point(sumX / count, sumY / count);
    }

    internal static Rect GetAnnotationBounds(object item) => item switch
    {
        TextAnnotation t => GetTextBounds(t),
        ShapeAnnotation { ShapeType: InlineAnnotationTool.Dot } dot =>
            new Rect(dot.Start.X - dot.StrokeWidth, dot.Start.Y - dot.StrokeWidth,
                     dot.StrokeWidth * 2, dot.StrokeWidth * 2),
        ShapeAnnotation s => NormalizedRect(s.Start, s.End),
        MeasurementAnnotation m when m.Points.Count >= 2 => NormalizedRect(m.Points[0], m.Points[1]),
        InkStroke ink when ink.Points.Count > 0 => GetStrokeBounds(ink),
        _ => default
    };

    /// <summary>Computes the combined PDF-space bounding box for a set of annotations.</summary>
    internal static Rect GetCombinedBounds(IEnumerable<object> items)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var item in items)
        {
            var b = GetAnnotationBounds(item);
            if (b == default) continue;
            minX = Math.Min(minX, b.Left);
            minY = Math.Min(minY, b.Top);
            maxX = Math.Max(maxX, b.Right);
            maxY = Math.Max(maxY, b.Bottom);
        }
        if (minX > maxX) return default;
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>Scales an annotation around a given anchor point by the given factors.</summary>
    internal static void ScaleAnnotation(object item, Point anchor, double sx, double sy)
    {
        static Point Scale(Point p, Point a, double sx, double sy) =>
            new(a.X + (p.X - a.X) * sx, a.Y + (p.Y - a.Y) * sy);

        switch (item)
        {
            case TextAnnotation t:
                t.Position = Scale(t.Position, anchor, sx, sy);
                if (t.ArrowOrigin.HasValue)
                    t.ArrowOrigin = Scale(t.ArrowOrigin.Value, anchor, sx, sy);
                t.FontSize = Math.Max(4, t.FontSize * Math.Max(sx, sy));
                if (t.MaxWidth > 0)
                    t.MaxWidth = Math.Max(20, t.MaxWidth * sx);
                break;
            case ShapeAnnotation s when s.ShapeType == InlineAnnotationTool.Dot:
                // Dots are fixed-size point objects — translate the center but don't scale.
                s.Start = Scale(s.Start, anchor, sx, sy);
                s.End = s.Start;
                s.InvalidatePen();
                break;
            case ShapeAnnotation s:
                s.Start = Scale(s.Start, anchor, sx, sy);
                s.End = Scale(s.End, anchor, sx, sy);
                s.StrokeWidth = Math.Max(0.5, s.StrokeWidth * Math.Max(sx, sy));
                s.InvalidatePen();
                break;
            case MeasurementAnnotation m:
                for (int i = 0; i < m.Points.Count; i++)
                    m.Points[i] = Scale(m.Points[i], anchor, sx, sy);
                break;
            case InkStroke ink:
                for (int i = 0; i < ink.Points.Count; i++)
                    ink.Points[i] = Scale(ink.Points[i], anchor, sx, sy);
                ink.Width = Math.Max(0.5, ink.Width * Math.Max(sx, sy));
                ink.InvalidatePen();
                break;
        }
    }

    /// <summary>Compute bounding box for a text annotation, accounting for MaxWidth word wrap.</summary>
    internal static Rect GetTextBounds(TextAnnotation t)
    {
        // Use pixel-accurate cached bounds when available (set during render)
        if (t.HasMeasuredBounds)
            return new Rect(t.Position.X, t.Position.Y, t.MeasuredWidth, t.MeasuredHeight);

        // Fallback heuristic for pre-render hit-testing
        double w, h;
        int newlineCount = CountNewlines(t.Text);
        if (t.MaxWidth > 0)
        {
            w = t.MaxWidth;
            double totalCharWidth = t.FontSize * Math.Max(1, t.Text.Length) * 0.55;
            int lineCount = Math.Max(1, (int)Math.Ceiling(totalCharWidth / t.MaxWidth));
            lineCount = Math.Max(lineCount, 1 + newlineCount);
            h = t.FontSize * lineCount * 1.3;
        }
        else
        {
            w = t.FontSize * Math.Max(1, t.Text.Length) * 0.55;
            h = t.FontSize * (1 + newlineCount) * 1.3;
        }
        return new Rect(t.Position.X, t.Position.Y, w, h);
    }

    private static int CountNewlines(string text)
    {
        int count = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n') count++;
        return count;
    }

    /// <summary>
    /// Word-wraps text into lines that fit within maxWidthPx (screen pixels).
    /// Preserves explicit newlines and breaks at word boundaries.
    /// </summary>
    private static List<string> WrapTextLines(string text, float maxWidthPx, SKFont font)
    {
        var result = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            if (string.IsNullOrEmpty(paragraph)) { result.Add(""); continue; }
            var words = paragraph.Split(' ');
            string currentLine = "";
            foreach (var word in words)
            {
                string test = currentLine.Length == 0 ? word : currentLine + " " + word;
                float width = font.MeasureText(test);
                if (width <= maxWidthPx || currentLine.Length == 0)
                    currentLine = test;
                else
                {
                    result.Add(currentLine);
                    currentLine = word;
                }
            }
            if (currentLine.Length > 0)
                result.Add(currentLine);
        }
        if (result.Count == 0) result.Add("");
        return result;
    }

    private static Rect GetStrokeBounds(InkStroke ink)
    {
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in ink.Points)
        {
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }
        return new Rect(minX, minY, Math.Max(0, maxX - minX), Math.Max(0, maxY - minY));
    }

    #region Z-Ordering

    /// <summary>Move an annotation to the top of its type list (drawn last = visually on top).</summary>
    /// <summary>Returns the max ZIndex across all annotations on the current page in all visible layers.</summary>
    private int GetMaxZIndex()
    {
        int max = 0;
        foreach (var layer in Layers)
        {
            if (layer.PageStrokes.TryGetValue(_currentPage, out var st)) foreach (var s in st) if (s.ZIndex > max) max = s.ZIndex;
            if (layer.PageShapes.TryGetValue(_currentPage, out var sh)) foreach (var s in sh) if (s.ZIndex > max) max = s.ZIndex;
            if (layer.PageTexts.TryGetValue(_currentPage, out var tx)) foreach (var s in tx) if (s.ZIndex > max) max = s.ZIndex;
            if (layer.PageMeasurements.TryGetValue(_currentPage, out var ms)) foreach (var s in ms) if (s.ZIndex > max) max = s.ZIndex;
        }
        return max;
    }

    /// <summary>Returns the min ZIndex across all annotations on the current page in all visible layers.</summary>
    private int GetMinZIndex()
    {
        int min = 0;
        foreach (var layer in Layers)
        {
            if (layer.PageStrokes.TryGetValue(_currentPage, out var st)) foreach (var s in st) if (s.ZIndex < min) min = s.ZIndex;
            if (layer.PageShapes.TryGetValue(_currentPage, out var sh)) foreach (var s in sh) if (s.ZIndex < min) min = s.ZIndex;
            if (layer.PageTexts.TryGetValue(_currentPage, out var tx)) foreach (var s in tx) if (s.ZIndex < min) min = s.ZIndex;
            if (layer.PageMeasurements.TryGetValue(_currentPage, out var ms)) foreach (var s in ms) if (s.ZIndex < min) min = s.ZIndex;
        }
        return min;
    }

    public bool BringToFront(object item)
    {
        if (ActiveLayer == null) return false;
        var zSnap = CaptureZOrderSnapshot(item);
        int newZ = GetMaxZIndex() + 1;
        bool moved = false;
        switch (item)
        {
            case InkStroke s: s.ZIndex = newZ; moved = true; break;
            case ShapeAnnotation sh: sh.ZIndex = newZ; moved = true; break;
            case TextAnnotation t: t.ZIndex = newZ; moved = true; break;
            case MeasurementAnnotation m: m.ZIndex = newZ; moved = true; break;
        }
        if (moved)
        {
            if (zSnap != null) { _undoStack.Push((UndoType.ZOrder, _currentPage, zSnap, ActiveLayer)); _redoStack.Clear(); }
            InvalidateVisual(); NotifyAnnotationChanged();
        }
        return moved;
    }

    /// <summary>Move an annotation behind all others by assigning a ZIndex below the current minimum.</summary>
    public bool SendToBack(object item)
    {
        if (ActiveLayer == null) return false;
        var zSnap = CaptureZOrderSnapshot(item);
        int newZ = GetMinZIndex() - 1;
        bool moved = false;
        switch (item)
        {
            case InkStroke s: s.ZIndex = newZ; moved = true; break;
            case ShapeAnnotation sh: sh.ZIndex = newZ; moved = true; break;
            case TextAnnotation t: t.ZIndex = newZ; moved = true; break;
            case MeasurementAnnotation m: m.ZIndex = newZ; moved = true; break;
        }
        if (moved)
        {
            if (zSnap != null) { _undoStack.Push((UndoType.ZOrder, _currentPage, zSnap, ActiveLayer)); _redoStack.Clear(); }
            InvalidateVisual(); NotifyAnnotationChanged();
        }
        return moved;
    }

    #endregion

    /// <summary>
    /// Shows or hides the sticky-note hover popup. Call on pointer-move when a
    /// sticky note icon is under the cursor.
    /// </summary>
    public void UpdateStickyNoteHover(Point pdfPoint)
    {
        // Fast path: skip scan when no text annotations exist anywhere
        if (_totalTextCount == 0)
        {
            if (_stickyNoteHoverItem != null) { _stickyNoteHoverItem = null; InvalidateVisual(); }
            return;
        }
        TextAnnotation? hit = null;
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (!layer.PageTexts.TryGetValue(_currentPage, out var texts)) continue;
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                // Only show hover popup for sticky notes — regular text annotations
                // are already visible on the canvas and don't need a redundant tooltip.
                if (texts[i].IsStickyNote && HitTestStickyNote(texts[i], pdfPoint))
                { hit = texts[i]; break; }
            }
            if (hit != null) break;
        }
        if (hit != _stickyNoteHoverItem)
        {
            _stickyNoteHoverItem = hit;
            InvalidateVisual();
        }
    }

    public void ClearStickyNoteHover()
    {
        if (_stickyNoteHoverItem != null)
        {
            _stickyNoteHoverItem = null;
            InvalidateVisual();
        }
    }

    /// <summary>Update the pen cursor preview position. Call on pointer-move in Draw/Highlight mode.</summary>
    public void UpdateCursorPreview(Point? pdfPos)
    {
        if (_cursorPdfPos != pdfPos)
        {
            _cursorPdfPos = pdfPos;
            InvalidateVisual();
        }
    }

    /// <summary>Show a ghost preview for text/sticky/arrowtext placement at cursor.</summary>
    public void UpdateTextPlacementPreview(InlineAnnotationTool tool, Point? pdfPos)
    {
        if (_textPlacementPreviewTool != tool || _textPlacementPreviewPos != pdfPos)
        {
            _textPlacementPreviewTool = tool;
            _textPlacementPreviewPos = pdfPos;
            InvalidateVisual();
        }
    }

    public void ClearTextPlacementPreview()
    {
        if (_textPlacementPreviewPos != null)
        {
            _textPlacementPreviewPos = null;
            _textPlacementPreviewTool = null;
            InvalidateVisual();
        }
    }

    /// <summary>Find the topmost annotation at a point.</summary>
    public object? FindTopmostAt(Point pdfPoint)
    {
        if (ActiveLayer == null) return null;
        double threshold = StrokeWidth * 3;

        if (ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
            for (int i = texts.Count - 1; i >= 0; i--)
            {
                var t = texts[i];
                // Arrow origin (tip) hit-test first — small circle around the tip
                if (t.ArrowOrigin.HasValue)
                {
                    double dx = pdfPoint.X - t.ArrowOrigin.Value.X;
                    double dy = pdfPoint.Y - t.ArrowOrigin.Value.Y;
                    if (dx * dx + dy * dy <= 8 * 8) return t;
                }
                bool hit = t.IsStickyNote
                    ? HitTestStickyNote(t, pdfPoint)
                    : HitTestText(t, pdfPoint);
                if (hit) return t;
            }

        if (ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var measurements))
            for (int i = measurements.Count - 1; i >= 0; i--)
                if (HitTestMeasurement(measurements[i], pdfPoint, threshold)) return measurements[i];

        if (ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes))
            for (int i = shapes.Count - 1; i >= 0; i--)
                if (HitTestShape(shapes[i], pdfPoint, threshold)) return shapes[i];

        if (ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            for (int i = strokes.Count - 1; i >= 0; i--)
                if (HitTestStroke(strokes[i], pdfPoint, threshold)) return strokes[i];

        return null;
    }

    /// <summary>Delete a specific annotation by reference from the active layer's current page.</summary>
    public bool DeleteAnnotation(object item)
    {
        if (ActiveLayer == null || IsActiveLayerLocked) return false;
        bool removed = false;
        switch (item)
        {
            case TextAnnotation t:
                if (ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts) && texts.Remove(t))
                { ActiveLayer.TextCount = Math.Max(0, ActiveLayer.TextCount - 1); _totalTextCount = Math.Max(0, _totalTextCount - 1); removed = true; }
                break;
            case ShapeAnnotation s:
                if (ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes) && shapes.Remove(s))
                { ActiveLayer.ShapeCount = Math.Max(0, ActiveLayer.ShapeCount - 1); _totalShapeCount = Math.Max(0, _totalShapeCount - 1); removed = true; }
                break;
            case MeasurementAnnotation m:
                if (ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var ms) && ms.Remove(m))
                { ActiveLayer.MeasurementCount = Math.Max(0, ActiveLayer.MeasurementCount - 1); _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - 1); removed = true; }
                break;
            case InkStroke ink:
                if (ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes) && strokes.Remove(ink))
                { ActiveLayer.StrokeCount = Math.Max(0, ActiveLayer.StrokeCount - 1); _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1); removed = true; }
                break;
        }
        if (removed)
        {
            _undoStack.Push((UndoType.Delete, _currentPage, item, ActiveLayer));
            _redoStack.Clear();
            ActiveLayer.RefreshStatus();
            _selectHighlightItems.Remove(item);
            RemoveFromGroups(item);
            InvalidateVisual();
            NotifyAnnotationChanged();
        }
        return removed;
    }

    /// <summary>Re-adds a previously deleted annotation to the specified layer's given page.</summary>
    private void RestoreDeletedAnnotation(int page, object item, AnnotationLayer? targetLayer = null)
    {
        var layer = targetLayer ?? ActiveLayer;
        if (layer == null) return;
        switch (item)
        {
            case TextAnnotation t:
                if (!layer.PageTexts.TryGetValue(page, out var texts)) { texts = []; layer.PageTexts[page] = texts; }
                texts.Add(t); layer.TextCount++; _totalTextCount++;
                break;
            case ShapeAnnotation s:
                if (!layer.PageShapes.TryGetValue(page, out var shapes)) { shapes = []; layer.PageShapes[page] = shapes; }
                shapes.Add(s); layer.ShapeCount++; _totalShapeCount++;
                break;
            case MeasurementAnnotation m:
                if (!layer.PageMeasurements.TryGetValue(page, out var ms)) { ms = []; layer.PageMeasurements[page] = ms; }
                ms.Add(m); layer.MeasurementCount++; _totalMeasurementCount++;
                break;
            case InkStroke ink:
                if (!layer.PageStrokes.TryGetValue(page, out var strokes)) { strokes = []; layer.PageStrokes[page] = strokes; }
                strokes.Add(ink); layer.StrokeCount++; _totalStrokeCount++;
                break;
        }
    }

    private void RemoveAnnotationFromLayer(int page, object item, AnnotationLayer? targetLayer = null)
    {
        var layer = targetLayer ?? ActiveLayer;
        if (layer == null) return;
        switch (item)
        {
            case TextAnnotation t:
                if (layer.PageTexts.TryGetValue(page, out var texts) && texts.Remove(t))
                { layer.TextCount = Math.Max(0, layer.TextCount - 1); _totalTextCount = Math.Max(0, _totalTextCount - 1); }
                break;
            case ShapeAnnotation s:
                if (layer.PageShapes.TryGetValue(page, out var shapes) && shapes.Remove(s))
                { layer.ShapeCount = Math.Max(0, layer.ShapeCount - 1); _totalShapeCount = Math.Max(0, _totalShapeCount - 1); }
                break;
            case MeasurementAnnotation m:
                if (layer.PageMeasurements.TryGetValue(page, out var ms) && ms.Remove(m))
                { layer.MeasurementCount = Math.Max(0, layer.MeasurementCount - 1); _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - 1); }
                break;
            case InkStroke ink:
                if (layer.PageStrokes.TryGetValue(page, out var strokes) && strokes.Remove(ink))
                { layer.StrokeCount = Math.Max(0, layer.StrokeCount - 1); _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1); }
                break;
        }
    }

    private static bool HitTestStroke(InkStroke stroke, Point pt, double threshold)
    {
        // Polylines have sparse points with straight segments — test segment proximity
        if (stroke.IsPolyline && stroke.Points.Count >= 2)
        {
            // Closed/fillable polylines: hit interior using polygon geometry (not bbox).
            if (stroke.IsClosed && stroke.Points.Count >= 3)
            {
                if (stroke.IsFilled && IsPointInPolygon(stroke.Points, pt))
                    return true;
            }
            for (int i = 0; i < stroke.Points.Count - 1; i++)
                if (DistanceToSegment(pt, stroke.Points[i], stroke.Points[i + 1]) <= threshold)
                    return true;
            if (stroke.IsClosed && stroke.Points.Count >= 3
                && DistanceToSegment(pt, stroke.Points[^1], stroke.Points[0]) <= threshold)
                return true;
            return false;
        }
        // Freehand/highlight: use segment distance for more natural geometry hit-testing.
        if (stroke.Points.Count >= 2)
        {
            double tol = Math.Max(threshold, stroke.Width * 0.7);
            for (int i = 0; i < stroke.Points.Count - 1; i++)
                if (DistanceToSegment(pt, stroke.Points[i], stroke.Points[i + 1]) <= tol)
                    return true;
            return false;
        }
        if (stroke.Points.Count == 1)
        {
            double dx = pt.X - stroke.Points[0].X;
            double dy = pt.Y - stroke.Points[0].Y;
            double tol = Math.Max(threshold, stroke.Width);
            return dx * dx + dy * dy <= tol * tol;
        }
        return false;
    }

    private static bool HitTestShape(ShapeAnnotation shape, Point pt, double threshold)
    {
        switch (shape.ShapeType)
        {
            case InlineAnnotationTool.Line:
            case InlineAnnotationTool.Arrow:
                return DistanceToSegment(pt, shape.Start, shape.End) <= threshold;

            case InlineAnnotationTool.Rectangle:
            {
                var r = NormalizedRect(shape.Start, shape.End);
                bool onEdge = DistanceToSegment(pt, r.TopLeft, r.TopRight) <= threshold
                    || DistanceToSegment(pt, r.TopRight, r.BottomRight) <= threshold
                    || DistanceToSegment(pt, r.BottomRight, r.BottomLeft) <= threshold
                    || DistanceToSegment(pt, r.BottomLeft, r.TopLeft) <= threshold;
                if (onEdge) return true;
                return shape.IsFilled && r.Contains(pt);
            }
            case InlineAnnotationTool.Ellipse:
            {
                var r = NormalizedRect(shape.Start, shape.End);
                double cx = r.X + r.Width / 2;
                double cy = r.Y + r.Height / 2;
                double rx = r.Width / 2;
                double ry = r.Height / 2;
                if (rx < 1 || ry < 1) return false;
                double ndx = (pt.X - cx) / rx;
                double ndy = (pt.Y - cy) / ry;
                double dist = Math.Sqrt(ndx * ndx + ndy * ndy);
                // Filled ellipses hit inside; outlines hit boundary only.
                if (shape.IsFilled && dist <= 1.0) return true;
                double normThreshold = threshold / Math.Min(rx, ry);
                return Math.Abs(dist - 1.0) <= normThreshold;
            }
            case InlineAnnotationTool.RevisionCloud:
            {
                var r = NormalizedRect(shape.Start, shape.End);
                bool onEdge = DistanceToSegment(pt, r.TopLeft, r.TopRight) <= threshold
                    || DistanceToSegment(pt, r.TopRight, r.BottomRight) <= threshold
                    || DistanceToSegment(pt, r.BottomRight, r.BottomLeft) <= threshold
                    || DistanceToSegment(pt, r.BottomLeft, r.TopLeft) <= threshold;
                if (onEdge) return true;
                return shape.IsFilled && r.Contains(pt);
            }
            case InlineAnnotationTool.Dot:
            {
                double dx = pt.X - shape.Start.X;
                double dy = pt.Y - shape.Start.Y;
                double hitR = Math.Max(shape.StrokeWidth * 2, threshold);
                return dx * dx + dy * dy <= hitR * hitR;
            }
        }
        return false;
    }

    private static bool IsPointInPolygon(IReadOnlyList<Point> poly, Point pt)
    {
        bool inside = false;
        int n = poly.Count;
        if (n < 3) return false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            var pi = poly[i];
            var pj = poly[j];
            double dy = pj.Y - pi.Y;
            if (Math.Abs(dy) < 1e-10) continue; // skip near-horizontal edges
            bool intersect = ((pi.Y > pt.Y) != (pj.Y > pt.Y))
                && (pt.X < (pj.X - pi.X) * (pt.Y - pi.Y) / dy + pi.X);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 0.001)
            return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        double t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq, 0, 1);
        double projX = a.X + t * dx;
        double projY = a.Y + t * dy;
        return Math.Sqrt((p.X - projX) * (p.X - projX) + (p.Y - projY) * (p.Y - projY));
    }

    private static bool NearlySamePoint(Point a, Point b, double epsilon = 0.01)
        => Math.Abs(a.X - b.X) <= epsilon && Math.Abs(a.Y - b.Y) <= epsilon;

    private static Rect NormalizedRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(b.X - a.X);
        double h = Math.Abs(b.Y - a.Y);
        return new Rect(x, y, w, h);
    }

    private static bool HitTestText(TextAnnotation text, Point pt)
        => GetTextBounds(text).Contains(pt);

    private static bool HitTestMeasurement(MeasurementAnnotation m, Point pt, double threshold)
    {
        var pts = m.Points;
        if (pts.Count < 2) return false;
        return DistanceToSegment(pt, pts[0], pts[1]) <= threshold;
    }

    #endregion

    #region Stroke Management (operates on active layer)

    public void Undo()
    {
        if (_undoStack.Count == 0) return;

        // Find the topmost entry for the current page AND active layer
        var shelved = new Stack<(UndoType type, int page, object? data, AnnotationLayer? layer)>();
        (UndoType type, int page, object? data, AnnotationLayer? layer)? found = null;
        while (_undoStack.Count > 0)
        {
            var entry = _undoStack.Pop();
            if (entry.page == _currentPage
                && (entry.layer == null || entry.layer == ActiveLayer))
            { found = entry; break; }
            shelved.Push(entry);
        }
        // Restore shelved entries
        while (shelved.Count > 0) _undoStack.Push(shelved.Pop());
        if (found is not { } f) return;

        // Use the layer stored in the entry (falls back to ActiveLayer)
        var (type, page, data, entryLayer) = f;
        var layer = entryLayer ?? ActiveLayer;
        if (layer == null) return;

        bool removed = false;
        object? item = null;

        switch (type)
        {
            case UndoType.Stroke:
                if (layer.PageStrokes.TryGetValue(page, out var strokes) && strokes.Count > 0)
                {
                    item = strokes[^1];
                    strokes.RemoveAt(strokes.Count - 1);
                    layer.StrokeCount = Math.Max(0, layer.StrokeCount - 1);
                    _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1);
                    removed = true;
                }
                break;
            case UndoType.Shape:
                if (layer.PageShapes.TryGetValue(page, out var shapes) && shapes.Count > 0)
                {
                    item = shapes[^1];
                    shapes.RemoveAt(shapes.Count - 1);
                    layer.ShapeCount = Math.Max(0, layer.ShapeCount - 1);
                    _totalShapeCount = Math.Max(0, _totalShapeCount - 1);
                    removed = true;
                }
                break;
            case UndoType.Text:
                if (layer.PageTexts.TryGetValue(page, out var texts) && texts.Count > 0)
                {
                    item = texts[^1];
                    texts.RemoveAt(texts.Count - 1);
                    layer.TextCount = Math.Max(0, layer.TextCount - 1);
                    _totalTextCount = Math.Max(0, _totalTextCount - 1);
                    removed = true;
                }
                break;
            case UndoType.Measurement:
                if (layer.PageMeasurements.TryGetValue(page, out var measurements) && measurements.Count > 0)
                {
                    item = measurements[^1];
                    measurements.RemoveAt(measurements.Count - 1);
                    layer.MeasurementCount = Math.Max(0, layer.MeasurementCount - 1);
                    _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - 1);
                    removed = true;
                }
                break;
            case UndoType.ClearPage:
                if (data is ClearPageSnapshot snap)
                {
                    if (snap.Strokes is { Count: > 0 })
                    {
                        if (!layer.PageStrokes.TryGetValue(page, out var rs)) { rs = []; layer.PageStrokes[page] = rs; }
                        rs.AddRange(snap.Strokes);
                        layer.StrokeCount += snap.Strokes.Count;
                        _totalStrokeCount += snap.Strokes.Count;
                    }
                    if (snap.Shapes is { Count: > 0 })
                    {
                        if (!layer.PageShapes.TryGetValue(page, out var rsh)) { rsh = []; layer.PageShapes[page] = rsh; }
                        rsh.AddRange(snap.Shapes);
                        layer.ShapeCount += snap.Shapes.Count;
                        _totalShapeCount += snap.Shapes.Count;
                    }
                    if (snap.Texts is { Count: > 0 })
                    {
                        if (!layer.PageTexts.TryGetValue(page, out var rt)) { rt = []; layer.PageTexts[page] = rt; }
                        rt.AddRange(snap.Texts);
                        layer.TextCount += snap.Texts.Count;
                        _totalTextCount += snap.Texts.Count;
                    }
                    if (snap.Measurements is { Count: > 0 })
                    {
                        if (!layer.PageMeasurements.TryGetValue(page, out var rm)) { rm = []; layer.PageMeasurements[page] = rm; }
                        rm.AddRange(snap.Measurements);
                        layer.MeasurementCount += snap.Measurements.Count;
                        _totalMeasurementCount += snap.Measurements.Count;
                    }
                    item = data;
                    removed = true;
                }
                break;
            case UndoType.Move:
                if (data is MoveSnapshot movSnap)
                {
                    var redoSnap = CapturePreDragSnapshot(movSnap.Item);
                    RestoreMoveSnapshot(movSnap);
                    item = redoSnap;
                    removed = true;
                }
                break;
            case UndoType.GroupMove:
                if (data is GroupMoveSnapshot groupMove && groupMove.Items.Count > 0)
                {
                    var redoItems = new List<MoveSnapshot>(groupMove.Items.Count);
                    foreach (var mv in groupMove.Items)
                    {
                        var rs = CapturePreDragSnapshot(mv.Item);
                        if (rs is MoveSnapshot rms)
                            redoItems.Add(rms);
                        RestoreMoveSnapshot(mv);
                    }
                    item = new GroupMoveSnapshot(redoItems);
                    removed = true;
                }
                break;
            case UndoType.GroupAdd:
                if (data is GroupAddSnapshot groupAddSnap && groupAddSnap.Items.Count > 0)
                {
                    // Remove every item that was placed as part of the group
                    foreach (var addedItem in groupAddSnap.Items)
                        RemoveAnnotationFromLayer(page, addedItem, layer);
                    item = groupAddSnap;
                    removed = true;
                    ClearSelectHighlight();
                }
                break;
            case UndoType.Delete:
                if (data != null)
                {
                    RestoreDeletedAnnotation(page, data, layer);
                    item = data;
                    removed = true;
                }
                break;
            case UndoType.PropertyChange:
                if (data is PropertySnapshot propSnap)
                {
                    var redoSnap = CapturePropertySnapshot(propSnap.Item);
                    RestorePropertySnapshot(propSnap);
                    item = redoSnap;
                    removed = true;
                }
                break;
            case UndoType.ZOrder:
                if (data is ZOrderSnapshot zSnap)
                {
                    var redoZ = CaptureZOrderSnapshot(zSnap.Item);
                    RestoreZOrder(zSnap);
                    item = redoZ;
                    removed = true;
                }
                break;
            case UndoType.GroupResize:
                if (data is List<GroupResizeSnapshot> preStates)
                {
                    // Capture current (post-resize) state for redo
                    var postStates = new List<GroupResizeSnapshot>(preStates.Count);
                    foreach (var s in preStates)
                        postStates.Add(CaptureGroupResizeSnapshot(s.Item));
                    // Restore pre-resize state
                    foreach (var s in preStates)
                        RestoreGroupResizeSnapshot(s);
                    item = postStates;
                    removed = true;
                }
                break;
            case UndoType.GroupPropertyChange:
                if (data is List<object> groupSnaps)
                {
                    var redoSnaps = new List<object>(groupSnaps.Count);
                    foreach (var sn in groupSnaps)
                    {
                        if (sn is PropertySnapshot ps)
                        {
                            var redo = CapturePropertySnapshot(ps.Item);
                            if (redo != null) redoSnaps.Add(redo);
                            RestorePropertySnapshot(ps);
                        }
                    }
                    item = redoSnaps;
                    removed = true;
                }
                break;
        }

        if (removed)
        {
            _redoStack.Push((type, page, item!, entryLayer));
            layer.RefreshStatus();
            InvalidateVisual();
            NotifyAnnotationChanged();
        }
    }

    /// <summary>Restores annotation positions from a MoveSnapshot.</summary>
    private static void RestoreMoveSnapshot(MoveSnapshot snap)
    {
        switch (snap.Item)
        {
            case TextAnnotation t:
                if (snap.Position.HasValue) t.Position = snap.Position.Value;
                t.ArrowOrigin = snap.ArrowOrigin;
                break;
            case ShapeAnnotation s:
                if (snap.ShapeStart.HasValue) s.Start = snap.ShapeStart.Value;
                if (snap.ShapeEnd.HasValue) s.End = snap.ShapeEnd.Value;
                s.InvalidatePen();
                break;
            case MeasurementAnnotation m when snap.Points != null:
                for (int i = 0; i < snap.Points.Length && i < m.Points.Count; i++)
                    m.Points[i] = snap.Points[i];
                break;
            case InkStroke ink when snap.Points != null:
                ink.Points.Clear();
                ink.Points.AddRange(snap.Points);
                ink.InvalidatePen();
                break;
        }
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;

        // Find the topmost entry for the current page AND active layer
        var shelved = new Stack<(UndoType type, int page, object item, AnnotationLayer? layer)>();
        (UndoType type, int page, object item, AnnotationLayer? layer)? found = null;
        while (_redoStack.Count > 0)
        {
            var entry = _redoStack.Pop();
            if (entry.page == _currentPage
                && (entry.layer == null || entry.layer == ActiveLayer))
            { found = entry; break; }
            shelved.Push(entry);
        }
        // Restore shelved entries
        while (shelved.Count > 0) _redoStack.Push(shelved.Pop());
        if (found is not { } f) return;

        var (type, page, item, entryLayer) = f;
        var layer = entryLayer ?? ActiveLayer;
        if (layer == null) return;

        bool restored = false;

        switch (type)
        {
            case UndoType.Stroke:
                if (item is InkStroke stroke)
                {
                    if (!layer.PageStrokes.TryGetValue(page, out var strokes))
                    {
                        strokes = [];
                        layer.PageStrokes[page] = strokes;
                    }
                    strokes.Add(stroke);
                    layer.StrokeCount++;
                    _totalStrokeCount++;
                    restored = true;
                }
                break;
            case UndoType.Shape:
                if (item is ShapeAnnotation shape)
                {
                    if (!layer.PageShapes.TryGetValue(page, out var shapes))
                    {
                        shapes = [];
                        layer.PageShapes[page] = shapes;
                    }
                    shapes.Add(shape);
                    layer.ShapeCount++;
                    _totalShapeCount++;
                    restored = true;
                }
                break;
            case UndoType.Text:
                if (item is TextAnnotation text)
                {
                    if (!layer.PageTexts.TryGetValue(page, out var texts))
                    {
                        texts = [];
                        layer.PageTexts[page] = texts;
                    }
                    texts.Add(text);
                    layer.TextCount++;
                    _totalTextCount++;
                    restored = true;
                }
                break;
            case UndoType.Measurement:
                if (item is MeasurementAnnotation measurement)
                {
                    if (!layer.PageMeasurements.TryGetValue(page, out var measurements))
                    {
                        measurements = [];
                        layer.PageMeasurements[page] = measurements;
                    }
                    measurements.Add(measurement);
                    layer.MeasurementCount++;
                    _totalMeasurementCount++;
                    restored = true;
                }
                break;
            case UndoType.ClearPage:
                if (item is ClearPageSnapshot snap)
                {
                    // Capture any annotations added after the undo so they aren't silently lost
                    List<InkStroke>? newStrokes = null;
                    List<ShapeAnnotation>? newShapes = null;
                    List<TextAnnotation>? newTexts = null;
                    List<MeasurementAnnotation>? newMeasurements = null;
                    if (layer.PageStrokes.TryGetValue(page, out var cs) && cs.Count > 0)
                    { newStrokes = new List<InkStroke>(cs); layer.StrokeCount -= cs.Count; _totalStrokeCount -= cs.Count; cs.Clear(); }
                    if (layer.PageShapes.TryGetValue(page, out var csh) && csh.Count > 0)
                    { newShapes = new List<ShapeAnnotation>(csh); layer.ShapeCount -= csh.Count; _totalShapeCount -= csh.Count; csh.Clear(); }
                    if (layer.PageTexts.TryGetValue(page, out var ct) && ct.Count > 0)
                    { newTexts = new List<TextAnnotation>(ct); layer.TextCount -= ct.Count; _totalTextCount -= ct.Count; ct.Clear(); }
                    if (layer.PageMeasurements.TryGetValue(page, out var cm) && cm.Count > 0)
                    { newMeasurements = new List<MeasurementAnnotation>(cm); layer.MeasurementCount -= cm.Count; _totalMeasurementCount -= cm.Count; cm.Clear(); }
                    // Push the captured state so undo can restore what was cleared by redo
                    var redoUndoSnap = new ClearPageSnapshot(newStrokes, newShapes, newTexts, newMeasurements);
                    _undoStack.Push((UndoType.ClearPage, page, redoUndoSnap, entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            case UndoType.Move:
                if (item is MoveSnapshot movRedoSnap)
                {
                    var undoSnap = CapturePreDragSnapshot(movRedoSnap.Item);
                    RestoreMoveSnapshot(movRedoSnap);
                    _undoStack.Push((UndoType.Move, page, undoSnap!, entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            case UndoType.GroupMove:
                if (item is GroupMoveSnapshot groupMoveRedo && groupMoveRedo.Items.Count > 0)
                {
                    var undoItems = new List<MoveSnapshot>(groupMoveRedo.Items.Count);
                    foreach (var mv in groupMoveRedo.Items)
                    {
                        var us = CapturePreDragSnapshot(mv.Item);
                        if (us is MoveSnapshot ums)
                            undoItems.Add(ums);
                        RestoreMoveSnapshot(mv);
                    }
                    _undoStack.Push((UndoType.GroupMove, page, new GroupMoveSnapshot(undoItems), entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            case UndoType.Delete:
            {
                // Redo delete = remove the item again
                bool didRemove = false;
                switch (item)
                {
                    case TextAnnotation t:
                        if (layer.PageTexts.TryGetValue(page, out var txts) && txts.Remove(t))
                        { layer.TextCount = Math.Max(0, layer.TextCount - 1); _totalTextCount = Math.Max(0, _totalTextCount - 1); didRemove = true; }
                        break;
                    case ShapeAnnotation s:
                        if (layer.PageShapes.TryGetValue(page, out var shps) && shps.Remove(s))
                        { layer.ShapeCount = Math.Max(0, layer.ShapeCount - 1); _totalShapeCount = Math.Max(0, _totalShapeCount - 1); didRemove = true; }
                        break;
                    case MeasurementAnnotation m:
                        if (layer.PageMeasurements.TryGetValue(page, out var mss) && mss.Remove(m))
                        { layer.MeasurementCount = Math.Max(0, layer.MeasurementCount - 1); _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - 1); didRemove = true; }
                        break;
                    case InkStroke ink:
                        if (layer.PageStrokes.TryGetValue(page, out var stks) && stks.Remove(ink))
                        { layer.StrokeCount = Math.Max(0, layer.StrokeCount - 1); _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1); didRemove = true; }
                        break;
                }
                if (didRemove)
                {
                    _undoStack.Push((UndoType.Delete, page, item, entryLayer));
                    layer.RefreshStatus();
                    ClearSelectHighlight();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            }
            case UndoType.PropertyChange:
                if (item is PropertySnapshot propRedoSnap)
                {
                    var undoPropSnap = CapturePropertySnapshot(propRedoSnap.Item);
                    RestorePropertySnapshot(propRedoSnap);
                    _undoStack.Push((UndoType.PropertyChange, page, undoPropSnap!, entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            case UndoType.ZOrder:
                if (item is ZOrderSnapshot zRedoSnap)
                {
                    var undoZ = CaptureZOrderSnapshot(zRedoSnap.Item);
                    RestoreZOrder(zRedoSnap);
                    _undoStack.Push((UndoType.ZOrder, page, undoZ!, entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            case UndoType.GroupResize:
                if (item is List<GroupResizeSnapshot> redoStates)
                {
                    // Capture current state for undo (so we can undo again)
                    var undoStates = new List<GroupResizeSnapshot>(redoStates.Count);
                    foreach (var s in redoStates)
                        undoStates.Add(CaptureGroupResizeSnapshot(s.Item));
                    // Restore the redo (post-resize) state
                    foreach (var s in redoStates)
                        RestoreGroupResizeSnapshot(s);
                    _undoStack.Push((UndoType.GroupResize, page, undoStates, entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            case UndoType.GroupPropertyChange:
                if (item is List<object> redoGroupSnaps)
                {
                    var undoGroupSnaps = new List<object>(redoGroupSnaps.Count);
                    foreach (var sn in redoGroupSnaps)
                    {
                        if (sn is PropertySnapshot ps)
                        {
                            var undoSnap = CapturePropertySnapshot(ps.Item);
                            if (undoSnap != null) undoGroupSnaps.Add(undoSnap);
                            RestorePropertySnapshot(ps);
                        }
                    }
                    _undoStack.Push((UndoType.GroupPropertyChange, page, undoGroupSnaps, entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
            case UndoType.GroupAdd:
                if (item is GroupAddSnapshot groupAddRedo && groupAddRedo.Items.Count > 0)
                {
                    foreach (var addedItem in groupAddRedo.Items)
                        RestoreDeletedAnnotation(page, addedItem, layer);
                    _undoStack.Push((UndoType.GroupAdd, page, groupAddRedo, entryLayer));
                    layer.RefreshStatus();
                    InvalidateVisual();
                    NotifyAnnotationChanged();
                    return;
                }
                break;
        }

        if (restored)
        {
            _undoStack.Push((type, page, null, entryLayer));
            layer.RefreshStatus();
            InvalidateVisual();
            NotifyAnnotationChanged();
        }
    }

    public void ClearPage()
    {
        if (ActiveLayer == null) return;

        List<InkStroke>? savedStrokes = null;
        List<ShapeAnnotation>? savedShapes = null;
        List<TextAnnotation>? savedTexts = null;
        List<MeasurementAnnotation>? savedMeasurements = null;

        if (ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes) && strokes.Count > 0)
        {
            savedStrokes = new List<InkStroke>(strokes);
            ActiveLayer.StrokeCount = Math.Max(0, ActiveLayer.StrokeCount - strokes.Count);
            _totalStrokeCount = Math.Max(0, _totalStrokeCount - strokes.Count);
            strokes.Clear();
        }
        if (ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes) && shapes.Count > 0)
        {
            savedShapes = new List<ShapeAnnotation>(shapes);
            ActiveLayer.ShapeCount = Math.Max(0, ActiveLayer.ShapeCount - shapes.Count);
            _totalShapeCount = Math.Max(0, _totalShapeCount - shapes.Count);
            shapes.Clear();
        }
        if (ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts) && texts.Count > 0)
        {
            savedTexts = new List<TextAnnotation>(texts);
            ActiveLayer.TextCount = Math.Max(0, ActiveLayer.TextCount - texts.Count);
            _totalTextCount = Math.Max(0, _totalTextCount - texts.Count);
            texts.Clear();
        }
        if (ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var measurements) && measurements.Count > 0)
        {
            savedMeasurements = new List<MeasurementAnnotation>(measurements);
            ActiveLayer.MeasurementCount = Math.Max(0, ActiveLayer.MeasurementCount - measurements.Count);
            _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - measurements.Count);
            measurements.Clear();
        }

        if (savedStrokes != null || savedShapes != null || savedTexts != null || savedMeasurements != null)
        {
            var snapshot = new ClearPageSnapshot(savedStrokes, savedShapes, savedTexts, savedMeasurements);
            _undoStack.Push((UndoType.ClearPage, _currentPage, snapshot, ActiveLayer));
            _redoStack.Clear();
            ActiveLayer.RefreshStatus();
            InvalidateVisual();
            NotifyAnnotationChanged();
        }
    }

    public void ClearAll()
    {
        foreach (var layer in Layers)
        {
            layer.PageStrokes.Clear();
            layer.PageShapes.Clear();
            layer.PageTexts.Clear();
            layer.PageMeasurements.Clear();
            layer.StrokeCount = 0;
            layer.ShapeCount = 0;
            layer.TextCount = 0;
            layer.MeasurementCount = 0;
            layer.RefreshStatus();
        }
        _totalStrokeCount = 0;
        _totalShapeCount = 0;
        _totalTextCount = 0;
        _totalMeasurementCount = 0;
        _undoStack.Clear();
        _redoStack.Clear();
        InvalidateVisual();
        NotifyAnnotationChanged();
    }

    /// <summary>Get all visible strokes for a page across all layers.</summary>
    public IReadOnlyList<InkStroke> GetStrokes(int page)
    {
        var result = new List<InkStroke>();
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageStrokes.TryGetValue(page, out var strokes))
                result.AddRange(strokes);
        }
        return result;
    }

    /// <summary>Get all visible shapes for a page across all layers.</summary>
    public IReadOnlyList<ShapeAnnotation> GetShapes(int page)
    {
        var result = new List<ShapeAnnotation>();
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageShapes.TryGetValue(page, out var shapes))
                result.AddRange(shapes);
        }
        return result;
    }

    /// <summary>Get all visible text annotations for a page across all layers.</summary>
    public IReadOnlyList<TextAnnotation> GetTexts(int page)
    {
        var result = new List<TextAnnotation>();
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageTexts.TryGetValue(page, out var texts))
                result.AddRange(texts);
        }
        return result;
    }

    /// <summary>Get all visible measurements for a page across all layers.</summary>
    public IReadOnlyList<MeasurementAnnotation> GetMeasurements(int page)
    {
        var result = new List<MeasurementAnnotation>();
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageMeasurements.TryGetValue(page, out var measurements))
                result.AddRange(measurements);
        }
        return result;
    }

    #endregion

    #region Rendering

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Apply dark-mode inversion AFTER the PDF content but BEFORE annotations
        // so that annotations retain their original colors.
        if (IsInverted)
            context.Custom(new InvertContentDrawOp(new Rect(Bounds.Size), InvertBackgroundColor, InvertTintColor, InvertTintIntensity));

        // Fast path: skip annotation rendering entirely when there's nothing to draw.
        // HasDiffOverlay is checked separately since it's independent of annotations.
        if (!HasDiffOverlay && !HasAnyStrokes) return;

        try
        {
            RenderAnnotations(context);
        }
        catch (Exception ex)
        {
            Finn.Utils.ErrorLogger.Log(ex, "AnnotatedPDFRenderer.Render");
        }
    }

    private void RenderAnnotations(DrawingContext context)
    {
        // Draw the diff overlay between PDF content and annotations.
        // Capture the image reference locally so a concurrent ClearDiffOverlay
        // on another thread cannot null it between the HasDiffOverlay check
        // and the actual draw op construction.
        var diffImage = _diffOverlayImage;
        if (DiffOverlayVisible && diffImage != null && _diffOverlayPage == _currentPage)
        {
            var oda = DisplayArea;
            var obs = Bounds.Size;
            if (oda.Width > 0 && oda.Height > 0 && obs.Width > 0 && obs.Height > 0)
            {
                context.Custom(new DiffOverlayDrawOp(
                    new Rect(obs), diffImage, oda, DiffOverlayOpacity, _diffImageZoom));
            }
        }

        if (!IsViewerInitialized || !HasAnyStrokes) return;

        var da = DisplayArea;
        var boundsSize = Bounds.Size;
        if (da.Width <= 0 || da.Height <= 0 ||
            boundsSize.Width <= 0 || boundsSize.Height <= 0) return;

        double scaleX = boundsSize.Width / da.Width;
        double scaleY = boundsSize.Height / da.Height;
        double penScale = (scaleX + scaleY) * 0.5;

        // Precomputed values for the fast PdfToScreen overload
        double offsetX = da.X, offsetY = da.Y;

        // Rebuild scale-dependent chrome pens only when penScale changes
        if (_cachedSelectHoverPen == null || Math.Abs(_cachedChromePenScale - penScale) > 0.05)
        {
            _cachedChromePenScale = penScale;
            _cachedSelectHoverPen = new Pen(s_selectHoverBrush,
                1.0 * penScale, dashStyle: s_dashStyle4_3, lineCap: PenLineCap.Round);
            _cachedSelectionPen = new Pen(s_selectPenBrush,
                1.5, dashStyle: s_dashStyle5_4,
                lineCap: PenLineCap.Round);
            _cachedEraserHoverPen = new Pen(s_eraserHoverPenBrush,
                2 * penScale, lineCap: PenLineCap.Round);
        }

        // Draw snap-to-grid dots (behind annotations, very subtle)
        if (SnapToGrid && GridSpacing > 0)
            RenderGridDots(context, da, boundsSize, scaleX, scaleY);

        // Collect text items for the SkiaSharp overlay pass (reuse pooled list)
        _textItemPool.Clear();
        var textItems = _textItemPool;

        // Collect all annotations from all visible layers into a single list sorted by ZIndex
        // so that cross-type z-ordering (e.g. text behind shape) works correctly.
        var allAnnotations = new List<(int Z, object Item, AnnotationLayer Layer)>();
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageStrokes.TryGetValue(_currentPage, out var strokes))
                foreach (var s in strokes) allAnnotations.Add((s.ZIndex, s, layer));
            if (layer.PageShapes.TryGetValue(_currentPage, out var shapes))
                foreach (var s in shapes) allAnnotations.Add((s.ZIndex, s, layer));
            if (layer.PageMeasurements.TryGetValue(_currentPage, out var measurements))
                foreach (var m in measurements) allAnnotations.Add((m.ZIndex, m, layer));
            if (layer.PageTexts.TryGetValue(_currentPage, out var texts))
                foreach (var t in texts) allAnnotations.Add((t.ZIndex, t, layer));
        }
        allAnnotations.Sort((a, b) => a.Z.CompareTo(b.Z));

        foreach (var (_, item, _) in allAnnotations)
        {
            switch (item)
            {
                case InkStroke stroke:
                    RenderStroke(context, stroke, da, boundsSize, scaleX, scaleY, penScale);
                    if (stroke.IsAreaMeasure && stroke.IsClosed && stroke.Points.Count >= 3)
                        CollectAreaLabel(stroke, da, boundsSize, penScale, textItems);
                    break;
                case ShapeAnnotation shape:
                    RenderShape(context, shape, da, boundsSize, scaleX, scaleY, penScale);
                    break;
                case MeasurementAnnotation m:
                    RenderMeasurementGeometry(context, m.Points, m.Color, da, boundsSize, penScale, m);
                    CollectMeasurementLabel(m, da, boundsSize, penScale, textItems);
                    break;
                case TextAnnotation t:
                    if (t.IsStickyNote)
                        CollectStickyNoteIcon(t, da, boundsSize, penScale, textItems, isExpanded: false);
                    else if (t.IsLabel)
                        CollectLabelAnnotation(t, da, boundsSize, penScale, textItems);
                    else
                    {
                        CollectTextAnnotation(t, da, boundsSize, penScale, textItems);
                        if (t.ArrowOrigin.HasValue)
                            RenderTextArrow(context, t, da, boundsSize, penScale);
                    }
                    break;
            }
        }

        // Draw the active stroke being drawn
        if (_activeStroke != null)
            RenderStroke(context, _activeStroke, da, boundsSize, scaleX, scaleY, penScale);

        // Draw the active polyline being constructed (with preview segment)
        if (_activePolyline != null)
        {
            RenderStroke(context, _activePolyline, da, boundsSize, scaleX, scaleY, penScale);
            // Preview segment from last committed point to cursor
            if (_polylinePreviewEnd.HasValue && _activePolyline.Points.Count > 0)
            {
                var lastPt = PdfToScreen(_activePolyline.Points[^1], da, boundsSize);
                var previewPt = PdfToScreen(_polylinePreviewEnd.Value, da, boundsSize);
                double pw = StrokeWidth * penScale;
                if (_cachedPreviewPen == null || _cachedPreviewColor != StrokeColor
                    || Math.Abs(_cachedPreviewWidth - pw) > 0.5)
                {
                    _cachedPreviewColor = StrokeColor;
                    _cachedPreviewWidth = pw;
                    _cachedPreviewPen = new Pen(
                        new SolidColorBrush(Color.FromArgb(140, StrokeColor.R, StrokeColor.G, StrokeColor.B)).ToImmutable(),
                        pw, dashStyle: s_dashStyle4_3, lineCap: PenLineCap.Round);
                }
                context.DrawLine(_cachedPreviewPen, lastPt, previewPt);
            }
            // Live area label while drawing
            if (_activePolyline.IsAreaMeasure && _activePolyline.Points.Count >= 3)
                CollectAreaLabel(_activePolyline, da, boundsSize, penScale, textItems);
        }

        // Draw the active shape being dragged (dashed preview)
        if (_activeShape != null)
        {
            var dashPen = _activeShape.GetOrCreateDashedPen(penScale);
            RenderShape(context, _activeShape, da, boundsSize, scaleX, scaleY, penScale, dashPen);
        }

        // Draw the active measurement being constructed
        if (_activeMeasurement != null)
        {
            var pts = _activeMeasurement.Points;
            if (pts.Count >= 2)
            {
                RenderMeasurementGeometry(context, pts,
                    _activeMeasurement.Color, da, boundsSize, penScale);

                var previewM = new MeasurementAnnotation
                {
                    Points = pts,
                    Scale = _activeMeasurement.Scale,
                    Color = _activeMeasurement.Color
                };
                CollectMeasurementLabel(previewM, da, boundsSize, penScale, textItems);
            }
        }

        // Draw ArrowText preview line (anchor to cursor)
        if (_arrowTextPreviewOrigin.HasValue && _lastPointerPdfPos.HasValue)
        {
            var from = PdfToScreen(_arrowTextPreviewOrigin.Value, da, boundsSize);
            var to = PdfToScreen(_lastPointerPdfPos.Value, da, boundsSize);
            double pw = StrokeWidth * penScale;
            if (_cachedPreviewPen == null || _cachedPreviewColor != StrokeColor
                || Math.Abs(_cachedPreviewWidth - 1.2) > 0.5)
            {
                _cachedPreviewColor = StrokeColor;
                _cachedPreviewWidth = 1.2;
                _cachedPreviewPen = new Pen(new SolidColorBrush(StrokeColor).ToImmutable(),
                    1.2, lineCap: PenLineCap.Round);
            }
            context.DrawLine(_cachedPreviewPen, from, to);
            DrawArrowhead(context, _cachedPreviewPen, to, from, penScale);
        }

        // Hover popup for the text annotation under the cursor (drawn above all other items)
        if (_stickyNoteHoverItem != null)
        {
            if (_stickyNoteHoverItem.IsStickyNote)
                CollectStickyNoteIcon(_stickyNoteHoverItem, da, boundsSize, penScale, textItems, isExpanded: true);
            else
                CollectTextHoverPopup(_stickyNoteHoverItem, da, boundsSize, penScale, textItems);
        }

        // Text / Sticky / ArrowText placement ghost at cursor
        if (_textPlacementPreviewPos.HasValue && _textPlacementPreviewTool.HasValue)
        {
            var ghostPos = PdfToScreen(_textPlacementPreviewPos.Value, da, boundsSize);
            var ghostTool = _textPlacementPreviewTool.Value;
            if (ghostTool == InlineAnnotationTool.StickyNote)
            {
                float iconSz = (float)(13.0 * penScale);
                var ghostColor = new SKColor(255, 235, 59, 120);
                textItems.Add(new TextOverlayDrawOp.TextItem(
                    (float)ghostPos.X, (float)ghostPos.Y, "", iconSz, ghostColor,
                    HasBackground: false, HasBorder: false, IsTextAnnotation: false,
                    IsStickyNote: true, IsExpandedStickyNote: false));
            }
            else
            {
                // Ghost text box frame (empty "Aa" placeholder)
                float ghostFontSz = (float)(TextFontSize * penScale);
                var ghostColor = new SKColor(StrokeColor.R, StrokeColor.G, StrokeColor.B, 100);
                float baselineY = (float)ghostPos.Y + ghostFontSz;
                textItems.Add(new TextOverlayDrawOp.TextItem(
                    (float)ghostPos.X, baselineY, "Aa", ghostFontSz, ghostColor,
                    HasBackground: true, HasBorder: true, IsTextAnnotation: true));
            }
        }

        // Render all text via SkiaSharp overlay.
        // Double-buffered: swap between two pre-allocated lists so the
        // render thread reads the previous frame's list while we populate the next.
        if (textItems.Count > 0)
        {
            var snapshot = _useSnapshotA ? _textSnapshotA : _textSnapshotB;
            _useSnapshotA = !_useSnapshotA;
            snapshot.Clear();
            snapshot.AddRange(textItems);
            context.Custom(new TextOverlayDrawOp(new Rect(boundsSize), snapshot));
        }

        // Eraser hover highlight: draw a translucent red overlay on the hovered item
        if (_eraserHoverItem != null)
            RenderEraserHover(context, da, boundsSize, scaleX, scaleY, penScale);

        // Select-mode hover outline: dotted bounding-box around the hovered annotation
        if (_selectHoverItem != null && !_selectHighlightItems.Contains(_selectHoverItem))
        {
            var hoverPen = _cachedSelectHoverPen!;
            double ox = da.X, oy = da.Y;
            Rect? hoverBounds = null;
            switch (_selectHoverItem)
            {
                case InkStroke hoverStroke when hoverStroke.Points.Count > 0:
                {
                    double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                    foreach (var p in hoverStroke.Points)
                    {
                        var sp = PdfToScreen(p, ox, oy, scaleX, scaleY);
                        minX = Math.Min(minX, sp.X); minY = Math.Min(minY, sp.Y);
                        maxX = Math.Max(maxX, sp.X); maxY = Math.Max(maxY, sp.Y);
                    }
                    hoverBounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                    break;
                }
                case ShapeAnnotation hoverShape:
                {
                    var hs = PdfToScreen(hoverShape.Start, ox, oy, scaleX, scaleY);
                    var he = PdfToScreen(hoverShape.End, ox, oy, scaleX, scaleY);
                    hoverBounds = new Rect(Math.Min(hs.X, he.X), Math.Min(hs.Y, he.Y),
                        Math.Abs(he.X - hs.X), Math.Abs(he.Y - hs.Y));
                    break;
                }
                case TextAnnotation hoverText:
                {
                    var hp = PdfToScreen(hoverText.Position, ox, oy, scaleX, scaleY);
                    if (hoverText.IsStickyNote)
                    {
                        double sz = 13.0 * penScale;
                        hoverBounds = new Rect(hp.X, hp.Y, sz, sz);
                    }
                    else
                    {
                        var htb = GetTextBounds(hoverText);
                        hoverBounds = new Rect(hp.X, hp.Y, htb.Width * penScale, htb.Height * penScale);
                    }
                    break;
                }
                case MeasurementAnnotation hoverMeas when hoverMeas.Points.Count >= 2:
                {
                    var hm0 = PdfToScreen(hoverMeas.Points[0], ox, oy, scaleX, scaleY);
                    var hm1 = PdfToScreen(hoverMeas.Points[1], ox, oy, scaleX, scaleY);
                    hoverBounds = new Rect(Math.Min(hm0.X, hm1.X), Math.Min(hm0.Y, hm1.Y),
                        Math.Abs(hm1.X - hm0.X), Math.Abs(hm1.Y - hm0.Y));
                    break;
                }
            }
            if (hoverBounds is { } hb)
                context.DrawRectangle(null, hoverPen, hb.Inflate(5), 3, 3);
        }

        // Pen cursor preview: colored circle showing pen size at cursor position
        if (_cursorPdfPos.HasValue && ActiveTool is InlineAnnotationTool.Draw or InlineAnnotationTool.Highlight)
        {
            var cp = PdfToScreen(_cursorPdfPos.Value, da, boundsSize);
            double radius = StrokeWidth * 0.5 * penScale;
            if (_cachedCursorBrush == null || _cachedCursorColor != StrokeColor)
            {
                _cachedCursorColor = StrokeColor;
                _cachedCursorBrush = new SolidColorBrush(
                    Color.FromArgb(160, StrokeColor.R, StrokeColor.G, StrokeColor.B)).ToImmutable();
                _cachedCrosshairPen = new Pen(new SolidColorBrush(
                    Color.FromArgb(180, StrokeColor.R, StrokeColor.G, StrokeColor.B)).ToImmutable(), 1.0);
            }
            context.DrawEllipse(_cachedCursorBrush, null, cp, radius, radius);
        }
        // Crosshair cursor preview for shape/line/measurement/polyline tools
        else if (_cursorPdfPos.HasValue
            && ActiveTool is InlineAnnotationTool.Rectangle or InlineAnnotationTool.Ellipse
                or InlineAnnotationTool.Line or InlineAnnotationTool.Arrow
                or InlineAnnotationTool.RevisionCloud or InlineAnnotationTool.MeasureDistance
                or InlineAnnotationTool.Polyline or InlineAnnotationTool.Dot)
        {
            var cp = PdfToScreen(_cursorPdfPos.Value, da, boundsSize);
            double arm = 8;
            if (_cachedCrosshairPen == null || _cachedCursorColor != StrokeColor)
            {
                _cachedCursorColor = StrokeColor;
                _cachedCursorBrush = new SolidColorBrush(
                    Color.FromArgb(160, StrokeColor.R, StrokeColor.G, StrokeColor.B)).ToImmutable();
                _cachedCrosshairPen = new Pen(new SolidColorBrush(
                    Color.FromArgb(180, StrokeColor.R, StrokeColor.G, StrokeColor.B)).ToImmutable(), 1.0);
            }
            context.DrawLine(_cachedCrosshairPen, new Point(cp.X - arm, cp.Y), new Point(cp.X + arm, cp.Y));
            context.DrawLine(_cachedCrosshairPen, new Point(cp.X, cp.Y - arm), new Point(cp.X, cp.Y + arm));
            context.DrawEllipse(null, _cachedCrosshairPen, cp, 3, 3);
        }

        // Snap-to-alignment guides: thin dotted lines across the viewport
        if (_snapGuideX.HasValue || _snapGuideY.HasValue)
        {
            var guidePen = s_snapGuidePen;
            double dotRadius = 3.5;
            double snapIconR = 5.5;
            if (_snapGuideX.HasValue)
            {
                var sx = PdfToScreen(new Point(_snapGuideX.Value, 0), da, boundsSize).X;
                context.DrawLine(guidePen, new Point(sx, 0), new Point(sx, boundsSize.Height));
            }
            if (_snapGuideY.HasValue)
            {
                var sy = PdfToScreen(new Point(0, _snapGuideY.Value), da, boundsSize).Y;
                context.DrawLine(guidePen, new Point(0, sy), new Point(boundsSize.Width, sy));
            }
            // Intersection dot at snap point
            if (_snapGuideX.HasValue && _snapGuideY.HasValue)
            {
                var dotPos = PdfToScreen(new Point(_snapGuideX.Value, _snapGuideY.Value), da, boundsSize);
                context.DrawEllipse(s_snapBrush, null, dotPos, dotRadius, dotRadius);
            }
            else if (_snapGuideX.HasValue && _snapVertexPos.HasValue)
            {
                var dotPos = PdfToScreen(new Point(_snapGuideX.Value, _snapVertexPos.Value.Y), da, boundsSize);
                context.DrawEllipse(s_snapBrush, null, dotPos, dotRadius, dotRadius);
            }
            else if (_snapGuideY.HasValue && _snapVertexPos.HasValue)
            {
                var dotPos = PdfToScreen(new Point(_snapVertexPos.Value.X, _snapGuideY.Value), da, boundsSize);
                context.DrawEllipse(s_snapBrush, null, dotPos, dotRadius, dotRadius);
            }

            // ── Snap type indicator icon at the snapped vertex position ──
            if (_snapVertexPos.HasValue)
            {
                var sv = PdfToScreen(_snapVertexPos.Value, da, boundsSize);
                var snapKind = _snapKindX != SnapKind.None ? _snapKindX : _snapKindY;
                DrawSnapIcon(context, sv, snapKind, snapIconR, s_snapVertexBrush, s_snapVertexPen);
            }
        }

        // ── Polar tracking readout: distance + angle near the cursor ──
        if (_polarAngleDeg.HasValue && _polarDistancePdf.HasValue && _polarCursorScreen.HasValue)
        {
            double distMm = _polarDistancePdf.Value * MeasurementScale;
            string distStr = distMm >= 1000.0 ? $"{distMm / 1000.0:F2} m" : $"{distMm:F1} mm";
            string label = $"{distStr}  ∠{_polarAngleDeg.Value:F1}°";

            var typeface = new Avalonia.Media.Typeface(
                Avalonia.Media.FontFamily.Default, Avalonia.Media.FontStyle.Normal,
                Avalonia.Media.FontWeight.Normal);
            var ft = new Avalonia.Media.FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                Avalonia.Media.FlowDirection.LeftToRight, typeface, 11, Avalonia.Media.Brushes.White);

            double pw = ft.Width + 10, ph = ft.Height + 6;
            double lx = _polarCursorScreen.Value.X + 14;
            double ly = _polarCursorScreen.Value.Y - ph - 6;
            if (lx + pw > boundsSize.Width) lx = _polarCursorScreen.Value.X - pw - 6;
            if (ly < 0) ly = _polarCursorScreen.Value.Y + 14;

            context.DrawRectangle(s_polarBgBrush, s_polarBorderPen,
                new Rect(lx, ly, pw, ph), 3, 3);
            context.DrawText(ft, new Point(lx + 5, ly + 3));
        }

        // Rubber-band marquee selection rectangle
        if (_screenshotMode && !_rubberBandStart.HasValue)
        {
            // Full-surface veil before any drag has started
            context.DrawRectangle(s_screenshotVeilBrush, null,
                new Rect(0, 0, boundsSize.Width, boundsSize.Height));
        }
        else if (_rubberBandStart.HasValue && _rubberBandEnd.HasValue)
        {
            var rs = PdfToScreen(_rubberBandStart.Value, da, boundsSize);
            var re = PdfToScreen(_rubberBandEnd.Value, da, boundsSize);
            var rect = new Rect(Math.Min(rs.X, re.X), Math.Min(rs.Y, re.Y),
                Math.Abs(re.X - rs.X), Math.Abs(re.Y - rs.Y));

            if (_screenshotMode)
            {
                // Draw dim veil over the four regions surrounding the selection,
                // leaving the selected area fully visible.
                var full = new Rect(0, 0, boundsSize.Width, boundsSize.Height);
                // Top strip
                if (rect.Top > 0)
                    context.DrawRectangle(s_screenshotVeilBrush, null,
                        new Rect(0, 0, full.Width, rect.Top));
                // Bottom strip
                if (rect.Bottom < full.Height)
                    context.DrawRectangle(s_screenshotVeilBrush, null,
                        new Rect(0, rect.Bottom, full.Width, full.Height - rect.Bottom));
                // Left strip (between top and bottom)
                if (rect.Left > 0)
                    context.DrawRectangle(s_screenshotVeilBrush, null,
                        new Rect(0, rect.Top, rect.Left, rect.Height));
                // Right strip (between top and bottom)
                if (rect.Right < full.Width)
                    context.DrawRectangle(s_screenshotVeilBrush, null,
                        new Rect(rect.Right, rect.Top, full.Width - rect.Right, rect.Height));
                // Selection border
                context.DrawRectangle(null, s_screenshotBorderPen, rect);
            }
            else
            {
                var fillBrush = _rubberBandCrossing ? s_rubberBandCrossingFillBrush : s_rubberBandFillBrush;
                var borderPen = _rubberBandCrossing ? s_rubberBandCrossingPen : s_rubberBandPen;
                context.DrawRectangle(fillBrush, borderPen, rect);
            }
        }

        // Selection handles: dashed bounding box around selected items
        if (_selectHighlightItems.Count > 0)
            RenderSelectionHighlight(context, da, boundsSize, scaleX, scaleY, penScale);
    }

    private void RenderSelectionHighlight(DrawingContext context, Rect da, Size boundsSize,
                                          double scaleX, double scaleY, double penScale)
    {
        // Selection UI uses constant screen-pixel sizes so handles don't
        // balloon when zoomed in or shrink when zoomed out.
        var selectPen = _cachedSelectionPen!;
        var vertexPen = s_vertexPen;
        bool single = _selectHighlightItems.Count == 1;
        const double vtxSize = 7.0;     // constant screen pixels
        const double cornerSize = 4.5;  // constant screen pixels
        const double padSize = 4.0;     // constant screen pixels
        double ox = da.X, oy = da.Y;

        // Determine if ALL highlighted items belong to a single group.
        // If so, we render only the combined bounding box (not per-item boxes).
        bool isGroupSelection = false;
        if (_selectHighlightItems.Count >= 2)
        {
            HashSet<object>? firstGroup = null;
            isGroupSelection = true;
            foreach (var item in _selectHighlightItems)
            {
                var g = GetGroup(item);
                if (g == null) { isGroupSelection = false; break; }
                firstGroup ??= g;
                if (!ReferenceEquals(g, firstGroup)) { isGroupSelection = false; break; }
            }
        }

        // For grouped items, skip individual bounding boxes
        if (!isGroupSelection)
        {
            foreach (var highlightItem in _selectHighlightItems)
            {
                Rect? bounds = null;
                switch (highlightItem)
                {
                    case InkStroke stroke:
                    {
                        if (stroke.Points.Count == 0) break;
                        double minX = double.MaxValue, minY = double.MaxValue;
                        double maxX = double.MinValue, maxY = double.MinValue;
                        foreach (var p in stroke.Points)
                        {
                            var sp = PdfToScreen(p, ox, oy, scaleX, scaleY);
                            minX = Math.Min(minX, sp.X); minY = Math.Min(minY, sp.Y);
                            maxX = Math.Max(maxX, sp.X); maxY = Math.Max(maxY, sp.Y);
                        }
                        bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                        break;
                    }
                    case ShapeAnnotation shape:
                    {
                        // Dots are point objects — show only a selection ring, no bounding box.
                        if (shape.ShapeType == InlineAnnotationTool.Dot)
                        {
                            var sc = PdfToScreen(shape.Start, ox, oy, scaleX, scaleY);
                            double dotR = shape.StrokeWidth * penScale + padSize;
                            context.DrawEllipse(null, selectPen, sc, dotR, dotR);
                            break;
                        }
                        var s = PdfToScreen(shape.Start, ox, oy, scaleX, scaleY);
                        var e = PdfToScreen(shape.End, ox, oy, scaleX, scaleY);
                        bounds = new Rect(
                            Math.Min(s.X, e.X), Math.Min(s.Y, e.Y),
                            Math.Abs(e.X - s.X), Math.Abs(e.Y - s.Y));
                        break;
                    }
                    case TextAnnotation t:
                    {
                        var sp = PdfToScreen(t.Position, ox, oy, scaleX, scaleY);
                        if (t.IsStickyNote)
                        {
                            double sz = 13.0 * penScale;
                            bounds = new Rect(sp.X, sp.Y, sz, sz);
                        }
                        else
                        {
                            var tb = GetTextBounds(t);
                            bounds = new Rect(sp.X, sp.Y, tb.Width * penScale, tb.Height * penScale);
                        }
                        break;
                    }
                    case MeasurementAnnotation m:
                    {
                        if (m.Points.Count >= 2)
                        {
                            var s0 = PdfToScreen(m.Points[0], ox, oy, scaleX, scaleY);
                            var s1 = PdfToScreen(m.Points[1], ox, oy, scaleX, scaleY);
                            bounds = new Rect(
                                Math.Min(s0.X, s1.X), Math.Min(s0.Y, s1.Y),
                                Math.Abs(s1.X - s0.X), Math.Abs(s1.Y - s0.Y));
                        }
                        break;
                    }
                }

                if (bounds is { } b)
                {
                    var inflated = b.Inflate(padSize);
                    context.DrawRectangle(null, selectPen, inflated);

                    if (single)
                    {
                        context.DrawEllipse(s_cornerBrush, null, inflated.TopLeft, cornerSize, cornerSize);
                        context.DrawEllipse(s_cornerBrush, null, inflated.TopRight, cornerSize, cornerSize);
                        context.DrawEllipse(s_cornerBrush, null, inflated.BottomLeft, cornerSize, cornerSize);
                        context.DrawEllipse(s_cornerBrush, null, inflated.BottomRight, cornerSize, cornerSize);

                        if (highlightItem is TextAnnotation { ArrowOrigin: { } ao })
                        {
                            var arrowScreen = PdfToScreen(ao, ox, oy, scaleX, scaleY);
                            context.DrawEllipse(s_vertexBrush, vertexPen, arrowScreen, vtxSize, vtxSize);
                        }
                        if (highlightItem is ShapeAnnotation selShape
                            && selShape.ShapeType != InlineAnnotationTool.Dot)
                        {
                            var ss = PdfToScreen(selShape.Start, ox, oy, scaleX, scaleY);
                            var se = PdfToScreen(selShape.End, ox, oy, scaleX, scaleY);
                            context.DrawEllipse(s_vertexBrush, vertexPen, ss, vtxSize, vtxSize);
                            context.DrawEllipse(s_vertexBrush, vertexPen, se, vtxSize, vtxSize);
                        }
                        if (highlightItem is MeasurementAnnotation selMeas && selMeas.Points.Count >= 2)
                        {
                            var mp0 = PdfToScreen(selMeas.Points[0], ox, oy, scaleX, scaleY);
                            var mp1 = PdfToScreen(selMeas.Points[1], ox, oy, scaleX, scaleY);
                            context.DrawEllipse(s_vertexBrush, vertexPen, mp0, vtxSize, vtxSize);
                            context.DrawEllipse(s_vertexBrush, vertexPen, mp1, vtxSize, vtxSize);
                        }
                        if (highlightItem is InkStroke { IsPolyline: true } selPoly)
                        {
                            foreach (var p in selPoly.Points)
                            {
                                var sp = PdfToScreen(p, ox, oy, scaleX, scaleY);
                                context.DrawEllipse(s_vertexBrush, vertexPen, sp, vtxSize, vtxSize);
                            }
                        }
                        // Text width resize handle: right-center edge
                        if (highlightItem is TextAnnotation { IsStickyNote: false } selTextResize && bounds is { } tb2)
                        {
                            var inf2 = tb2.Inflate(padSize);
                            var midRight = new Point(inf2.Right, (inf2.Top + inf2.Bottom) / 2);
                            context.DrawEllipse(s_vertexBrush, vertexPen, midRight, vtxSize, vtxSize);
                        }
                    }
                }
            }
        } // end if (!isGroupSelection)

        // Draw combined bounding box with resize handles for multi-selection or group selection
        if (_selectHighlightItems.Count >= 2)
        {
            var combinedPdf = GetCombinedBounds(_selectHighlightItems);
            if (combinedPdf is { Width: > 0 } or { Height: > 0 })
            {
                var ctl = PdfToScreen(combinedPdf.TopLeft, ox, oy, scaleX, scaleY);
                var cbr = PdfToScreen(combinedPdf.BottomRight, ox, oy, scaleX, scaleY);
                var combinedScreen = new Rect(
                    Math.Min(ctl.X, cbr.X), Math.Min(ctl.Y, cbr.Y),
                    Math.Abs(cbr.X - ctl.X), Math.Abs(cbr.Y - ctl.Y))
                    .Inflate(padSize + 3);
                context.DrawRectangle(null, s_groupPen, combinedScreen, 2, 2);

                // Corner resize handles
                const double resizeHandleSize = 5.0;
                context.DrawRectangle(s_vertexBrush, vertexPen,
                    new Rect(combinedScreen.TopLeft.X - resizeHandleSize, combinedScreen.TopLeft.Y - resizeHandleSize,
                        resizeHandleSize * 2, resizeHandleSize * 2));
                context.DrawRectangle(s_vertexBrush, vertexPen,
                    new Rect(combinedScreen.TopRight.X - resizeHandleSize, combinedScreen.TopRight.Y - resizeHandleSize,
                        resizeHandleSize * 2, resizeHandleSize * 2));
                context.DrawRectangle(s_vertexBrush, vertexPen,
                    new Rect(combinedScreen.BottomLeft.X - resizeHandleSize, combinedScreen.BottomLeft.Y - resizeHandleSize,
                        resizeHandleSize * 2, resizeHandleSize * 2));
                context.DrawRectangle(s_vertexBrush, vertexPen,
                    new Rect(combinedScreen.BottomRight.X - resizeHandleSize, combinedScreen.BottomRight.Y - resizeHandleSize,
                        resizeHandleSize * 2, resizeHandleSize * 2));
            }
        }
    }

    /// <summary>
    /// Draws subtle dots at every grid intersection within the visible viewport.
    /// Performance-capped: skips rendering when dots would be too dense or too numerous.
    /// </summary>
    private void RenderGridDots(DrawingContext context, Rect da, Size boundsSize,
                                double scaleX, double scaleY)
    {
        double g = GridSpacing;
        double stepPx = Math.Min(g * scaleX, g * scaleY);
        if (stepPx < 8) return; // too dense to be useful

        double startX = Math.Ceiling(da.X / g) * g;
        double startY = Math.Ceiling(da.Y / g) * g;
        double endX = da.X + da.Width;
        double endY = da.Y + da.Height;

        // Cap dot count to keep rendering fast
        int countX = (int)Math.Ceiling((endX - startX) / g);
        int countY = (int)Math.Ceiling((endY - startY) / g);
        if (countX > 100 || countY > 100) return;

        // Reuse cached geometry when viewport and spacing haven't changed
        if (_cachedGridGeometry == null || _cachedGridDa != da
            || Math.Abs(_cachedGridSpacing - g) > 0.001)
        {
            double dotRadius = 1.0;
            double offsetX = da.X, offsetY = da.Y;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (double py = startY; py <= endY; py += g)
                {
                    double sy = (py - offsetY) * scaleY;
                    for (double px = startX; px <= endX; px += g)
                    {
                        double sx = (px - offsetX) * scaleX;
                        var center = new Point(sx, sy);
                        ctx.BeginFigure(new Point(center.X + dotRadius, center.Y), true);
                        ctx.ArcTo(new Point(center.X - dotRadius, center.Y),
                            new Size(dotRadius, dotRadius), 0, false, SweepDirection.Clockwise);
                        ctx.ArcTo(new Point(center.X + dotRadius, center.Y),
                            new Size(dotRadius, dotRadius), 0, false, SweepDirection.Clockwise);
                        ctx.EndFigure(true);
                    }
                }
            }
            _cachedGridGeometry = geometry;
            _cachedGridDa = da;
            _cachedGridSpacing = g;
        }
        context.DrawGeometry(s_gridDotBrush, null, _cachedGridGeometry);
    }

    private void RenderEraserHover(DrawingContext context, Rect da, Size boundsSize,
                                    double scaleX, double scaleY, double penScale)
    {
        var hoverPen = _cachedEraserHoverPen!;

        switch (_eraserHoverItem)
        {
            case InkStroke stroke:
                RenderStroke(context, stroke, da, boundsSize, scaleX, scaleY, penScale, hoverPen);
                break;
            case ShapeAnnotation shape:
                RenderShape(context, shape, da, boundsSize, scaleX, scaleY, penScale, hoverPen);
                break;
            case TextAnnotation text:
            {
                double ox = da.X, oy = da.Y;
                var screenPos = PdfToScreen(text.Position, ox, oy, scaleX, scaleY);
                if (text.IsStickyNote)
                {
                    double sz = 13.0 * penScale;
                    context.DrawRectangle(s_eraserHoverBrush, hoverPen, new Rect(screenPos.X, screenPos.Y, sz, sz), 4, 4);
                }
                else
                {
                    var tb = GetTextBounds(text);
                    double w = tb.Width * penScale;
                    double h = tb.Height * penScale;
                    context.DrawRectangle(s_eraserHoverBrush, hoverPen, new Rect(screenPos.X, screenPos.Y, w, h), 4, 4);
                }
                break;
            }
            case MeasurementAnnotation m:
                if (m.Points.Count >= 2)
                {
                    double ox = da.X, oy = da.Y;
                    var s0 = PdfToScreen(m.Points[0], ox, oy, scaleX, scaleY);
                    var s1 = PdfToScreen(m.Points[1], ox, oy, scaleX, scaleY);
                    context.DrawLine(hoverPen, s0, s1);
                }
                break;
        }
    }

    private void RenderStroke(DrawingContext context, InkStroke stroke,
                              Rect da, Size boundsSize,
                              double scaleX, double scaleY, double penScale,
                              IPen? overridePen = null)
    {
        var pts = stroke.Points;
        if (pts.Count < 2) return;

        var pen = overridePen ?? stroke.GetOrCreatePen(penScale);

        bool closed = stroke.IsClosed && stroke.IsPolyline && pts.Count >= 3;

        // Try to reuse cached geometry when the view transform and points haven't changed.
        // This avoids rebuilding StreamGeometry (incl. Catmull-Rom splines) every frame
        // for committed strokes during static renders (hover, mode toggle, etc.).
        double offsetX = da.X, offsetY = da.Y;
        var geometry = stroke.CachedGeometry;
        bool cacheHit = geometry != null
            && stroke.CachedGeometryPointCount == pts.Count
            && stroke.CachedGeometryClosed == closed
            && stroke.CachedGeoScaleX == scaleX && stroke.CachedGeoScaleY == scaleY
            && stroke.CachedGeoOffX == offsetX && stroke.CachedGeoOffY == offsetY;

        if (!cacheHit)
        {
            // Pre-transform all points to screen space once (fast overload avoids per-point division)
            int n = pts.Count;
            Span<Point> sp = n <= 256 ? stackalloc Point[n] : new Point[n];
            for (int i = 0; i < n; i++)
                sp[i] = PdfToScreen(pts[i], offsetX, offsetY, scaleX, scaleY);

            geometry = BuildStrokeGeometry(stroke, sp, n, closed, penScale);

            // Only cache for committed strokes (not the active stroke being drawn)
            if (overridePen == null)
            {
                stroke.CachedGeometry = geometry;
                stroke.CachedGeometryClosed = closed;
                stroke.CachedGeoScaleX = scaleX;
                stroke.CachedGeoScaleY = scaleY;
                stroke.CachedGeoOffX = offsetX;
                stroke.CachedGeoOffY = offsetY;
                stroke.CachedGeometryPointCount = pts.Count;
            }
        }

        // For closed polylines with fill, draw a translucent fill
        IBrush? fillBrush = null;
        if (closed && stroke.IsFilled && overridePen == null)
            fillBrush = stroke.GetOrCreateFillBrush();
        context.DrawGeometry(fillBrush, pen, geometry!);
    }

    /// <summary>Builds a StreamGeometry from pre-transformed screen-space points.</summary>
    private static StreamGeometry BuildStrokeGeometry(InkStroke stroke, Span<Point> sp, int n, bool closed, double penScale)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (n == 2 || stroke.IsPolyline)
            {
                double cr = stroke.CornerRadius * penScale;
                if (cr > 0.5 && stroke.IsPolyline && n >= 3)
                {
                    // Determine effective point count: for closed shapes where
                    // last == first, skip the duplicated closing point.
                    int vn = n;
                    if (closed)
                    {
                        var f = sp[0]; var l = sp[n - 1];
                        if (Math.Abs(f.X - l.X) < 1 && Math.Abs(f.Y - l.Y) < 1)
                            vn = n - 1;
                    }

                    // Build the rounded-corner figure.
                    // For closed: round all vertices, start figure at the arc-start of vertex 0.
                    // For open: start at sp[0], round interior vertices, end at sp[n-1].

                    Point figureStart;
                    if (closed && vn >= 3)
                    {
                        var prev0 = sp[(vn - 1) % vn];
                        var curr0 = sp[0];
                        var next0 = sp[1];
                        double r0 = ComputeArcRadius(prev0, curr0, next0, cr);
                        if (r0 >= 0.5)
                        {
                            double dxIn0 = curr0.X - prev0.X, dyIn0 = curr0.Y - prev0.Y;
                            double lenIn0 = Math.Sqrt(dxIn0 * dxIn0 + dyIn0 * dyIn0);
                            double dxOut0 = next0.X - curr0.X, dyOut0 = next0.Y - curr0.Y;
                            double lenOut0 = Math.Sqrt(dxOut0 * dxOut0 + dyOut0 * dyOut0);
                            figureStart = new Point(curr0.X - dxIn0 / lenIn0 * r0, curr0.Y - dyIn0 / lenIn0 * r0);
                            ctx.BeginFigure(figureStart, closed);
                            var arcEnd0 = new Point(curr0.X + dxOut0 / lenOut0 * r0, curr0.Y + dyOut0 / lenOut0 * r0);
                            ctx.QuadraticBezierTo(curr0, arcEnd0);
                        }
                        else
                        {
                            ctx.BeginFigure(curr0, closed);
                        }
                    }
                    else
                    {
                        ctx.BeginFigure(sp[0], closed);
                    }

                    // Interior vertices (or all vertices for closed)
                    int loopStart = 1;
                    int loopEnd = closed ? vn : n - 1;
                    for (int i = loopStart; i < loopEnd; i++)
                    {
                        var prev = sp[(i - 1 + vn) % vn];
                        var curr = sp[i % vn];
                        var next = sp[(i + 1) % vn];

                        double r = ComputeArcRadius(prev, curr, next, cr);
                        if (r < 0.5)
                        {
                            ctx.LineTo(curr);
                            continue;
                        }
                        double dxIn = curr.X - prev.X, dyIn = curr.Y - prev.Y;
                        double lenIn = Math.Sqrt(dxIn * dxIn + dyIn * dyIn);
                        double dxOut = next.X - curr.X, dyOut = next.Y - curr.Y;
                        double lenOut = Math.Sqrt(dxOut * dxOut + dyOut * dyOut);
                        var arcStart = new Point(curr.X - dxIn / lenIn * r, curr.Y - dyIn / lenIn * r);
                        var arcEnd = new Point(curr.X + dxOut / lenOut * r, curr.Y + dyOut / lenOut * r);
                        ctx.LineTo(arcStart);
                        ctx.QuadraticBezierTo(curr, arcEnd);
                    }

                    if (!closed)
                        ctx.LineTo(sp[n - 1]);
                }
                else
                {
                    ctx.BeginFigure(sp[0], closed);
                    for (int i = 1; i < n; i++)
                        ctx.LineTo(sp[i]);
                }
            }
            else
            {
                ctx.BeginFigure(sp[0], false);
                // Catmull-Rom spline → cubic Bézier on pre-transformed points
                for (int i = 0; i < n - 1; i++)
                {
                    var pm1 = sp[Math.Max(i - 1, 0)];
                    var pi  = sp[i];
                    var pi1 = sp[i + 1];
                    var pi2 = sp[Math.Min(i + 2, n - 1)];

                    ctx.CubicBezierTo(
                        new Point(pi.X + (pi1.X - pm1.X) / 6.0,
                                  pi.Y + (pi1.Y - pm1.Y) / 6.0),
                        new Point(pi1.X - (pi2.X - pi.X) / 6.0,
                                  pi1.Y - (pi2.Y - pi.Y) / 6.0),
                        pi1);
                }
            }
            ctx.EndFigure(closed);
        }
        return geometry;
    }

    /// <summary>Computes the effective arc radius for a polyline vertex, clamped to half the shorter adjacent segment.</summary>
    private static double ComputeArcRadius(Point prev, Point curr, Point next, double cr)
    {
        double dxIn = curr.X - prev.X, dyIn = curr.Y - prev.Y;
        double lenIn = Math.Sqrt(dxIn * dxIn + dyIn * dyIn);
        double dxOut = next.X - curr.X, dyOut = next.Y - curr.Y;
        double lenOut = Math.Sqrt(dxOut * dxOut + dyOut * dyOut);
        if (lenIn < 1 || lenOut < 1) return 0;
        return Math.Min(cr, Math.Min(lenIn, lenOut) * 0.5);
    }

    private void RenderShape(DrawingContext context, ShapeAnnotation shape,
                             Rect da, Size boundsSize,
                             double scaleX, double scaleY, double penScale,
                             IPen? overridePen = null)
    {
        var pen = overridePen ?? shape.GetOrCreatePen(penScale);

        double offsetX = da.X, offsetY = da.Y;
        var screenStart = PdfToScreen(shape.Start, offsetX, offsetY, scaleX, scaleY);
        var screenEnd = PdfToScreen(shape.End, offsetX, offsetY, scaleX, scaleY);

        // Create optional fill brush for filled shapes (cached on the annotation)
        IBrush? fillBrush = null;
        if (shape.IsFilled && overridePen == null)
            fillBrush = shape.GetOrCreateFillBrush();

        switch (shape.ShapeType)
        {
            case InlineAnnotationTool.Line:
                context.DrawLine(pen, screenStart, screenEnd);
                break;

            case InlineAnnotationTool.Arrow:
                context.DrawLine(pen, screenStart, screenEnd);
                DrawArrowhead(context, pen, screenStart, screenEnd, penScale);
                DrawTailDot(context, pen, screenStart, penScale);
                break;

            case InlineAnnotationTool.Rectangle:
            {
                double x = Math.Min(screenStart.X, screenEnd.X);
                double y = Math.Min(screenStart.Y, screenEnd.Y);
                double w = Math.Abs(screenEnd.X - screenStart.X);
                double h = Math.Abs(screenEnd.Y - screenStart.Y);
                double cr = shape.CornerRadius * penScale;
                context.DrawRectangle(fillBrush, pen, new Rect(x, y, w, h), cr, cr);
                break;
            }
            case InlineAnnotationTool.Ellipse:
            {
                double cx = (screenStart.X + screenEnd.X) / 2;
                double cy = (screenStart.Y + screenEnd.Y) / 2;
                double rx = Math.Abs(screenEnd.X - screenStart.X) / 2;
                double ry = Math.Abs(screenEnd.Y - screenStart.Y) / 2;
                context.DrawEllipse(fillBrush, pen, new Point(cx, cy), rx, ry);
                break;
            }
            case InlineAnnotationTool.RevisionCloud:
            {
                var cloudGeometry = shape.GetOrCreateCloudGeometry(screenStart, screenEnd, penScale);
                context.DrawGeometry(fillBrush, pen, cloudGeometry);
                break;
            }
            case InlineAnnotationTool.Dot:
            {
                // Dot radius is based on StrokeWidth (diameter = StrokeWidth * 2 screen units)
                // Dots are always rendered as solid filled circles — no outline/dash pen.
                double r = shape.StrokeWidth * penScale;
                context.DrawEllipse(shape.GetOrCreateDotBrush(), null, screenStart, r, r);
                break;
            }
        }
    }

    private static void DrawArrowhead(DrawingContext context, IPen pen,
                                      Point from, Point to, double penScale)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double headLen = Math.Min(8 * penScale, len * 0.4);
        double headAngle = Math.PI / 8; // 22.5 degrees

        double angle = Math.Atan2(dy, dx);
        var left = new Point(
            to.X - headLen * Math.Cos(angle - headAngle),
            to.Y - headLen * Math.Sin(angle - headAngle));
        var right = new Point(
            to.X - headLen * Math.Cos(angle + headAngle),
            to.Y - headLen * Math.Sin(angle + headAngle));

        var arrowGeometry = new StreamGeometry();
        using (var ctx = arrowGeometry.Open())
        {
            ctx.BeginFigure(left, true);
            ctx.LineTo(to);
            ctx.LineTo(right);
            ctx.EndFigure(true);
        }

        // Always draw the arrowhead with a solid pen regardless of the line body's dash pattern.
        var solidPen = new Pen(pen.Brush, pen.Thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        context.DrawGeometry(pen.Brush, solidPen, arrowGeometry);
    }

    /// <summary>Draws a small filled circle at the tail (origin) of an arrow.</summary>
    private static void DrawTailDot(DrawingContext context, IPen pen, Point origin, double penScale)
    {
        double r = Math.Max(2.0, pen.Thickness * 0.9);
        context.DrawEllipse(pen.Brush, null, origin, r, r);
    }

    /// <summary>
    /// Renders the arrow line from a TextAnnotation's ArrowOrigin to its Position.
    /// The arrow connects to the center of the closest side of the text bounding box.
    /// Uses SkiaSharp font measurement so the box matches the frame drawn by TextOverlayDrawOp.
    /// </summary>
    private void RenderTextArrow(DrawingContext context, TextAnnotation t,
                                 Rect da, Size boundsSize, double penScale)
    {
        if (!t.ArrowOrigin.HasValue) return;
        var arrowTip = PdfToScreen(t.ArrowOrigin.Value, da, boundsSize);
        var boxOrigin = PdfToScreen(t.Position, da, boundsSize);

        // Use GetTextBounds which accounts for MaxWidth word-wrapping,
        // then measure actual rendered lines with SkiaSharp for pixel accuracy.
        float fontSize = (float)(t.FontSize * penScale);
        float lineHeight = fontSize * 1.3f;
        float x = (float)boxOrigin.X;
        float y = (float)boxOrigin.Y + fontSize;

        string fontFamily = t.FontFamily ?? "";
        var typeface = GetCachedTypeface(fontFamily);
        using var skFont = new SKFont(typeface, fontSize);

        // Word-wrap if MaxWidth is set, otherwise split on explicit newlines
        List<string> lines;
        if (t.MaxWidth > 0)
        {
            float scaleX = da.Width > 0 ? (float)(boundsSize.Width / da.Width) : (float)penScale;
            float maxWidthPx = (float)(t.MaxWidth * scaleX);
            lines = WrapTextLines(t.Text, maxWidthPx, skFont);
        }
        else
        {
            lines = [.. t.Text.Split('\n')];
        }

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                skFont.MeasureText(line, out var tb);
                minX = Math.Min(minX, x + tb.Left);
                minY = Math.Min(minY, y + tb.Top);
                maxX = Math.Max(maxX, x + tb.Left + tb.Width);
                maxY = Math.Max(maxY, y + tb.Top + tb.Height);
            }
            y += lineHeight;
        }

        if (minX >= maxX) return; // no measurable text

        float pad = 5;
        float accentW = 4;
        var boxRect = new Rect(minX - pad - accentW, minY - pad,
            (maxX - minX) + pad * 2 + accentW, (maxY - minY) + pad * 2);

        var connection = ClosestSideCenter(boxRect, arrowTip);

        byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
        var c = Color.FromArgb(alpha, t.Color.R, t.Color.G, t.Color.B);
        var pen = t.GetOrCreateArrowPen(penScale);
        context.DrawLine(pen, arrowTip, connection);
        DrawArrowhead(context, pen, connection, arrowTip, penScale);
    }

    /// <summary>Returns the center point of the rectangle side closest to the given point.</summary>
    private static Point ClosestSideCenter(Rect rect, Point pt)
    {
        Span<Point> candidates =
        [
            new(rect.X + rect.Width / 2, rect.Y),                    // top
            new(rect.X + rect.Width / 2, rect.Y + rect.Height),      // bottom
            new(rect.X, rect.Y + rect.Height / 2),                   // left
            new(rect.X + rect.Width, rect.Y + rect.Height / 2)       // right
        ];
        Point best = candidates[0];
        double bestDist = double.MaxValue;
        foreach (var c in candidates)
        {
            double dx = c.X - pt.X;
            double dy = c.Y - pt.Y;
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = c; }
        }
        return best;
    }

    private void RenderMeasurementGeometry(DrawingContext context, List<Point> pdfPoints,
                                            Color color, Rect da, Size boundsSize, double penScale,
                                            MeasurementAnnotation? annotation = null)
    {
        if (pdfPoints.Count < 2) return;

        IPen solidPen;
        Color c;
        if (annotation != null)
        {
            var (pen, _) = annotation.GetOrCreatePen(penScale);
            solidPen = pen;
            c = Color.FromArgb(220, color.R, color.G, color.B);
        }
        else
        {
            c = Color.FromArgb(220, color.R, color.G, color.B);
            var brush = new SolidColorBrush(c).ToImmutable();
            solidPen = new Pen(brush, 1.5 * penScale,
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        }

        var s0 = PdfToScreen(pdfPoints[0], da, boundsSize);
        var s1 = PdfToScreen(pdfPoints[1], da, boundsSize);
        context.DrawLine(solidPen, s0, s1);
        DrawEndMark(context, solidPen, s0, s1, penScale);
        DrawEndMark(context, solidPen, s1, s0, penScale);
        DrawMeasureArrowhead(context, c, s0, s1, penScale);
        DrawMeasureArrowhead(context, c, s1, s0, penScale);
    }

    private static void DrawMeasureArrowhead(DrawingContext context, Color color,
                                              Point tip, Point from, double penScale)
    {
        double dx = tip.X - from.X;
        double dy = tip.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        double headLen = Math.Min(6 * penScale, len * 0.3);
        double angle = Math.Atan2(dy, dx);
        const double half = Math.PI / 7;
        var p1 = new Point(tip.X - headLen * Math.Cos(angle - half),
                           tip.Y - headLen * Math.Sin(angle - half));
        var p2 = new Point(tip.X - headLen * Math.Cos(angle + half),
                           tip.Y - headLen * Math.Sin(angle + half));
        var geo = new StreamGeometry();
        using (var ctx2 = geo.Open())
        {
            ctx2.BeginFigure(p1, true);
            ctx2.LineTo(tip);
            ctx2.LineTo(p2);
            ctx2.EndFigure(true);
        }
        // Use the pen's existing brush (same color) instead of allocating a new one
        var brush = new SolidColorBrush(color).ToImmutable();
        context.DrawGeometry(brush, null, geo);
    }

    private static void DrawEndMark(DrawingContext context, IPen pen,
                                    Point at, Point toward, double penScale)
    {
        double dx = toward.X - at.X;
        double dy = toward.Y - at.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;

        double perpX = -dy / len;
        double perpY = dx / len;
        double markLen = 5 * penScale;

        context.DrawLine(pen,
            new Point(at.X - perpX * markLen, at.Y - perpY * markLen),
            new Point(at.X + perpX * markLen, at.Y + perpY * markLen));
    }

    private void CollectStickyNoteIcon(TextAnnotation t, Rect da, Size boundsSize,
                                        double penScale, List<TextOverlayDrawOp.TextItem> items,
                                        bool isExpanded)
    {
        var iconPos = PdfToScreen(t.Position, da, boundsSize);
        float iconSize = (float)(13.0 * penScale);
        byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
        var color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha);
        items.Add(new TextOverlayDrawOp.TextItem(
            (float)iconPos.X, (float)iconPos.Y, t.Text, iconSize, color,
            HasBackground: false, HasBorder: false, IsTextAnnotation: false,
            IsStickyNote: true, IsExpandedStickyNote: isExpanded,
            PopupFontSize: (float)(t.FontSize * penScale)));
    }

    private void CollectTextAnnotation(TextAnnotation t, Rect da, Size boundsSize,
                                        double penScale, List<TextOverlayDrawOp.TextItem> items)
    {
        var screenPos = PdfToScreen(t.Position, da, boundsSize);
        float fontSize = (float)(t.FontSize * penScale);
        byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
        var color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha);
        string fontFamily = t.FontFamily ?? "";

        var typeface = GetCachedTypeface(fontFamily);
        using var skFont = new SKFont(typeface, fontSize);

        // Word-wrap if MaxWidth is set, otherwise split on explicit newlines
        List<string> lines;
        if (t.MaxWidth > 0)
        {
            float scaleX = da.Width > 0 ? (float)(boundsSize.Width / da.Width) : (float)penScale;
            float maxWidthPx = (float)(t.MaxWidth * scaleX);
            lines = WrapTextLines(t.Text, maxWidthPx, skFont);
        }
        else
        {
            lines = [.. t.Text.Split('\n')];
        }

        float lineHeight = fontSize * 1.3f;
        float y = (float)screenPos.Y + fontSize;
        bool first = true;

        // Track pixel-accurate extents for cached bounds
        float measuredMaxW = 0;

        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                float lineW = skFont.MeasureText(line);
                measuredMaxW = Math.Max(measuredMaxW, lineW);
                items.Add(new TextOverlayDrawOp.TextItem(
                    (float)screenPos.X, y, line, fontSize, color,
                    HasBackground: true, HasBorder: first, IsTextAnnotation: true,
                    FontFamily: fontFamily));
                first = false;
            }
            y += lineHeight;
        }

        // Cache pixel-accurate bounds back to the annotation (in PDF units)
        // so hit-testing and selection boxes match the actual rendered text.
        if (penScale > 0.001)
        {
            t.MeasuredWidth = measuredMaxW / penScale;
            t.MeasuredHeight = lines.Count * t.FontSize * 1.3;
        }
    }

    /// <summary>
    /// Collects a label-style text annotation. Uses a fixed screen-space font
    /// size (doesn't grow with zoom) and centres the text at the annotation's
    /// PDF position. Rendered with a subtle rounded background pill, no textbox
    /// frame. Used for diff version labels.
    /// </summary>
    private void CollectLabelAnnotation(TextAnnotation t, Rect da, Size boundsSize,
                                        double penScale, List<TextOverlayDrawOp.TextItem> items)
    {
        var screenPos = PdfToScreen(t.Position, da, boundsSize);
        // Fixed screen-space font size so the label stays readable at any zoom.
        float fontSize = (float)t.FontSize;
        byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
        var color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha);
        string fontFamily = t.FontFamily ?? "";

        var typeface = GetCachedTypeface(fontFamily);
        using var skFont = new SKFont(typeface, fontSize);
        float textWidth = skFont.MeasureText(t.Text);

        // Centre the text horizontally at the annotation position.
        float x = (float)screenPos.X - textWidth * 0.5f;
        float y = (float)screenPos.Y + fontSize;

        items.Add(new TextOverlayDrawOp.TextItem(
            x, y, t.Text, fontSize, color,
            HasBackground: true, HasBorder: false, IsTextAnnotation: false,
            FontFamily: fontFamily));

        // Update cached bounds for hit-testing (in PDF units).
        if (penScale > 0.001)
        {
            t.MeasuredWidth = textWidth / penScale;
            t.MeasuredHeight = t.FontSize * 1.3;
        }
    }

    private void CollectMeasurementLabel(MeasurementAnnotation m, Rect da, Size boundsSize,
                                         double penScale, List<TextOverlayDrawOp.TextItem> items)
    {
        if (m.Points.Count < 2) return;

        // Convert both endpoints to screen space so we can compute the
        // perpendicular direction in screen coordinates (pixel-accurate offset).
        var s0 = PdfToScreen(m.Points[0], da, boundsSize);
        var s1 = PdfToScreen(m.Points[1], da, boundsSize);

        double dx = s1.X - s0.X;
        double dy = s1.Y - s0.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);

        // Midpoint in screen space
        double midX = (s0.X + s1.X) * 0.5;
        double midY = (s0.Y + s1.Y) * 0.5;

        // Perpendicular unit vector (rotated 90°). Then bias toward screen-up
        // (negative Y) so the label consistently floats above the line.
        double perpX, perpY;
        if (len > 0.5)
        {
            perpX = -dy / len;
            perpY =  dx / len;
            // Flip if pointing downward (positive Y in screen space)
            if (perpY > 0) { perpX = -perpX; perpY = -perpY; }
        }
        else
        {
            perpX = 0; perpY = -1; // degenerate: just go up
        }

        // Offset = font height + a small gap so the label clears the line and its end-marks
        float fontSize = (float)(10 * penScale);
        double offset = fontSize * 1.4 + 4 * penScale;
        double labelX = midX + perpX * offset;
        double labelY = midY + perpY * offset;

        var color = new SKColor(m.Color.R, m.Color.G, m.Color.B);
        items.Add(new TextOverlayDrawOp.TextItem(
            (float)labelX, (float)labelY, m.GetLabel(), fontSize, color,
            HasBackground: true, HasBorder: false, IsTextAnnotation: false,
            TintBackground: true, CenterOnPoint: true));
    }

    /// <summary>Computes the signed area (PDF-space pt²) of a polygon using the shoelace formula.</summary>
    private static double ComputePolygonAreaPt2(List<Point> pts)
    {
        int n = pts.Count;
        if (n < 3) return 0;
        double area = 0;
        for (int i = 0; i < n; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % n];
            area += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(area) * 0.5;
    }

    /// <summary>Computes the perimeter (PDF-space pt) of a closed polygon.</summary>
    private static double ComputePolygonPerimeterPt(List<Point> pts)
    {
        int n = pts.Count;
        if (n < 2) return 0;
        double perim = 0;
        for (int i = 0; i < n; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % n];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            perim += Math.Sqrt(dx * dx + dy * dy);
        }
        return perim;
    }

    /// <summary>Renders an area label at the centroid of a closed area-measure polyline.</summary>
    private void CollectAreaLabel(InkStroke stroke, Rect da, Size boundsSize,
                                  double penScale, List<TextOverlayDrawOp.TextItem> items)
    {
        var pts = stroke.Points;
        if (pts.Count < 3) return;

        // Centroid
        double cx = 0, cy = 0;
        foreach (var p in pts) { cx += p.X; cy += p.Y; }
        cx /= pts.Count; cy /= pts.Count;
        var screenPos = PdfToScreen(new Point(cx, cy), da, boundsSize);

        double scaleMmPerPt = stroke.AreaScale;   // mm/pt

        // Area label — ⊟ (U+22DF filled square) prefix to identify the value as area
        double areaPt2 = ComputePolygonAreaPt2(pts);
        double areaMm2 = areaPt2 * scaleMmPerPt * scaleMmPerPt;
        string areaLabel = "\u25A0 " + FormatArea(areaMm2);

        // Perimeter label
        double perimPt = ComputePolygonPerimeterPt(pts);
        double perimMm = perimPt * scaleMmPerPt;
        string perimLabel = "\u2299 " + FormatLength(perimMm);

        float fontSize = (float)(10 * penScale);
        var color = new SKColor(stroke.Color.R, stroke.Color.G, stroke.Color.B);

        items.Add(new TextOverlayDrawOp.TextItem(
            (float)screenPos.X, (float)screenPos.Y,
            areaLabel, fontSize, color,
            HasBackground: true, HasBorder: false, IsTextAnnotation: false,
            TintBackground: true,
            SecondLine: perimLabel,
            CenterOnPoint: true));
    }

    private static string FormatArea(double mm2)
    {
        if (mm2 >= 1_000_000)
            return $"{mm2 / 1_000_000:F2} m²";
        if (mm2 >= 10_000)
            return $"{mm2 / 10_000:F2} dm²";
        if (mm2 >= 100)
            return $"{mm2 / 100:F2} cm²";
        return $"{mm2:F1} mm²";
    }

    private static string FormatLength(double mm)
    {
        if (mm >= 1000) return $"{mm / 1000:F2} m";
        if (mm >= 10)   return $"{mm:F1} mm";
        return $"{mm:F2} mm";
    }


    /// <summary>Collect a hover popup for a non-sticky text annotation (shown on hover like sticky notes).</summary>
    private void CollectTextHoverPopup(TextAnnotation t, Rect da, Size boundsSize,
                                       double penScale, List<TextOverlayDrawOp.TextItem> items)
    {
        var screenPos = PdfToScreen(t.Position, da, boundsSize);
        float fontSize = (float)(t.FontSize * penScale);
        byte alpha = t.Opacity < 1.0 ? (byte)(t.Opacity * 255) : (byte)255;
        var color = new SKColor(t.Color.R, t.Color.G, t.Color.B, alpha);
        items.Add(new TextOverlayDrawOp.TextItem(
            (float)screenPos.X, (float)screenPos.Y, t.Text, fontSize, color,
            HasBackground: false, HasBorder: false, IsTextAnnotation: false,
            IsStickyNote: true, IsExpandedStickyNote: true, IsPopupOnly: true,
            PopupFontSize: fontSize));
    }

    /// <summary>
    /// Custom draw operation that inverts colors of everything already drawn
    /// (the PDF content) using the same Skia pipeline formerly in InvertColorControl.
    /// Inserted between <c>base.Render</c> and <c>RenderAnnotations</c> so that
    /// annotations are drawn AFTER the inversion and retain their original colors.
    /// </summary>
    private sealed class InvertContentDrawOp(Rect bounds, Color backgroundColor, Color tintColor, int tintIntensity) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;

        // Reuse paint objects per render thread to avoid native alloc+dispose every frame.
        [ThreadStatic] private static SKPaint? s_invertPaint;
        [ThreadStatic] private static SKPaint? s_tintPaint;
        [ThreadStatic] private static SKPaint? s_mulPaint;
        [ThreadStatic] private static SKPaint? s_alphaPaint;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
                return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas == null) return;

            var rect = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);

            canvas.Save();
            canvas.ClipRect(rect);

            // Pass 1: invert via Difference with white.
            var invertPaint = s_invertPaint ??= new SKPaint();
            invertPaint.Color = SKColors.White;
            invertPaint.BlendMode = SKBlendMode.Difference;
            canvas.DrawRect(rect, invertPaint);

            // Pass 2 (optional): tint via Plus (additive) blend.
            bool hasTint = tintColor.R != 0 || tintColor.G != 0 || tintColor.B != 0;
            if (hasTint)
            {
                var tintPaint = s_tintPaint ??= new SKPaint();
                tintPaint.Color = new SKColor(tintColor.R, tintColor.G, tintColor.B, 255);
                tintPaint.BlendMode = SKBlendMode.Plus;
                canvas.DrawRect(rect, tintPaint);

                // Pass 2b: Multiply with a near-white color derived from the tint.
                int maxT = Math.Max(tintColor.R, Math.Max(tintColor.G, tintColor.B));
                int intensity = Math.Clamp(tintIntensity, 0, 50);
                if (maxT > 0 && intensity > 0)
                {
                    byte mR = (byte)(255 - (maxT - tintColor.R) * intensity / maxT);
                    byte mG = (byte)(255 - (maxT - tintColor.G) * intensity / maxT);
                    byte mB = (byte)(255 - (maxT - tintColor.B) * intensity / maxT);

                    var mulPaint = s_mulPaint ??= new SKPaint();
                    mulPaint.Color = new SKColor(mR, mG, mB, 255);
                    mulPaint.BlendMode = SKBlendMode.Multiply;
                    canvas.DrawRect(rect, mulPaint);
                }
            }

            // Pass 3: preserve original alpha from the background color.
            var alphaPaint = s_alphaPaint ??= new SKPaint();
            alphaPaint.Color = new SKColor(255, 255, 255, backgroundColor.A);
            alphaPaint.BlendMode = SKBlendMode.DstIn;
            canvas.DrawRect(rect, alphaPaint);

            canvas.Restore();
        }
    }

    /// <summary>
    /// Custom draw operation that renders a diff-highlight image as a
    /// semi-transparent overlay between the PDF page and annotations.
    /// </summary>
    private class DiffOverlayDrawOp : ICustomDrawOperation
    {
        private readonly Rect _bounds;
        private readonly SKImage _image;
        private readonly Rect _displayArea;
        private readonly double _opacity;
        private readonly float _zoom;

        // Reuse a single SKPaint across frames on the render thread to
        // avoid the native alloc+dispose overhead every frame.
        [ThreadStatic]
        private static SKPaint? s_paint;

        public DiffOverlayDrawOp(Rect bounds, SKImage image, Rect displayArea, double opacity, float zoom = 1f)
        {
            _bounds = bounds;
            _image = image;
            _displayArea = displayArea;
            _opacity = Math.Clamp(opacity, 0, 1);
            _zoom = zoom;
        }

        public Rect Bounds => _bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature lease)
                return;
            using var api = lease.Lease();
            var canvas = api.SkCanvas;
            if (canvas == null) return;

            // Image pixels = PDF points × ZOOM. Scale DisplayArea (PDF coords)
            // by the zoom factor to get the correct source rect into the image.
            float z = _zoom;
            float destW = (float)_bounds.Width;
            float destH = (float)_bounds.Height;
            var srcRect = new SKRect(
                (float)_displayArea.X * z, (float)_displayArea.Y * z,
                (float)(_displayArea.X + _displayArea.Width) * z,
                (float)(_displayArea.Y + _displayArea.Height) * z);
            var destRect = new SKRect(0, 0, destW, destH);

            // Reuse the cached paint; create once per render thread.
            var paint = s_paint ??= new SKPaint { IsAntialias = false };
            paint.Color = SKColors.White.WithAlpha((byte)(_opacity * 255));

            canvas.Save();
            canvas.ClipRect(destRect);
            // SKImage.DrawImage uses GPU-cached textures when available,
            // avoiding the per-frame CPU→GPU pixel upload that DrawBitmap requires.
            // Nearest-neighbor sampling is fastest for diff highlight overlays.
            canvas.DrawImage(_image, srcRect, destRect, new SKSamplingOptions(SKFilterMode.Nearest), paint);
            canvas.Restore();
        }
    }

    private class TextOverlayDrawOp : ICustomDrawOperation
    {
        public record struct TextItem(float X, float Y, string Text, float FontSize, SKColor Color,
                                       bool HasBackground, bool HasBorder, bool IsTextAnnotation,
                                       string FontFamily = "",
                                       bool IsStickyNote = false, bool IsExpandedStickyNote = false,
                                       float PopupFontSize = 0f, bool IsPopupOnly = false,
                                       bool TintBackground = false,
                                       string SecondLine = "",
                                       bool CenterOnPoint = false);

        private readonly Rect _bounds;
        private readonly List<TextItem> _items;

        public TextOverlayDrawOp(Rect bounds, List<TextItem> items)
        {
            _bounds = bounds;
            _items = items;
        }

        public Rect Bounds => _bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        // Reuse paint/font objects per render thread to avoid native alloc+dispose overhead (#2)
        [ThreadStatic] private static SKFont? s_defaultFont;
        [ThreadStatic] private static SKPaint? s_textPaint;
        [ThreadStatic] private static SKPaint? s_bgPaint;
        [ThreadStatic] private static SKPaint? s_borderPaint;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
                return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas == null) return;

            var defaultFont = s_defaultFont ??= new SKFont(SKTypeface.Default);
            var paint = s_textPaint ??= new SKPaint { IsAntialias = true };
            var bgPaint = s_bgPaint ??= new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            var borderPaint = s_borderPaint ??= new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.2f };

            // First pass: draw sticky-note icons (and expanded popup if hovered)
            DrawStickyNoteIcons(canvas, bgPaint, borderPaint);

            // Second pass: draw grouped text-annotation frames
            DrawTextAnnotationFrames(canvas, defaultFont, bgPaint, borderPaint);

            // Third pass: draw measurement-label backgrounds + all text
            SKFont? customFont = null;
            string lastFamily = "";
            try
            {
                foreach (var item in _items)
                {
                    // Sticky notes are fully handled by DrawStickyNoteIcons; skip here
                    if (item.IsStickyNote) continue;

                    var font = defaultFont;
                    if (!string.IsNullOrEmpty(item.FontFamily))
                    {
                        if (item.FontFamily != lastFamily)
                        {
                            customFont?.Dispose();
                            var typeface = GetCachedTypeface(item.FontFamily);
                            customFont = new SKFont(typeface);
                            lastFamily = item.FontFamily;
                        }
                        if (customFont != null) font = customFont;
                    }

                    font.Size = item.FontSize;
                    paint.Color = item.Color;

                    if (item.HasBackground && !item.IsTextAnnotation)
                    {
                        bool twoLine = !string.IsNullOrEmpty(item.SecondLine);
                        if (twoLine)
                        {
                            // Measure both lines and draw one unified centered pill
                            font.MeasureText(item.Text,       out var b1);
                            font.MeasureText(item.SecondLine, out var b2);
                            float lineH    = item.FontSize * 1.3f;
                            float boxW     = Math.Max(b1.Width, b2.Width) + 16;
                            float boxH     = lineH * 2 + 6;
                            float boxX     = item.X - boxW / 2f;
                            float line1Y   = item.Y - lineH * 0.5f;
                            float line2Y   = line1Y + lineH;
                            float bgTop    = line1Y + b1.Top - 4;

                            if (item.TintBackground)
                                bgPaint.Color = new SKColor(
                                    (byte)(200 + item.Color.Red   / 5),
                                    (byte)(200 + item.Color.Green / 5),
                                    (byte)(200 + item.Color.Blue  / 5),
                                    220);
                            else
                                bgPaint.Color = new SKColor(255, 255, 255, 200);

                            canvas.DrawRoundRect(boxX, bgTop, boxW, boxH, 4, 4, bgPaint);
                            borderPaint.Color = new SKColor(item.Color.Red, item.Color.Green, item.Color.Blue, 80);
                            borderPaint.StrokeWidth = 1f;
                            canvas.DrawRoundRect(boxX, bgTop, boxW, boxH, 4, 4, borderPaint);

                            // Draw a faint divider between the two lines
                            borderPaint.Color = new SKColor(item.Color.Red, item.Color.Green, item.Color.Blue, 40);
                            float divY = bgTop + lineH + 2;
                            canvas.DrawLine(boxX + 6, divY, boxX + boxW - 6, divY, borderPaint);

                            // Draw text centered in the pill
                            canvas.DrawText(item.Text,       item.X - b1.Width / 2f - b1.Left, line1Y, font, paint);
                            canvas.DrawText(item.SecondLine, item.X - b2.Width / 2f - b2.Left, line2Y, font, paint);
                            continue;
                        }

                        font.MeasureText(item.Text, out var textBounds);
                        // CenterOnPoint: item.X/Y is the target center — offset so the pill
                        // is both horizontally and vertically centered on that point.
                        float drawX = item.CenterOnPoint
                            ? item.X - textBounds.Width / 2f - textBounds.Left
                            : item.X;
                        float drawY = item.CenterOnPoint
                            ? item.Y - (textBounds.Top + textBounds.Height / 2f)
                            : item.Y;
                        // Tinted backgrounds (measurement/area labels) get a faint wash of the annotation color
                        if (item.TintBackground)
                            bgPaint.Color = new SKColor(
                                (byte)(200 + item.Color.Red   / 5),
                                (byte)(200 + item.Color.Green / 5),
                                (byte)(200 + item.Color.Blue  / 5),
                                210);
                        else
                            bgPaint.Color = new SKColor(255, 255, 255, 200);
                        canvas.DrawRoundRect(
                            drawX + textBounds.Left - 4,
                            drawY + textBounds.Top - 3,
                            textBounds.Width + 8,
                            textBounds.Height + 6,
                            3, 3, bgPaint);
                        // Matching faint border
                        borderPaint.Color = new SKColor(item.Color.Red, item.Color.Green, item.Color.Blue, 60);
                        borderPaint.StrokeWidth = 1f;
                        canvas.DrawRoundRect(
                            drawX + textBounds.Left - 4,
                            drawY + textBounds.Top - 3,
                            textBounds.Width + 8,
                            textBounds.Height + 6,
                            3, 3, borderPaint);

                        canvas.DrawText(item.Text, drawX, drawY, font, paint);
                        continue;
                    }

                    canvas.DrawText(item.Text, item.X, item.Y, font, paint);
                }
            }
            finally { customFont?.Dispose(); }
        }

        /// <summary>
        /// Renders all sticky-note icon items. Items with IsExpandedStickyNote = true
        /// also get a popup bubble showing the comment text.
        /// </summary>
        private void DrawStickyNoteIcons(SKCanvas canvas, SKPaint bgPaint, SKPaint borderPaint)
        {
            foreach (var item in _items)
            {
                if (!item.IsStickyNote) continue;
                if (!item.IsPopupOnly)
                    DrawStickyNoteIcon(canvas, item.X, item.Y, item.FontSize, item.Color, bgPaint, borderPaint);
                if (item.IsExpandedStickyNote && !string.IsNullOrEmpty(item.Text))
                    DrawStickyNotePopup(canvas, item.X, item.Y, item.FontSize, item.Text, item.Color, bgPaint, borderPaint, item.PopupFontSize);
            }
        }

        // Reuse SKPath objects per render thread to avoid native allocation per icon (#5)
        [ThreadStatic] private static SKPath? s_bodyPath;
        [ThreadStatic] private static SKPath? s_foldPath;
        [ThreadStatic] private static SKPaint? s_linesPaint;

        private static void DrawStickyNoteIcon(SKCanvas canvas, float x, float y, float sz,
                                               SKColor color, SKPaint bgPaint, SKPaint borderPaint)
        {
            float fold = sz * 0.28f;

            // Main body: folded top-right corner
            var bodyPath = s_bodyPath ??= new SKPath();
            bodyPath.Reset();
            bodyPath.MoveTo(x, y);
            bodyPath.LineTo(x + sz - fold, y);
            bodyPath.LineTo(x + sz, y + fold);
            bodyPath.LineTo(x + sz, y + sz);
            bodyPath.LineTo(x, y + sz);
            bodyPath.Close();

            // Saturated fill: strong notepad-yellow presence
            bgPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 100);
            canvas.DrawPath(bodyPath, bgPaint);

            // Border
            borderPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 240);
            borderPaint.StrokeWidth = 1f;
            canvas.DrawPath(bodyPath, borderPaint);

            // Fold triangle (slightly darker)
            var foldPath = s_foldPath ??= new SKPath();
            foldPath.Reset();
            foldPath.MoveTo(x + sz - fold, y);
            foldPath.LineTo(x + sz, y + fold);
            foldPath.LineTo(x + sz - fold, y + fold);
            foldPath.Close();
            bgPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 160);
            canvas.DrawPath(foldPath, bgPaint);
            borderPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 220);
            canvas.DrawPath(foldPath, borderPaint);

            // Three lines suggesting text content
            var linesPaint = s_linesPaint ??= new SKPaint { IsAntialias = true };
            linesPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 180);
            linesPaint.StrokeWidth = MathF.Max(1f, sz * 0.07f);
            linesPaint.StrokeCap = SKStrokeCap.Round;
            float lx = x + sz * 0.14f;
            float lw = sz * 0.52f;
            float ly = y + sz * 0.38f;
            float ls = sz * 0.18f;
            canvas.DrawLine(lx, ly,           lx + lw,        ly,           linesPaint);
            canvas.DrawLine(lx, ly + ls,      lx + lw,        ly + ls,      linesPaint);
            canvas.DrawLine(lx, ly + ls * 2f, lx + lw * 0.65f, ly + ls * 2f, linesPaint);
        }

        private static void DrawStickyNotePopup(SKCanvas canvas, float iconX, float iconY,
                                                float iconSize, string text, SKColor color,
                                                SKPaint bgPaint, SKPaint borderPaint,
                                                float annotFontSize)
        {
            // Use the annotation's own font size; fall back to a sensible default
            float popupFontSize = annotFontSize > 0 ? annotFontSize : MathF.Max(11f, iconSize * 0.75f);
            float lineHeight = popupFontSize * 1.35f;

            using var popupFont = new SKFont(SKTypeface.Default, popupFontSize);

            // Word-wrap to a reasonable popup width (200px or ~20 chars)
            float maxPopupWidth = MathF.Max(200f, popupFontSize * 18f);
            var wrappedLines = WrapTextLines(text, maxPopupWidth, popupFont);

            float maxW = 0;
            var lineBounds = new List<(string line, SKRect bounds)>();
            foreach (var line in wrappedLines)
            {
                if (line.Length == 0) { lineBounds.Add((line, default)); continue; }
                popupFont.MeasureText(line, out var tb);
                lineBounds.Add((line, tb));
                maxW = Math.Max(maxW, tb.Width);
            }
            maxW = Math.Max(maxW, popupFontSize * 3);
            float contentH = wrappedLines.Count * lineHeight;

            float pad = 7f;
            float popupW = maxW + pad * 2;
            float popupH = contentH + pad * 2;

            // Position: default above-right; clamp so it stays on screen
            float px = iconX + iconSize + 6;
            float py = iconY - popupH;
            if (py < 4) py = iconY + iconSize + 4;

            // Drop shadow
            bgPaint.Color = new SKColor(0, 0, 0, 25);
            canvas.DrawRoundRect(px + 1, py + 2, popupW, popupH, 4, 4, bgPaint);

            // White body
            bgPaint.Color = new SKColor(255, 255, 255, 245);
            canvas.DrawRoundRect(px, py, popupW, popupH, 4, 4, bgPaint);

            // Subtle border
            borderPaint.Color = new SKColor(0, 0, 0, 30);
            borderPaint.StrokeWidth = 1f;
            canvas.DrawRoundRect(px, py, popupW, popupH, 4, 4, borderPaint);

            // Colored top accent bar
            bgPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 210);
            canvas.DrawRoundRect(px, py, popupW, 4, 4, 4, bgPaint);

            // Text content
            using var textPaint = new SKPaint
            {
                Color = new SKColor(30, 30, 30, 230),
                IsAntialias = true
            };
            float ty = py + pad + popupFontSize;
            foreach (var (line, _) in lineBounds)
            {
                if (line.Length > 0)
                    canvas.DrawText(line, px + pad, ty, popupFont, textPaint);
                ty += lineHeight;
            }
        }

        /// <summary>
        /// Groups text-annotation lines (identified by HasBorder on the first line)
        /// and draws a single white background + colored border frame around each group.
        /// </summary>
        private void DrawTextAnnotationFrames(SKCanvas canvas, SKFont defaultFont,
                                               SKPaint bgPaint, SKPaint borderPaint)
        {
            int i = 0;
            while (i < _items.Count)
            {
                var item = _items[i];
                if (!item.IsTextAnnotation) { i++; continue; }

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;
                var groupColor = item.Color;
                int j = i;

                while (j < _items.Count && _items[j].IsTextAnnotation)
                {
                    var line = _items[j];
                    defaultFont.Size = line.FontSize;
                    defaultFont.MeasureText(line.Text, out var tb);

                    float left = line.X + tb.Left;
                    float top = line.Y + tb.Top;
                    float right = line.X + tb.Left + tb.Width;
                    float bottom = line.Y + tb.Top + tb.Height;

                    minX = Math.Min(minX, left);
                    minY = Math.Min(minY, top);
                    maxX = Math.Max(maxX, right);
                    maxY = Math.Max(maxY, bottom);
                    j++;

                    if (j < _items.Count && _items[j].HasBorder) break;
                }

                // Stamp-style frame: accent bar + shadow + white body
                float pad = 5;
                float accentW = 4;
                var textArea = new SKRect(minX - pad, minY - pad, maxX + pad, maxY + pad);
                var fullArea = new SKRect(textArea.Left - accentW, textArea.Top,
                                          textArea.Right, textArea.Bottom);
                var frameRRect = new SKRoundRect(fullArea, 4, 4);

                // Subtle drop shadow
                bgPaint.Color = new SKColor(0, 0, 0, 25);
                canvas.DrawRoundRect(new SKRoundRect(
                    new SKRect(fullArea.Left + 1, fullArea.Top + 1,
                               fullArea.Right + 1, fullArea.Bottom + 2), 4, 4), bgPaint);

                // White background (nearly opaque)
                bgPaint.Color = new SKColor(255, 255, 255, 245);
                canvas.DrawRoundRect(frameRRect, bgPaint);

                // Subtle gray border
                borderPaint.Color = new SKColor(0, 0, 0, 30);
                canvas.DrawRoundRect(frameRRect, borderPaint);

                // Colored left accent bar (rounded only on left corners)
                var accentRRect = new SKRoundRect();
                accentRRect.SetRectRadii(
                    new SKRect(fullArea.Left, fullArea.Top,
                               fullArea.Left + accentW, fullArea.Bottom),
                    [new SKPoint(4, 4), new SKPoint(0, 0),
                     new SKPoint(0, 0), new SKPoint(4, 4)]);
                bgPaint.Color = new SKColor(groupColor.Red, groupColor.Green, groupColor.Blue, 210);
                canvas.DrawRoundRect(accentRRect, bgPaint);

                i = j;
            }
        }
    }

    #endregion
}

public class InkStroke
{
    public List<Point> Points { get; set; } = [];
    public Color Color { get; set; } = Colors.Red;
    public int ZIndex { get; set; }
    public double Width { get; set; } = 3;
    public double Opacity { get; set; } = 1.0;
    public bool IsHighlighter { get; set; }
    /// <summary>When true, points are connected with straight line segments instead of spline curves.</summary>
    public bool IsPolyline { get; set; }
    /// <summary>When true, the polyline forms a closed shape (last point connects back to first).</summary>
    public bool IsClosed { get; set; }
    /// <summary>When true, the closed polyline is rendered with a translucent fill.</summary>
    public bool IsFilled { get; set; }
    /// <summary>When true, this closed polyline is an area-measurement annotation and shows an area label.</summary>
    public bool IsAreaMeasure { get; set; }
    /// <summary>Measurement scale (mm/pt) captured at the time the area was drawn, matching the distance-measure scale.</summary>
    public double AreaScale { get; set; } = 25.4 / 72.0;
    /// <summary>Dash pattern applied to the stroke.</summary>
    public LineDashPattern DashPattern { get; set; } = LineDashPattern.Solid;
    /// <summary>
    /// Corner radius in PDF points for polyline vertices.
    /// 0 = sharp corners. Typical presets: 0, 2, 5, 10.
    /// </summary>
    public double CornerRadius { get; set; }

    // Cached pen to avoid per-frame allocation during rendering.
    private IPen? _cachedPen;
    private double _cachedPenScale;
    private IBrush? _cachedFillBrush;

    // Cached geometry to avoid rebuilding StreamGeometry every frame.
    // Invalidated when points change (InvalidateGeometry) or the view transform shifts.
    internal StreamGeometry? CachedGeometry;
    internal bool CachedGeometryClosed;
    internal double CachedGeoScaleX, CachedGeoScaleY, CachedGeoOffX, CachedGeoOffY;
    internal int CachedGeometryPointCount;

    internal IPen GetOrCreatePen(double penScale)
    {
        // Recreate only when the scale changes (zoom/resize) or first call
        if (_cachedPen == null || Math.Abs(_cachedPenScale - penScale) > 0.05)
        {
            _cachedPenScale = penScale;
            var c = Opacity < 1.0
                ? Color.FromArgb((byte)(Opacity * 255), Color.R, Color.G, Color.B)
                : Color;
            var brush = new SolidColorBrush(c).ToImmutable();
            var cap = IsHighlighter ? PenLineCap.Square : PenLineCap.Round;
            _cachedPen = new Pen(brush, Width * penScale,
                dashStyle: ShapeAnnotation.GetDashStyle(DashPattern),
                lineCap: cap, lineJoin: PenLineJoin.Round);
        }
        return _cachedPen;
    }

    internal IBrush GetOrCreateFillBrush()
    {
        byte alpha = IsAreaMeasure ? (byte)55 : (byte)30;
        _cachedFillBrush ??= new SolidColorBrush(
            Color.FromArgb(alpha, Color.R, Color.G, Color.B)).ToImmutable();
        return _cachedFillBrush;
    }

    public void InvalidatePen() { _cachedPen = null; _cachedFillBrush = null; CachedGeometry = null; }
    public void InvalidateGeometry() { CachedGeometry = null; }
}
