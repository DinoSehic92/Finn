using Avalonia;
using Avalonia.Media;
using Finn.Model;
using MuPDFCore.MuPDFRenderer;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
    private int _currentPage;
    private int _totalStrokeCount;

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
        _layers = layers ?? [];
        _activeLayer = _layers.Count > 0 ? _layers[0] : null;
        RecalculateStrokeCount();
        if (IsViewerInitialized)
            InvalidateVisual();
    }

    private void RecalculateStrokeCount()
    {
        int total = 0;
        foreach (var layer in _layers)
            total += layer.StrokeCount;
        _totalStrokeCount = total;
    }

    private AnnotationLayer? _activeLayer;
    public AnnotationLayer? ActiveLayer
    {
        get => _activeLayer;
        set { _activeLayer = value; InvalidateVisual(); }
    }

    public Color StrokeColor { get; set; } = Color.FromRgb(214, 64, 69);
    public double StrokeWidth { get; set; } = 3;
    public double StrokeOpacity { get; set; } = 1.0;
    public bool IsHighlighterMode { get; set; }

    public bool HasAnyStrokes => _totalStrokeCount > 0 || _activeStroke != null;

    public void SetStrokePage(int page)
    {
        if (page == _currentPage) return;
        // Cancel any in-progress stroke to prevent it leaking onto the new page
        _activeStroke = null;
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
        Layers.Remove(layer);
        if (ActiveLayer == layer)
            ActiveLayer = Layers.Count > 0 ? Layers[0] : null;
        InvalidateVisual();
    }

    public void ClearLayer(AnnotationLayer layer)
    {
        _totalStrokeCount = Math.Max(0, _totalStrokeCount - layer.StrokeCount);
        layer.PageStrokes.Clear();
        layer.StrokeCount = 0;
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
            IsHighlighter = IsHighlighterMode
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
            ActiveLayer.RefreshStatus();
        }

        _activeStroke = null;
        InvalidateVisual();
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
        InvalidateVisual();
    }

    #endregion

    #region Stroke Management (operates on active layer)

    public void Undo()
    {
        if (ActiveLayer == null) return;
        if (ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes) && strokes.Count > 0)
        {
            strokes.RemoveAt(strokes.Count - 1);
            ActiveLayer.StrokeCount = Math.Max(0, ActiveLayer.StrokeCount - 1);
            _totalStrokeCount = Math.Max(0, _totalStrokeCount - 1);
            ActiveLayer.RefreshStatus();
            InvalidateVisual();
        }
    }

    public void ClearPage()
    {
        if (ActiveLayer == null) return;
        if (ActiveLayer.PageStrokes.TryGetValue(_currentPage, out var strokes) && strokes.Count > 0)
        {
            int removed = strokes.Count;
            strokes.Clear();
            ActiveLayer.StrokeCount = Math.Max(0, ActiveLayer.StrokeCount - removed);
            _totalStrokeCount = Math.Max(0, _totalStrokeCount - removed);
            ActiveLayer.RefreshStatus();
            InvalidateVisual();
        }
    }

    public void ClearAll()
    {
        foreach (var layer in Layers)
        {
            layer.PageStrokes.Clear();
            layer.StrokeCount = 0;
            layer.RefreshStatus();
        }
        _totalStrokeCount = 0;
        InvalidateVisual();
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

        // Draw all visible layers
        foreach (var layer in Layers)
        {
            if (!layer.IsVisible) continue;
            if (layer.PageStrokes.TryGetValue(_currentPage, out var strokes))
            {
                foreach (var stroke in strokes)
                    RenderStroke(context, stroke, da, boundsSize, scaleX, scaleY);
            }
        }

        // Draw the active stroke being drawn
        if (_activeStroke != null)
            RenderStroke(context, _activeStroke, da, boundsSize, scaleX, scaleY);
    }

    private void RenderStroke(DrawingContext context, InkStroke stroke,
                              Rect da, Size boundsSize,
                              double scaleX, double scaleY)
    {
        var pts = stroke.Points;
        if (pts.Count < 2) return;

        double penScale = (scaleX + scaleY) * 0.5;
        var pen = stroke.GetOrCreatePen(penScale);

        // Pre-transform all points to screen space once
        int n = pts.Count;
        Span<Point> sp = n <= 256 ? stackalloc Point[n] : new Point[n];
        for (int i = 0; i < n; i++)
            sp[i] = PdfToScreen(pts[i], da, boundsSize);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(sp[0], false);

            if (n == 2)
            {
                ctx.LineTo(sp[1]);
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

    #endregion
}

public class InkStroke
{
    public List<Point> Points { get; set; } = [];
    public Color Color { get; set; } = Colors.Red;
    public double Width { get; set; } = 3;
    public double Opacity { get; set; } = 1.0;
    public bool IsHighlighter { get; set; }

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
            _cachedPen = new Pen(brush, Width * penScale, lineCap: cap, lineJoin: PenLineJoin.Round);
        }
        return _cachedPen;
    }
}
