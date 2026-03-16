using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Finn.Model;
using MuPDFCore.MuPDFRenderer;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

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

    // Eraser hover highlight: the item the eraser is hovering over
    private object? _eraserHoverItem;
    // Sticky-note hover: which note's popup to show
    private TextAnnotation? _stickyNoteHoverItem;
    private int _totalStrokeCount;
    private int _totalShapeCount;
    private int _totalTextCount;
    private int _totalMeasurementCount;

    private enum UndoType { Stroke, Shape, Text, Measurement, ClearPage }
    private readonly Stack<(UndoType type, int page, object? data)> _undoStack = new();
    private readonly Stack<(UndoType type, int page, object item)> _redoStack = new();

    /// <summary>Raised whenever annotations are added, removed, or modified.</summary>
    public event Action? AnnotationChanged;
    public void NotifyAnnotationChanged() => AnnotationChanged?.Invoke();

    private record ClearPageSnapshot(
        List<InkStroke>? Strokes, List<ShapeAnnotation>? Shapes,
        List<TextAnnotation>? Texts, List<MeasurementAnnotation>? Measurements);

    // Cursor preview position for pen-size visualization
    private Point? _cursorPdfPos;

    /// <summary>Tool-type for the text placement ghost (Text, StickyNote, or ArrowText).</summary>
    private InlineAnnotationTool? _textPlacementPreviewTool;
    /// <summary>PDF-space position for the text placement ghost.</summary>
    private Point? _textPlacementPreviewPos;

    // Snap-to-alignment guides (PDF-space X/Y coordinates to draw as dotted lines)
    private double? _snapGuideX;
    private double? _snapGuideY;

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
        _layers = layers ?? [];
        foreach (var layer in _layers) layer.RecalculateCounts();
        _activeLayer = _layers.Count > 0 ? _layers[0] : null;
        _undoStack.Clear();
        _redoStack.Clear();
        RecalculateStrokeCount();
        if (IsViewerInitialized)
            InvalidateVisual();
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

    private AnnotationLayer? _activeLayer;
    public AnnotationLayer? ActiveLayer
    {
        get => _activeLayer;
        set { _activeLayer = value; InvalidateVisual(); }
    }

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

    // Selection highlight: the item currently being dragged with the Select tool
    private object? _selectHighlightItem;

    /// <summary>
    /// Millimetres per PDF point used for measurement labels.
    /// Default = 25.4/72 (uncalibrated). Set via calibration workflow.
    /// </summary>
    public double MeasurementScale { get; set; } = 25.4 / 72.0;

    public bool HasAnyStrokes => _totalStrokeCount > 0 || _totalShapeCount > 0
                                  || _totalTextCount > 0 || _totalMeasurementCount > 0
                                  || _activeStroke != null || _activeShape != null
                                  || _activeMeasurement != null || _arrowTextPreviewOrigin != null
                                  || _eraserHoverItem != null || _cursorPdfPos != null
                                  || _selectHighlightItem != null || _stickyNoteHoverItem != null
                                  || _textPlacementPreviewPos != null
                                  || _snapGuideX != null || _snapGuideY != null;

    public bool CanRedo => _redoStack.Count > 0;

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
        InvalidateVisual();
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
    }

    /// <summary>Ensure at least one layer exists; create the default if empty.</summary>
    public void EnsureDefaultLayer()
    {
        if (Layers.Count == 0)
            AddLayer("Layer 1", Color.FromRgb(214, 64, 69));
    }

    #endregion

    #region Coordinate Transform

    /// <summary>
    /// When Shift is held, constrain the endpoint so the line from
    /// <paramref name="origin"/> snaps to the nearest 0°/45°/90° axis.
    /// For rectangles/ellipses this produces perfect squares/circles.
    /// </summary>
    internal static Point ConstrainToAxis(Point origin, Point end)
    {
        double dx = end.X - origin.X;
        double dy = end.Y - origin.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return end;

        double angle = Math.Atan2(dy, dx);
        // Snap to nearest 45° increment
        double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
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
        double snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
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

    #endregion

    #region Drawing API

    public void BeginStroke(Point pdfPoint)
    {
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
            _undoStack.Push((UndoType.Stroke, _currentPage, null));
            _redoStack.Clear();
            ActiveLayer.RefreshStatus();
            NotifyAnnotationChanged();
        }
        _activeStroke = null;
    }

    // ── Polyline (multi-click straight-line segments) ─────────────────

    public void BeginPolyline(Point pdfPoint)
    {
        EnsureDefaultLayer();
        _activePolyline = new InkStroke
        {
            Color = StrokeColor,
            Width = StrokeWidth,
            Opacity = StrokeOpacity,
            IsPolyline = true,
            DashPattern = StrokeDashPattern
        };
        _activePolyline.Points.Add(pdfPoint);
        _polylinePreviewEnd = pdfPoint;
        InvalidateVisual();
    }

    public void AddPolylinePoint(Point pdfPoint, bool constrainAxis = false)
    {
        if (_activePolyline == null) return;
        if (constrainAxis && _activePolyline.Points.Count > 0)
            pdfPoint = ConstrainToAxis(_activePolyline.Points[^1], pdfPoint);
        _activePolyline.Points.Add(pdfPoint);
        _polylinePreviewEnd = pdfPoint;
        InvalidateVisual();
    }

    public void UpdatePolylinePreview(Point pdfPoint, bool constrainAxis = false)
    {
        if (constrainAxis && _activePolyline != null && _activePolyline.Points.Count > 0)
            pdfPoint = ConstrainToAxis(_activePolyline.Points[^1], pdfPoint);
        _polylinePreviewEnd = pdfPoint;
        InvalidateVisual();
    }

    public void EndPolyline()
    {
        if (_activePolyline != null && _activePolyline.Points.Count >= 2 && ActiveLayer != null)
        {
            if (!ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            {
                strokes = [];
                ActiveLayer.PageStrokes[_currentPage] = strokes;
            }
            strokes.Add(_activePolyline);
            ActiveLayer.StrokeCount++;
            _totalStrokeCount++;
            _undoStack.Push((UndoType.Stroke, _currentPage, null));
            _redoStack.Clear();
            ActiveLayer.RefreshStatus();
            NotifyAnnotationChanged();
        }
        _activePolyline = null;
        _polylinePreviewEnd = null;
        InvalidateVisual();
    }

    public void CancelPolyline()
    {
        _activePolyline = null;
        _polylinePreviewEnd = null;
        InvalidateVisual();
    }

    public bool HasActivePolyline => _activePolyline != null;
    public bool HasActiveShape => _activeShape != null;
    public bool HasActiveMeasurement => _activeMeasurement != null;

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
        InvalidateVisual();
    }

    #endregion

    #region Shape API

    public void BeginShape(Point pdfPoint)
    {
        EnsureDefaultLayer();
        _activeShape = new ShapeAnnotation
        {
            ShapeType = ActiveTool,
            Start = pdfPoint,
            End = pdfPoint,
            Color = StrokeColor,
            StrokeWidth = StrokeWidth,
            Opacity = StrokeOpacity,
            IsFilled = IsFilledMode,
            DashPattern = StrokeDashPattern
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
                : ConstrainToAxis(_activeShape.Start, pdfPoint);
        }
        else if (_activeShape.ShapeType is InlineAnnotationTool.Line or InlineAnnotationTool.Arrow)
        {
            pdfPoint = MagneticSnap(_activeShape.Start, pdfPoint);
        }
        _activeShape.End = pdfPoint;
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
                _undoStack.Push((UndoType.Shape, _currentPage, null));
                _redoStack.Clear();
                ActiveLayer.RefreshStatus();
                NotifyAnnotationChanged();
            }
        }
        _activeShape = null;
        InvalidateVisual();
    }

    #endregion

    #region Text API

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

    public void PlaceText(Point pdfPoint, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;

        var annotation = new TextAnnotation
        {
            Position = pdfPoint,
            Text = text,
            FontSize = TextFontSize,
            Color = StrokeColor,
            Opacity = StrokeOpacity,
            FontFamily = TextFontFamily
        };

        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
        {
            texts = [];
            ActiveLayer.PageTexts[_currentPage] = texts;
        }
        texts.Add(annotation);
        ActiveLayer.TextCount++;
        _totalTextCount++;
        _undoStack.Push((UndoType.Text, _currentPage, null));
        _redoStack.Clear();
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    public void PlaceStickyNote(Point pdfPoint, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
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
        _undoStack.Push((UndoType.Text, _currentPage, null));
        _redoStack.Clear();
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    public void PlaceArrowText(Point arrowOrigin, Point textPosition, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
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
            ArrowOrigin = arrowOrigin
        };

        if (!ActiveLayer.PageTexts.TryGetValue(_currentPage, out var texts))
        {
            texts = [];
            ActiveLayer.PageTexts[_currentPage] = texts;
        }
        texts.Add(annotation);
        ActiveLayer.TextCount++;
        _totalTextCount++;
        _undoStack.Push((UndoType.Text, _currentPage, null));
        _redoStack.Clear();
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        ClearArrowTextPreview();
    }

    /// <summary>Place a pre-built shape annotation (used by paste).</summary>
    public void PlaceShape(ShapeAnnotation shape)
    {
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        if (!ActiveLayer.PageShapes.TryGetValue(_currentPage, out var shapes))
        { shapes = []; ActiveLayer.PageShapes[_currentPage] = shapes; }
        shapes.Add(shape);
        ActiveLayer.ShapeCount++;
        _totalShapeCount++;
        _undoStack.Push((UndoType.Shape, _currentPage, null));
        _redoStack.Clear();
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    /// <summary>Place a pre-built ink stroke (used by paste).</summary>
    public void PlaceStroke(InkStroke stroke)
    {
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        if (!ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes))
        { strokes = []; ActiveLayer.PageStrokes[_currentPage] = strokes; }
        strokes.Add(stroke);
        ActiveLayer.StrokeCount++;
        _totalStrokeCount++;
        _undoStack.Push((UndoType.Stroke, _currentPage, null));
        _redoStack.Clear();
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
    }

    /// <summary>Place a pre-built measurement (used by paste).</summary>
    public void PlaceMeasurement(MeasurementAnnotation measurement)
    {
        EnsureDefaultLayer();
        if (ActiveLayer == null) return;
        if (!ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var ms))
        { ms = []; ActiveLayer.PageMeasurements[_currentPage] = ms; }
        ms.Add(measurement);
        ActiveLayer.MeasurementCount++;
        _totalMeasurementCount++;
        _undoStack.Push((UndoType.Measurement, _currentPage, null));
        _redoStack.Clear();
        ActiveLayer.RefreshStatus();
        NotifyAnnotationChanged();
        InvalidateVisual();
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

    #endregion

    #region Measurement API

    public void BeginMeasurement(Point pdfPoint)
    {
        EnsureDefaultLayer();
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
            var target = constrainAxis
                ? ConstrainToAxis(_activeMeasurement.Points[0], pdfPoint)
                : MagneticSnap(_activeMeasurement.Points[0], pdfPoint);
            _activeMeasurement.Points[^1] = target;
        }
        InvalidateVisual();
    }

    public void EndMeasurement()
    {
        if (_activeMeasurement != null && ActiveLayer != null && _activeMeasurement.Points.Count >= 2)
        {
            if (!ActiveLayer.PageMeasurements.TryGetValue(_currentPage, out var measurements))
            {
                measurements = [];
                ActiveLayer.PageMeasurements[_currentPage] = measurements;
            }
            measurements.Add(_activeMeasurement);
            ActiveLayer.MeasurementCount++;
            _totalMeasurementCount++;
            _undoStack.Push((UndoType.Measurement, _currentPage, null));
            _redoStack.Clear();
            ActiveLayer.RefreshStatus();
            NotifyAnnotationChanged();
        }
        _activeMeasurement = null;
        _measurementPreviewPoint = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Calibrate the measurement scale using the most recent measurement.
    /// The user specifies the real-world distance in mm for that measurement,
    /// and all future (and existing) measurements are rescaled accordingly.
    /// </summary>
    public void CalibrateFromLastMeasurement(double realDistanceMm)
    {
        // Find the last committed measurement on the current page across all layers
        MeasurementAnnotation? last = null;
        foreach (var layer in Layers)
        {
            if (layer.PageMeasurements.TryGetValue(_currentPage, out var ms) && ms.Count > 0)
                last = ms[^1];
        }
        if (last == null || last.Points.Count < 2 || realDistanceMm <= 0) return;

        // Compute raw PDF-point distance
        double dx = last.Points[1].X - last.Points[0].X;
        double dy = last.Points[1].Y - last.Points[0].Y;
        double pdfDist = Math.Sqrt(dx * dx + dy * dy);
        if (pdfDist < 0.01) return;

        double newScale = realDistanceMm / pdfDist;
        MeasurementScale = newScale;

        // Apply the new scale to all existing measurements
        foreach (var layer in Layers)
        {
            foreach (var list in layer.PageMeasurements.Values)
            {
                foreach (var m in list)
                    m.Scale = newScale;
            }
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
        if (ActiveLayer == null) return false;

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
                    texts.RemoveAt(i);
                    ActiveLayer.TextCount = Math.Max(0, ActiveLayer.TextCount - 1);
                    _totalTextCount = Math.Max(0, _totalTextCount - 1);
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
                    measurements.RemoveAt(i);
                    ActiveLayer.MeasurementCount = Math.Max(0, ActiveLayer.MeasurementCount - 1);
                    _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - 1);
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
                    shapes.RemoveAt(i);
                    ActiveLayer.ShapeCount = Math.Max(0, ActiveLayer.ShapeCount - 1);
                    _totalShapeCount = Math.Max(0, _totalShapeCount - 1);
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
                    strokes.RemoveAt(i);
                    ActiveLayer.StrokeCount = Math.Max(0, ActiveLayer.StrokeCount - 1);
                    _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1);
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

    /// <summary>Set the item to draw selection handles around (Select tool).</summary>
    public void SetSelectHighlight(object? item)
    {
        if (_selectHighlightItem != item)
        {
            _selectHighlightItem = item;
            InvalidateVisual();
        }
    }

    public void ClearSelectHighlight()
    {
        if (_selectHighlightItem != null)
        {
            _selectHighlightItem = null;
            InvalidateVisual();
        }
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
        if (ActiveLayer == null) return (rawDx, rawDy);

        // Get the center of the dragged item after applying the raw delta
        var center = GetAnnotationCenter(dragging);
        double cx = center.X + rawDx;
        double cy = center.Y + rawDy;

        double bestDx = rawDx, bestDy = rawDy;
        double bestSnapDistX = threshold, bestSnapDistY = threshold;

        // Collect centers of all other annotations on this page
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            CollectSnapTargets(layer, dragging, cx, cy, ref bestDx, ref bestDy,
                ref bestSnapDistX, ref bestSnapDistY, rawDx, rawDy, center);
        }

        return (bestDx, bestDy);
    }

    public void ClearSnapGuides()
    {
        if (_snapGuideX != null || _snapGuideY != null)
        {
            _snapGuideX = null;
            _snapGuideY = null;
            InvalidateVisual();
        }
    }

    private void CollectSnapTargets(AnnotationLayer layer, object dragging,
        double cx, double cy, ref double bestDx, ref double bestDy,
        ref double bestSnapDistX, ref double bestSnapDistY,
        double rawDx, double rawDy, Point dragCenter)
    {
        if (layer.PageTexts.TryGetValue(_currentPage, out var texts))
            foreach (var t in texts) { if (!ReferenceEquals(t, dragging)) CheckSnap(GetAnnotationCenter(t), cx, cy, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
        if (layer.PageShapes.TryGetValue(_currentPage, out var shapes))
            foreach (var s in shapes) { if (!ReferenceEquals(s, dragging)) CheckSnap(GetAnnotationCenter(s), cx, cy, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
        if (layer.PageMeasurements.TryGetValue(_currentPage, out var ms))
            foreach (var m in ms) { if (!ReferenceEquals(m, dragging)) CheckSnap(GetAnnotationCenter(m), cx, cy, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
        if (layer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            foreach (var ink in strokes) { if (!ReferenceEquals(ink, dragging)) CheckSnap(GetAnnotationCenter(ink), cx, cy, rawDx, rawDy, ref bestDx, ref bestDy, ref bestSnapDistX, ref bestSnapDistY); }
    }

    private void CheckSnap(Point other, double cx, double cy, double rawDx, double rawDy,
        ref double bestDx, ref double bestDy, ref double bestSnapDistX, ref double bestSnapDistY)
    {
        double distX = Math.Abs(cx - other.X);
        double distY = Math.Abs(cy - other.Y);
        if (distX < bestSnapDistX)
        {
            bestSnapDistX = distX;
            bestDx = rawDx + (other.X - cx);
            _snapGuideX = other.X;
        }
        if (distY < bestSnapDistY)
        {
            bestSnapDistY = distY;
            bestDy = rawDy + (other.Y - cy);
            _snapGuideY = other.Y;
        }
    }

    private static Point GetAnnotationCenter(object item) => item switch
    {
        TextAnnotation t => t.Position,
        ShapeAnnotation s => new Point((s.Start.X + s.End.X) / 2, (s.Start.Y + s.End.Y) / 2),
        MeasurementAnnotation m when m.Points.Count >= 2 =>
            new Point((m.Points[0].X + m.Points[1].X) / 2, (m.Points[0].Y + m.Points[1].Y) / 2),
        InkStroke ink when ink.Points.Count > 0 =>
            new Point(ink.Points.Average(p => p.X), ink.Points.Average(p => p.Y)),
        _ => default
    };

    /// <summary>
    /// Shows or hides the sticky-note hover popup. Call on pointer-move when a
    /// sticky note icon is under the cursor.
    /// </summary>
    public void UpdateStickyNoteHover(Point pdfPoint)
    {
        TextAnnotation? hit = null;
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (!layer.PageTexts.TryGetValue(_currentPage, out var texts)) continue;
            for (int i = texts.Count - 1; i >= 0; i--)
            {
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
        if (ActiveLayer == null) return false;
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
            ActiveLayer.RefreshStatus();
            ClearSelectHighlight();
            InvalidateVisual();
            NotifyAnnotationChanged();
        }
        return removed;
    }

    private static bool HitTestStroke(InkStroke stroke, Point pt, double threshold)
    {
        // Polylines have sparse points with straight segments — test segment proximity
        if (stroke.IsPolyline && stroke.Points.Count >= 2)
        {
            for (int i = 0; i < stroke.Points.Count - 1; i++)
                if (DistanceToSegment(pt, stroke.Points[i], stroke.Points[i + 1]) <= threshold)
                    return true;
            return false;
        }
        double threshSq = threshold * threshold;
        foreach (var p in stroke.Points)
        {
            double dx = pt.X - p.X;
            double dy = pt.Y - p.Y;
            if (dx * dx + dy * dy <= threshSq)
                return true;
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
                // Hit if near any of the 4 edges
                return DistanceToSegment(pt, r.TopLeft, r.TopRight) <= threshold
                    || DistanceToSegment(pt, r.TopRight, r.BottomRight) <= threshold
                    || DistanceToSegment(pt, r.BottomRight, r.BottomLeft) <= threshold
                    || DistanceToSegment(pt, r.BottomLeft, r.TopLeft) <= threshold;
            }
            case InlineAnnotationTool.Ellipse:
            {
                var r = NormalizedRect(shape.Start, shape.End);
                double cx = r.X + r.Width / 2;
                double cy = r.Y + r.Height / 2;
                double rx = r.Width / 2;
                double ry = r.Height / 2;
                if (rx < 1 || ry < 1) return false;
                // Normalized distance from center on the ellipse boundary
                double ndx = (pt.X - cx) / rx;
                double ndy = (pt.Y - cy) / ry;
                double dist = Math.Sqrt(ndx * ndx + ndy * ndy);
                // Near boundary means dist ≈ 1.0
                double normThreshold = threshold / Math.Min(rx, ry);
                return Math.Abs(dist - 1.0) <= normThreshold;
            }
            case InlineAnnotationTool.RevisionCloud:
            {
                // Same hit-test as rectangle (cloud follows the bounding box edges)
                var r = NormalizedRect(shape.Start, shape.End);
                return DistanceToSegment(pt, r.TopLeft, r.TopRight) <= threshold
                    || DistanceToSegment(pt, r.TopRight, r.BottomRight) <= threshold
                    || DistanceToSegment(pt, r.BottomRight, r.BottomLeft) <= threshold
                    || DistanceToSegment(pt, r.BottomLeft, r.TopLeft) <= threshold;
            }
        }
        return false;
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

    private static Rect NormalizedRect(Point a, Point b)
    {
        double x = Math.Min(a.X, b.X);
        double y = Math.Min(a.Y, b.Y);
        double w = Math.Abs(b.X - a.X);
        double h = Math.Abs(b.Y - a.Y);
        return new Rect(x, y, w, h);
    }

    private static bool HitTestText(TextAnnotation text, Point pt)
    {
        double w = text.FontSize * text.Text.Length * 0.55;
        double h = text.FontSize * (1 + text.Text.Count(c => c == '\n')) * 1.3;
        var rect = new Rect(text.Position.X, text.Position.Y, w, h);
        return rect.Contains(pt);
    }

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
        if (ActiveLayer == null || _undoStack.Count == 0) return;

        var (type, page, data) = _undoStack.Pop();
        bool removed = false;
        object? item = null;

        switch (type)
        {
            case UndoType.Stroke:
                if (ActiveLayer.PageStrokes.TryGetValue(page, out var strokes) && strokes.Count > 0)
                {
                    item = strokes[^1];
                    strokes.RemoveAt(strokes.Count - 1);
                    ActiveLayer.StrokeCount = Math.Max(0, ActiveLayer.StrokeCount - 1);
                    _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1);
                    removed = true;
                }
                break;
            case UndoType.Shape:
                if (ActiveLayer.PageShapes.TryGetValue(page, out var shapes) && shapes.Count > 0)
                {
                    item = shapes[^1];
                    shapes.RemoveAt(shapes.Count - 1);
                    ActiveLayer.ShapeCount = Math.Max(0, ActiveLayer.ShapeCount - 1);
                    _totalShapeCount = Math.Max(0, _totalShapeCount - 1);
                    removed = true;
                }
                break;
            case UndoType.Text:
                if (ActiveLayer.PageTexts.TryGetValue(page, out var texts) && texts.Count > 0)
                {
                    item = texts[^1];
                    texts.RemoveAt(texts.Count - 1);
                    ActiveLayer.TextCount = Math.Max(0, ActiveLayer.TextCount - 1);
                    _totalTextCount = Math.Max(0, _totalTextCount - 1);
                    removed = true;
                }
                break;
            case UndoType.Measurement:
                if (ActiveLayer.PageMeasurements.TryGetValue(page, out var measurements) && measurements.Count > 0)
                {
                    item = measurements[^1];
                    measurements.RemoveAt(measurements.Count - 1);
                    ActiveLayer.MeasurementCount = Math.Max(0, ActiveLayer.MeasurementCount - 1);
                    _totalMeasurementCount = Math.Max(0, _totalMeasurementCount - 1);
                    removed = true;
                }
                break;
            case UndoType.ClearPage:
                if (data is ClearPageSnapshot snap)
                {
                    if (snap.Strokes is { Count: > 0 })
                    {
                        if (!ActiveLayer.PageStrokes.TryGetValue(page, out var rs)) { rs = []; ActiveLayer.PageStrokes[page] = rs; }
                        rs.AddRange(snap.Strokes);
                        ActiveLayer.StrokeCount += snap.Strokes.Count;
                        _totalStrokeCount += snap.Strokes.Count;
                    }
                    if (snap.Shapes is { Count: > 0 })
                    {
                        if (!ActiveLayer.PageShapes.TryGetValue(page, out var rsh)) { rsh = []; ActiveLayer.PageShapes[page] = rsh; }
                        rsh.AddRange(snap.Shapes);
                        ActiveLayer.ShapeCount += snap.Shapes.Count;
                        _totalShapeCount += snap.Shapes.Count;
                    }
                    if (snap.Texts is { Count: > 0 })
                    {
                        if (!ActiveLayer.PageTexts.TryGetValue(page, out var rt)) { rt = []; ActiveLayer.PageTexts[page] = rt; }
                        rt.AddRange(snap.Texts);
                        ActiveLayer.TextCount += snap.Texts.Count;
                        _totalTextCount += snap.Texts.Count;
                    }
                    if (snap.Measurements is { Count: > 0 })
                    {
                        if (!ActiveLayer.PageMeasurements.TryGetValue(page, out var rm)) { rm = []; ActiveLayer.PageMeasurements[page] = rm; }
                        rm.AddRange(snap.Measurements);
                        ActiveLayer.MeasurementCount += snap.Measurements.Count;
                        _totalMeasurementCount += snap.Measurements.Count;
                    }
                    item = data;
                    removed = true;
                }
                break;
        }

        if (removed)
        {
            _redoStack.Push((type, page, item!));
            ActiveLayer.RefreshStatus();
            InvalidateVisual();
            NotifyAnnotationChanged();
        }
    }

    public void Redo()
    {
        if (ActiveLayer == null || _redoStack.Count == 0) return;

        var (type, page, item) = _redoStack.Pop();
        bool restored = false;

        switch (type)
        {
            case UndoType.Stroke:
                if (item is InkStroke stroke)
                {
                    if (!ActiveLayer.PageStrokes.TryGetValue(page, out var strokes))
                    {
                        strokes = [];
                        ActiveLayer.PageStrokes[page] = strokes;
                    }
                    strokes.Add(stroke);
                    ActiveLayer.StrokeCount++;
                    _totalStrokeCount++;
                    restored = true;
                }
                break;
            case UndoType.Shape:
                if (item is ShapeAnnotation shape)
                {
                    if (!ActiveLayer.PageShapes.TryGetValue(page, out var shapes))
                    {
                        shapes = [];
                        ActiveLayer.PageShapes[page] = shapes;
                    }
                    shapes.Add(shape);
                    ActiveLayer.ShapeCount++;
                    _totalShapeCount++;
                    restored = true;
                }
                break;
            case UndoType.Text:
                if (item is TextAnnotation text)
                {
                    if (!ActiveLayer.PageTexts.TryGetValue(page, out var texts))
                    {
                        texts = [];
                        ActiveLayer.PageTexts[page] = texts;
                    }
                    texts.Add(text);
                    ActiveLayer.TextCount++;
                    _totalTextCount++;
                    restored = true;
                }
                break;
            case UndoType.Measurement:
                if (item is MeasurementAnnotation measurement)
                {
                    if (!ActiveLayer.PageMeasurements.TryGetValue(page, out var measurements))
                    {
                        measurements = [];
                        ActiveLayer.PageMeasurements[page] = measurements;
                    }
                    measurements.Add(measurement);
                    ActiveLayer.MeasurementCount++;
                    _totalMeasurementCount++;
                    restored = true;
                }
                break;
            case UndoType.ClearPage:
                if (item is ClearPageSnapshot snap)
                {
                    if (ActiveLayer.PageStrokes.TryGetValue(page, out var cs) && cs.Count > 0)
                    { ActiveLayer.StrokeCount -= cs.Count; _totalStrokeCount -= cs.Count; cs.Clear(); }
                    if (ActiveLayer.PageShapes.TryGetValue(page, out var csh) && csh.Count > 0)
                    { ActiveLayer.ShapeCount -= csh.Count; _totalShapeCount -= csh.Count; csh.Clear(); }
                    if (ActiveLayer.PageTexts.TryGetValue(page, out var ct) && ct.Count > 0)
                    { ActiveLayer.TextCount -= ct.Count; _totalTextCount -= ct.Count; ct.Clear(); }
                    if (ActiveLayer.PageMeasurements.TryGetValue(page, out var cm) && cm.Count > 0)
                    { ActiveLayer.MeasurementCount -= cm.Count; _totalMeasurementCount -= cm.Count; cm.Clear(); }
                    restored = true;
                }
                break;
        }

        if (restored)
        {
            _undoStack.Push((type, page, null));
            ActiveLayer.RefreshStatus();
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
            _undoStack.Push((UndoType.ClearPage, _currentPage, snapshot));
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

        if (!IsViewerInitialized || !HasAnyStrokes) return;

        var da = DisplayArea;
        var boundsSize = Bounds.Size;
        if (da.Width <= 0 || da.Height <= 0 ||
            boundsSize.Width <= 0 || boundsSize.Height <= 0) return;

        double scaleX = boundsSize.Width / da.Width;
        double scaleY = boundsSize.Height / da.Height;
        double penScale = (scaleX + scaleY) * 0.5;

        // Collect text items for the SkiaSharp overlay pass
        var textItems = new List<TextOverlayDrawOp.TextItem>();

        // Draw all visible layers
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            {
                foreach (var stroke in strokes)
                    RenderStroke(context, stroke, da, boundsSize, scaleX, scaleY);
            }
            if (layer.PageShapes.TryGetValue(_currentPage, out var shapes))
            {
                foreach (var shape in shapes)
                    RenderShape(context, shape, da, boundsSize, scaleX, scaleY);
            }
            if (layer.PageMeasurements.TryGetValue(_currentPage, out var measurements))
            {
                foreach (var m in measurements)
                {
                    RenderMeasurementGeometry(context, m.Points, m.Color, da, boundsSize, penScale);
                    CollectMeasurementLabel(m, da, boundsSize, penScale, textItems);
                }
            }
            if (layer.PageTexts.TryGetValue(_currentPage, out var texts))
            {
                foreach (var t in texts)
                {
                    if (t.IsStickyNote)
                        CollectStickyNoteIcon(t, da, boundsSize, penScale, textItems, isExpanded: false);
                    else
                    {
                        CollectTextAnnotation(t, da, boundsSize, penScale, textItems);
                        if (t.ArrowOrigin.HasValue)
                            RenderTextArrow(context, t, da, boundsSize, penScale);
                    }
                }
            }
        }

        // Draw the active stroke being drawn
        if (_activeStroke != null)
            RenderStroke(context, _activeStroke, da, boundsSize, scaleX, scaleY);

        // Draw the active polyline being constructed (with preview segment)
        if (_activePolyline != null)
        {
            RenderStroke(context, _activePolyline, da, boundsSize, scaleX, scaleY);
            // Preview segment from last committed point to cursor
            if (_polylinePreviewEnd.HasValue && _activePolyline.Points.Count > 0)
            {
                var lastPt = PdfToScreen(_activePolyline.Points[^1], da, boundsSize);
                var previewPt = PdfToScreen(_polylinePreviewEnd.Value, da, boundsSize);
                var previewPen = new Pen(
                    new SolidColorBrush(Color.FromArgb(140, StrokeColor.R, StrokeColor.G, StrokeColor.B)).ToImmutable(),
                    StrokeWidth * penScale, dashStyle: new DashStyle([4, 3], 0),
                    lineCap: PenLineCap.Round);
                context.DrawLine(previewPen, lastPt, previewPt);
            }
        }

        // Draw the active shape being dragged (dashed preview)
        if (_activeShape != null)
        {
            var dashPen = _activeShape.GetOrCreateDashedPen(penScale);
            RenderShape(context, _activeShape, da, boundsSize, scaleX, scaleY, dashPen);
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
            var previewPen = new Pen(new SolidColorBrush(StrokeColor).ToImmutable(),
                1.2, lineCap: PenLineCap.Round);
            context.DrawLine(previewPen, from, to);
            DrawArrowhead(context, previewPen, to, from, penScale);
        }

        // Hover popup for the sticky note under the cursor (drawn above all other items)
        if (_stickyNoteHoverItem != null)
            CollectStickyNoteIcon(_stickyNoteHoverItem, da, boundsSize, penScale, textItems, isExpanded: true);

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

        // Render all text via SkiaSharp overlay
        if (textItems.Count > 0)
            context.Custom(new TextOverlayDrawOp(new Rect(boundsSize), textItems));

        // Eraser hover highlight: draw a translucent red overlay on the hovered item
        if (_eraserHoverItem != null)
            RenderEraserHover(context, da, boundsSize, scaleX, scaleY, penScale);

        // Pen cursor preview: colored circle showing pen size at cursor position
        if (_cursorPdfPos.HasValue && ActiveTool is InlineAnnotationTool.Draw or InlineAnnotationTool.Highlight)
        {
            var cp = PdfToScreen(_cursorPdfPos.Value, da, boundsSize);
            double radius = StrokeWidth * 0.5 * penScale;
            var previewColor = Color.FromArgb(160, StrokeColor.R, StrokeColor.G, StrokeColor.B);
            var previewBrush = new SolidColorBrush(previewColor).ToImmutable();
            context.DrawEllipse(previewBrush, null, cp, radius, radius);
        }

        // Snap-to-alignment guides: thin dotted lines across the viewport
        if (_snapGuideX.HasValue || _snapGuideY.HasValue)
        {
            var guidePen = new Pen(new SolidColorBrush(Color.FromArgb(120, 59, 130, 217)).ToImmutable(),
                1.0, dashStyle: new DashStyle([3, 3], 0), lineCap: PenLineCap.Flat);
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
        }

        // Selection handles: dashed bounding box around the item being dragged
        if (_selectHighlightItem != null)
            RenderSelectionHighlight(context, da, boundsSize, scaleX, scaleY, penScale);
    }

    private void RenderSelectionHighlight(DrawingContext context, Rect da, Size boundsSize,
                                          double scaleX, double scaleY, double penScale)
    {
        // Subtle selection box: thin, low-alpha gray dashed outline
        var selectPen = new Pen(new SolidColorBrush(Color.FromArgb(80, 120, 120, 120)).ToImmutable(),
            1.0 * penScale, dashStyle: new DashStyle([5, 4], 0),
            lineCap: PenLineCap.Round);
        // Vertex handles: white fill + dark outline for clean contrast
        var vertexBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255)).ToImmutable();
        var vertexPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 60, 60, 60)).ToImmutable(),
            1.2 * penScale, lineCap: PenLineCap.Round);

        Rect? bounds = null;
        switch (_selectHighlightItem)
        {
            case InkStroke stroke:
            {
                if (stroke.Points.Count == 0) break;
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in stroke.Points)
                {
                    var sp = PdfToScreen(p, da, boundsSize);
                    minX = Math.Min(minX, sp.X); minY = Math.Min(minY, sp.Y);
                    maxX = Math.Max(maxX, sp.X); maxY = Math.Max(maxY, sp.Y);
                }
                bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                break;
            }
            case ShapeAnnotation shape:
            {
                var s = PdfToScreen(shape.Start, da, boundsSize);
                var e = PdfToScreen(shape.End, da, boundsSize);
                bounds = new Rect(
                    Math.Min(s.X, e.X), Math.Min(s.Y, e.Y),
                    Math.Abs(e.X - s.X), Math.Abs(e.Y - s.Y));
                break;
            }
            case TextAnnotation t:
            {
                var sp = PdfToScreen(t.Position, da, boundsSize);
                if (t.IsStickyNote)
                {
                    double sz = 13.0 * penScale;
                    bounds = new Rect(sp.X, sp.Y, sz, sz);
                }
                else
                {
                    double w = t.FontSize * penScale * t.Text.Length * 0.55;
                    double h = t.FontSize * penScale * (1 + t.Text.Count(c => c == '\n')) * 1.3;
                    bounds = new Rect(sp.X, sp.Y, w, h);
                }
                break;
            }
            case MeasurementAnnotation m:
            {
                if (m.Points.Count >= 2)
                {
                    var s0 = PdfToScreen(m.Points[0], da, boundsSize);
                    var s1 = PdfToScreen(m.Points[1], da, boundsSize);
                    bounds = new Rect(
                        Math.Min(s0.X, s1.X), Math.Min(s0.Y, s1.Y),
                        Math.Abs(s1.X - s0.X), Math.Abs(s1.Y - s0.Y));
                }
                break;
            }
        }

        if (bounds is { } b)
        {
            var inflated = b.Inflate(4 * penScale);
            context.DrawRectangle(null, selectPen, inflated);
            // Corner dots — tiny, very subtle
            var cornerBrush = new SolidColorBrush(Color.FromArgb(60, 120, 120, 120)).ToImmutable();
            double cornerSize = 2.0 * penScale;
            context.DrawEllipse(cornerBrush, null, inflated.TopLeft, cornerSize, cornerSize);
            context.DrawEllipse(cornerBrush, null, inflated.TopRight, cornerSize, cornerSize);
            context.DrawEllipse(cornerBrush, null, inflated.BottomLeft, cornerSize, cornerSize);
            context.DrawEllipse(cornerBrush, null, inflated.BottomRight, cornerSize, cornerSize);

            // Arrow-origin handle: draggable circle at the arrow tip
            if (_selectHighlightItem is TextAnnotation { ArrowOrigin: { } ao })
            {
                var arrowScreen = PdfToScreen(ao, da, boundsSize);
                double vtxSize = 5 * penScale;
                context.DrawEllipse(vertexBrush, vertexPen, arrowScreen, vtxSize, vtxSize);
            }

            // Shape vertex handles: draggable circles at Start and End
            if (_selectHighlightItem is ShapeAnnotation selShape)
            {
                var ss = PdfToScreen(selShape.Start, da, boundsSize);
                var se = PdfToScreen(selShape.End, da, boundsSize);
                double vtxSize = 5 * penScale;
                context.DrawEllipse(vertexBrush, vertexPen, ss, vtxSize, vtxSize);
                context.DrawEllipse(vertexBrush, vertexPen, se, vtxSize, vtxSize);
            }

            // Measurement vertex handles: draggable circles at endpoints
            if (_selectHighlightItem is MeasurementAnnotation selMeas && selMeas.Points.Count >= 2)
            {
                var mp0 = PdfToScreen(selMeas.Points[0], da, boundsSize);
                var mp1 = PdfToScreen(selMeas.Points[1], da, boundsSize);
                double vtxSize = 5 * penScale;
                context.DrawEllipse(vertexBrush, vertexPen, mp0, vtxSize, vtxSize);
                context.DrawEllipse(vertexBrush, vertexPen, mp1, vtxSize, vtxSize);
            }

            // Polyline vertex handles: draggable circles at every vertex
            if (_selectHighlightItem is InkStroke { IsPolyline: true } selPoly)
            {
                double vtxSize = 5 * penScale;
                foreach (var p in selPoly.Points)
                {
                    var sp = PdfToScreen(p, da, boundsSize);
                    context.DrawEllipse(vertexBrush, vertexPen, sp, vtxSize, vtxSize);
                }
            }
        }
    }

    private void RenderEraserHover(DrawingContext context, Rect da, Size boundsSize,
                                    double scaleX, double scaleY, double penScale)
    {
        var hoverBrush = new SolidColorBrush(Color.FromArgb(60, 255, 50, 50)).ToImmutable();
        var hoverPen = new Pen(new SolidColorBrush(Color.FromArgb(140, 255, 50, 50)).ToImmutable(),
            2 * penScale, lineCap: PenLineCap.Round);

        switch (_eraserHoverItem)
        {
            case InkStroke stroke:
                RenderStroke(context, stroke, da, boundsSize, scaleX, scaleY, hoverPen);
                break;
            case ShapeAnnotation shape:
                RenderShape(context, shape, da, boundsSize, scaleX, scaleY, hoverPen);
                break;
            case TextAnnotation text:
            {
                var screenPos = PdfToScreen(text.Position, da, boundsSize);
                if (text.IsStickyNote)
                {
                    double sz = 13.0 * penScale;
                }
                else
                {
                    double w = text.FontSize * text.Text.Length * 0.55 * penScale;
                    double h = text.FontSize * (1 + text.Text.Count(c => c == '\n')) * 1.3 * penScale;
                    context.DrawRectangle(hoverBrush, hoverPen, new Rect(screenPos.X, screenPos.Y, w, h), 4, 4);
                }
                break;
            }
            case MeasurementAnnotation m:
                if (m.Points.Count >= 2)
                {
                    var s0 = PdfToScreen(m.Points[0], da, boundsSize);
                    var s1 = PdfToScreen(m.Points[1], da, boundsSize);
                    context.DrawLine(hoverPen, s0, s1);
                }
                break;
        }
    }

    private void RenderStroke(DrawingContext context, InkStroke stroke,
                              Rect da, Size boundsSize,
                              double scaleX, double scaleY, IPen? overridePen = null)
    {
        var pts = stroke.Points;
        if (pts.Count < 2) return;

        double penScale = (scaleX + scaleY) * 0.5;
        var pen = overridePen ?? stroke.GetOrCreatePen(penScale);

        // Pre-transform all points to screen space once
        int n = pts.Count;
        Span<Point> sp = n <= 256 ? stackalloc Point[n] : new Point[n];
        for (int i = 0; i < n; i++)
            sp[i] = PdfToScreen(pts[i], da, boundsSize);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(sp[0], false);

            if (n == 2 || stroke.IsPolyline)
            {
                // Straight line segments (2-point strokes or polylines)
                for (int i = 1; i < n; i++)
                    ctx.LineTo(sp[i]);
            }
            else
            {
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
            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private void RenderShape(DrawingContext context, ShapeAnnotation shape,
                             Rect da, Size boundsSize,
                             double scaleX, double scaleY, IPen? overridePen = null)
    {
        double penScale = (scaleX + scaleY) * 0.5;
        var pen = overridePen ?? shape.GetOrCreatePen(penScale);

        var screenStart = PdfToScreen(shape.Start, da, boundsSize);
        var screenEnd = PdfToScreen(shape.End, da, boundsSize);

        // Create optional fill brush for filled shapes
        IBrush? fillBrush = null;
        if (shape.IsFilled && overridePen == null)
        {
            var fillColor = Color.FromArgb(80, shape.Color.R, shape.Color.G, shape.Color.B);
            fillBrush = new SolidColorBrush(fillColor).ToImmutable();
        }

        switch (shape.ShapeType)
        {
            case InlineAnnotationTool.Line:
                context.DrawLine(pen, screenStart, screenEnd);
                break;

            case InlineAnnotationTool.Arrow:
                context.DrawLine(pen, screenStart, screenEnd);
                DrawArrowhead(context, pen, screenStart, screenEnd, penScale);
                break;

            case InlineAnnotationTool.Rectangle:
            {
                double x = Math.Min(screenStart.X, screenEnd.X);
                double y = Math.Min(screenStart.Y, screenEnd.Y);
                double w = Math.Abs(screenEnd.X - screenStart.X);
                double h = Math.Abs(screenEnd.Y - screenStart.Y);
                context.DrawRectangle(fillBrush, pen, new Rect(x, y, w, h));
                break;
            }
            case InlineAnnotationTool.Ellipse:
            {
                double cx = (screenStart.X + screenEnd.X) / 2;
                double cy = (screenStart.Y + screenEnd.Y) / 2;
                double rx = Math.Abs(screenEnd.X - screenStart.X) / 2;
                double ry = Math.Abs(screenEnd.Y - screenStart.Y) / 2;
                var geometry = new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));
                context.DrawGeometry(fillBrush, pen, geometry);
                break;
            }
            case InlineAnnotationTool.RevisionCloud:
            {
                var cloudGeometry = CreateCloudPath(screenStart, screenEnd, penScale);
                context.DrawGeometry(fillBrush, pen, cloudGeometry);
                break;
            }
        }
    }

    /// <summary>
    /// Creates a revision cloud geometry: a closed path of small arc segments
    /// running around the perimeter of the bounding rectangle.
    /// </summary>
    private static StreamGeometry CreateCloudPath(Point screenStart, Point screenEnd, double penScale)
    {
        double x1 = Math.Min(screenStart.X, screenEnd.X);
        double y1 = Math.Min(screenStart.Y, screenEnd.Y);
        double x2 = Math.Max(screenStart.X, screenEnd.X);
        double y2 = Math.Max(screenStart.Y, screenEnd.Y);

        double arcRadius = 8 * penScale;
        if (arcRadius < 4) arcRadius = 4;

        // Collect perimeter points (clockwise: top → right → bottom → left)
        var perimeterPoints = new List<Point>();
        void AddEdge(Point from, Point to)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double edgeLen = Math.Sqrt(dx * dx + dy * dy);
            int segments = Math.Max(1, (int)(edgeLen / (arcRadius * 1.6)));
            for (int i = 0; i < segments; i++)
            {
                double t = (double)i / segments;
                perimeterPoints.Add(new Point(from.X + dx * t, from.Y + dy * t));
            }
        }
        AddEdge(new Point(x1, y1), new Point(x2, y1)); // top
        AddEdge(new Point(x2, y1), new Point(x2, y2)); // right
        AddEdge(new Point(x2, y2), new Point(x1, y2)); // bottom
        AddEdge(new Point(x1, y2), new Point(x1, y1)); // left

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            if (perimeterPoints.Count < 2)
            {
                ctx.BeginFigure(new Point(x1, y1), true);
                ctx.LineTo(new Point(x2, y2));
                ctx.EndFigure(true);
            }
            else
            {
                ctx.BeginFigure(perimeterPoints[0], true);
                for (int i = 0; i < perimeterPoints.Count; i++)
                {
                    var next = perimeterPoints[(i + 1) % perimeterPoints.Count];
                    var mid = new Point(
                        (perimeterPoints[i].X + next.X) / 2,
                        (perimeterPoints[i].Y + next.Y) / 2);

                    // Compute outward bulge perpendicular to the edge
                    double edx = next.X - perimeterPoints[i].X;
                    double edy = next.Y - perimeterPoints[i].Y;
                    double elen = Math.Sqrt(edx * edx + edy * edy);
                    if (elen < 0.5) { ctx.LineTo(next); continue; }

                    // Outward normal (for clockwise winding, outward is to the right)
                    double nx = edy / elen;
                    double ny = -edx / elen;
                    double bulge = arcRadius * 0.6;

                    var cp = new Point(mid.X + nx * bulge, mid.Y + ny * bulge);
                    ctx.QuadraticBezierTo(cp, next);
                }
                ctx.EndFigure(true);
            }
        }
        return geometry;
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

        // Fill the arrowhead with the same color as the pen
        context.DrawGeometry(pen.Brush, pen, arrowGeometry);
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

        // Measure exact text box using SkiaSharp (same logic as DrawTextAnnotationFrames)
        float fontSize = (float)(t.FontSize * penScale);
        float lineHeight = fontSize * 1.3f;
        float x = (float)boxOrigin.X;
        float y = (float)boxOrigin.Y + fontSize;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        using var skFont = new SKFont(SKTypeface.Default, fontSize);
        foreach (var line in t.Text.Split('\n'))
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
        var pen = new Pen(new SolidColorBrush(c).ToImmutable(),
            1.2, lineCap: PenLineCap.Round);
        context.DrawLine(pen, arrowTip, connection);
        DrawArrowhead(context, pen, connection, arrowTip, penScale);
    }

    /// <summary>Returns the center point of the rectangle side closest to the given point.</summary>
    private static Point ClosestSideCenter(Rect rect, Point pt)
    {
        Point[] candidates =
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
                                           Color color, Rect da, Size boundsSize, double penScale)
    {
        if (pdfPoints.Count < 2) return;

        var c = Color.FromArgb(220, color.R, color.G, color.B);
        var brush = new SolidColorBrush(c).ToImmutable();
        var solidPen = new Pen(brush, 1.5 * penScale,
            lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

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
        var brush = new SolidColorBrush(color).ToImmutable();
        var geo = new StreamGeometry();
        using (var ctx2 = geo.Open())
        {
            ctx2.BeginFigure(p1, true);
            ctx2.LineTo(tip);
            ctx2.LineTo(p2);
            ctx2.EndFigure(true);
        }
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

        var lines = t.Text.Split('\n');
        float lineHeight = fontSize * 1.3f;
        float y = (float)screenPos.Y + fontSize;
        bool first = true;

        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                items.Add(new TextOverlayDrawOp.TextItem(
                    (float)screenPos.X, y, line, fontSize, color,
                    HasBackground: true, HasBorder: first, IsTextAnnotation: true,
                    FontFamily: fontFamily));
                first = false;
            }
            y += lineHeight;
        }
    }

    private void CollectMeasurementLabel(MeasurementAnnotation m, Rect da, Size boundsSize,
                                         double penScale, List<TextOverlayDrawOp.TextItem> items)
    {
        var labelPos = PdfToScreen(m.GetLabelPosition(), da, boundsSize);
        float fontSize = (float)(10 * penScale);
        var color = new SKColor(m.Color.R, m.Color.G, m.Color.B);
        items.Add(new TextOverlayDrawOp.TextItem(
            (float)labelPos.X, (float)labelPos.Y, m.GetLabel(), fontSize, color,
            HasBackground: true, HasBorder: false, IsTextAnnotation: false));
    }

    /// <summary>
    /// Custom draw operation that renders text annotations and measurement labels
    /// using SkiaSharp's native text rendering via ICustomDrawOperation.
    /// </summary>
    private class TextOverlayDrawOp : ICustomDrawOperation
    {
        public record struct TextItem(float X, float Y, string Text, float FontSize, SKColor Color,
                                       bool HasBackground, bool HasBorder, bool IsTextAnnotation,
                                       string FontFamily = "",
                                       bool IsStickyNote = false, bool IsExpandedStickyNote = false,
                                       float PopupFontSize = 0f);

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

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature leaseFeature)
                return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            if (canvas == null) return;

            using var defaultFont = new SKFont(SKTypeface.Default);
            using var paint = new SKPaint { IsAntialias = true };
            using var bgPaint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = true };
            using var borderPaint = new SKPaint { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeWidth = 1.2f };

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
                            var typeface = SKTypeface.FromFamilyName(item.FontFamily) ?? SKTypeface.Default;
                            customFont = new SKFont(typeface);
                            lastFamily = item.FontFamily;
                        }
                        if (customFont != null) font = customFont;
                    }

                    font.Size = item.FontSize;
                    paint.Color = item.Color;

                    if (item.HasBackground && !item.IsTextAnnotation)
                    {
                        font.MeasureText(item.Text, out var textBounds);
                        bgPaint.Color = new SKColor(255, 255, 255, 200);
                        canvas.DrawRoundRect(
                            item.X + textBounds.Left - 3,
                            item.Y + textBounds.Top - 2,
                            textBounds.Width + 6,
                            textBounds.Height + 4,
                            3, 3, bgPaint);
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
                DrawStickyNoteIcon(canvas, item.X, item.Y, item.FontSize, item.Color, bgPaint, borderPaint);
                if (item.IsExpandedStickyNote && !string.IsNullOrEmpty(item.Text))
                    DrawStickyNotePopup(canvas, item.X, item.Y, item.FontSize, item.Text, item.Color, bgPaint, borderPaint, item.PopupFontSize);
            }
        }

        private static void DrawStickyNoteIcon(SKCanvas canvas, float x, float y, float sz,
                                               SKColor color, SKPaint bgPaint, SKPaint borderPaint)
        {
            float fold = sz * 0.28f;

            // Main body: folded top-right corner
            var bodyPath = new SKPath();
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
            var foldPath = new SKPath();
            foldPath.MoveTo(x + sz - fold, y);
            foldPath.LineTo(x + sz, y + fold);
            foldPath.LineTo(x + sz - fold, y + fold);
            foldPath.Close();
            bgPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 160);
            canvas.DrawPath(foldPath, bgPaint);
            borderPaint.Color = new SKColor(color.Red, color.Green, color.Blue, 220);
            canvas.DrawPath(foldPath, borderPaint);

            // Three lines suggesting text content
            using var linesPaint = new SKPaint
            {
                Color = new SKColor(color.Red, color.Green, color.Blue, 180),
                StrokeWidth = MathF.Max(1f, sz * 0.07f),
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };
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
            string[] lines = text.Split('\n');

            using var popupFont = new SKFont(SKTypeface.Default, popupFontSize);
            float maxW = 0;
            var lineBounds = new List<(string line, SKRect bounds)>();
            foreach (var line in lines)
            {
                if (line.Length == 0) { lineBounds.Add((line, default)); continue; }
                popupFont.MeasureText(line, out var tb);
                lineBounds.Add((line, tb));
                maxW = Math.Max(maxW, tb.Width);
            }
            maxW = Math.Max(maxW, popupFontSize * 3);
            float contentH = lines.Length * lineHeight;

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
    public double Width { get; set; } = 3;
    public double Opacity { get; set; } = 1.0;
    public bool IsHighlighter { get; set; }
    /// <summary>When true, points are connected with straight line segments instead of spline curves.</summary>
    public bool IsPolyline { get; set; }
    /// <summary>Dash pattern applied to the stroke.</summary>
    public LineDashPattern DashPattern { get; set; } = LineDashPattern.Solid;

    // Cached pen to avoid per-frame allocation during rendering.
    private IPen? _cachedPen;
    private double _cachedPenScale;

    internal IPen GetOrCreatePen(double penScale)
    {
        // Recreate only when the scale changes (zoom/resize) or first call
        if (_cachedPen == null || Math.Abs(_cachedPenScale - penScale) > 0.001)
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

    public void InvalidatePen() => _cachedPen = null;
}
