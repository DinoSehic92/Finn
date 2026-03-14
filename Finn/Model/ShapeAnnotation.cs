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
    Text,
    ArrowText,
    MeasureDistance,
    RevisionCloud,
    Eraser
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

    public void InvalidatePen()
    {
        _cachedPen = null;
        _cachedDashedPen = null;
    }

    // Cached pen to avoid per-frame allocation during rendering.
    private IPen? _cachedPen;
    private double _cachedPenScale;

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
