using Avalonia;
using Avalonia.Media;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Finn.Model;

/// <summary>
/// A distance measurement annotation stored in PDF-space coordinates.
/// By default, PDF units (1 pt = 1/72 inch) are converted to mm.
/// Use <see cref="Scale"/> to override with a calibrated ratio.
/// Does not modify the original file — purely an overlay.
/// </summary>
public class MeasurementAnnotation
{
    public List<Point> Points { get; set; } = [];
    public Color Color { get; set; } = Color.FromRgb(214, 64, 69);

    /// <summary>
    /// Millimetres per PDF point. Default = 25.4/72 (uncalibrated).
    /// Set via calibration to match the real-world document scale.
    /// </summary>
    public double Scale { get; set; } = 25.4 / 72.0;

    public double GetDistance()
    {
        if (Points.Count < 2) return 0;
        double dx = (Points[1].X - Points[0].X) * Scale;
        double dy = (Points[1].Y - Points[0].Y) * Scale;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public string GetLabel()
    {
        double d = GetDistance();
        return d >= 1000 ? $"{d / 1000:F2} m" : $"{d:F1} mm";
    }

    public Point GetLabelPosition()
    {
        if (Points.Count < 2) return default;
        return new Point((Points[0].X + Points[1].X) / 2, (Points[0].Y + Points[1].Y) / 2);
    }

    // Cached pen/brush to avoid per-frame allocation during rendering.
    [JsonIgnore] private IPen? _cachedPen;
    [JsonIgnore] private IBrush? _cachedBrush;
    [JsonIgnore] private double _cachedPenScale;

    internal (IPen pen, IBrush brush) GetOrCreatePen(double penScale)
    {
        if (_cachedPen == null || Math.Abs(_cachedPenScale - penScale) > 0.05)
        {
            _cachedPenScale = penScale;
            var c = Color.FromArgb(220, Color.R, Color.G, Color.B);
            _cachedBrush = new SolidColorBrush(c).ToImmutable();
            _cachedPen = new Pen(_cachedBrush, 1.5 * penScale,
                lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        }
        return (_cachedPen, _cachedBrush!);
    }

    internal void InvalidatePen() { _cachedPen = null; _cachedBrush = null; }
}
