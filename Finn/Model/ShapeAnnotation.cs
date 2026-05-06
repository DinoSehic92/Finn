using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace Finn.Model;

/// <summary>
/// Tool modes for the inline annotation system on the PDF renderer.
/// </summary>
public enum InlineAnnotationTool
{
    Select,
    Draw,
    Highlight,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Polyline,
    Text,
    ArrowText,
    StickyNote,
    MeasureDistance,
    MeasureArea,
    RevisionCloud,
    Eraser,
    Dot
}

/// <summary>
/// Dash pattern for annotation strokes.
/// </summary>
public enum LineDashPattern
{
    Solid,
    Dashed,
    Dotted,
    DashDot
}

/// <summary>
/// A geometric shape annotation (rectangle, ellipse, line, or arrow)
/// stored in PDF-space coordinates so it scales correctly with zoom/pan.
/// </summary>
public class ShapeAnnotation
{
    public InlineAnnotationTool ShapeType { get; set; }
    public Point Start { get; set; }
    public Point End { get; set; }
    public Color Color { get; set; } = Color.FromRgb(214, 64, 69);
    public double StrokeWidth { get; set; } = 3;
    public double Opacity { get; set; } = 1.0;
    /// <summary>When true, the shape is rendered with a translucent fill in addition to the stroke.</summary>
    public bool IsFilled { get; set; }
    /// <summary>Dash pattern applied to the shape stroke.</summary>
    public LineDashPattern DashPattern { get; set; } = LineDashPattern.Solid;
    /// <summary>
    /// Corner radius in PDF points for Rectangle shapes.
    /// 0 = sharp corners. Typical presets: 0, 2, 5, 10.
    /// </summary>
    public double CornerRadius { get; set; }

    public void InvalidatePen()
    {
        _cachedPen = null;
        _cachedDashedPen = null;
        _cachedFillBrush = null;
        _cachedDotBrush = null;
        _cachedCloudGeometry = null;
    }

    // Cached pen to avoid per-frame allocation during rendering.
    private IPen? _cachedPen;
    private double _cachedPenScale;
    private IBrush? _cachedFillBrush;
    private IBrush? _cachedDotBrush;

    // Cached cloud geometry — rebuilt only when screen-space endpoints or penScale change.
    private StreamGeometry? _cachedCloudGeometry;
    private Point _cachedCloudScreenStart;
    private Point _cachedCloudScreenEnd;
    private double _cachedCloudPenScale;

    // Cached dash styles — avoid allocating identical objects per pen creation
    private static readonly DashStyle s_dashed = new([4, 3], 0);
    private static readonly DashStyle s_dotted = new([1, 2], 0);
    private static readonly DashStyle s_dashDot = new([4, 2, 1, 2], 0);

    internal static DashStyle? GetDashStyle(LineDashPattern pattern) => pattern switch
    {
        LineDashPattern.Dashed => s_dashed,
        LineDashPattern.Dotted => s_dotted,
        LineDashPattern.DashDot => s_dashDot,
        _ => null
    };

    internal IPen GetOrCreatePen(double penScale)
    {
        if (_cachedPen == null || Math.Abs(_cachedPenScale - penScale) > 0.05)
        {
            _cachedPenScale = penScale;
            var c = Opacity < 1.0
                ? Color.FromArgb((byte)(Opacity * 255), Color.R, Color.G, Color.B)
                : Color;
            var brush = new SolidColorBrush(c).ToImmutable();
            _cachedPen = new Pen(brush, StrokeWidth * penScale,
                dashStyle: GetDashStyle(DashPattern),
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        }
        return _cachedPen;
    }

    internal IBrush GetOrCreateFillBrush()
    {
        _cachedFillBrush ??= new SolidColorBrush(
            Color.FromArgb(80, Color.R, Color.G, Color.B)).ToImmutable();
        return _cachedFillBrush;
    }

    // Cached dashed pen used while drawing the shape preview.
    private IPen? _cachedDashedPen;
    private double _cachedDashedPenScale;

    internal IPen GetOrCreateDashedPen(double penScale)
    {
        if (_cachedDashedPen == null || Math.Abs(_cachedDashedPenScale - penScale) > 0.05)
        {
            _cachedDashedPenScale = penScale;
            var c = Opacity < 1.0
                ? Color.FromArgb((byte)(Opacity * 255), Color.R, Color.G, Color.B)
                : Color;
            var brush = new SolidColorBrush(c).ToImmutable();
            _cachedDashedPen = new Pen(brush, StrokeWidth * penScale,
                dashStyle: new DashStyle([4, 3], 0),
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        }
        return _cachedDashedPen;
    }

    internal IBrush GetOrCreateDotBrush()
    {
        _cachedDotBrush ??= new SolidColorBrush(
            Opacity < 1.0
                ? Color.FromArgb((byte)(Opacity * 255), Color.R, Color.G, Color.B)
                : Color).ToImmutable();
        return _cachedDotBrush;
    }

    internal StreamGeometry GetOrCreateCloudGeometry(Point screenStart, Point screenEnd, double penScale)
    {
        if (_cachedCloudGeometry == null
            || _cachedCloudScreenStart != screenStart
            || _cachedCloudScreenEnd != screenEnd
            || Math.Abs(_cachedCloudPenScale - penScale) > 0.05)
        {
            _cachedCloudGeometry = BuildCloudPath(screenStart, screenEnd, penScale);
            _cachedCloudScreenStart = screenStart;
            _cachedCloudScreenEnd = screenEnd;
            _cachedCloudPenScale = penScale;
        }
        return _cachedCloudGeometry;
    }

    private static StreamGeometry BuildCloudPath(Point screenStart, Point screenEnd, double penScale)
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
}
