using Avalonia;
using Avalonia.Media;
using System;

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
    }

    // Cached pen to avoid per-frame allocation during rendering.
    private IPen? _cachedPen;
    private double _cachedPenScale;

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
        if (_cachedPen == null || Math.Abs(_cachedPenScale - penScale) > 0.001)
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

    // Cached dashed pen used while drawing the shape preview.
    private IPen? _cachedDashedPen;
    private double _cachedDashedPenScale;

    internal IPen GetOrCreateDashedPen(double penScale)
    {
        if (_cachedDashedPen == null || Math.Abs(_cachedDashedPenScale - penScale) > 0.001)
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
}
