using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Finn.Model;

namespace Finn.Controls;

public sealed class StructuralLinePatternIcon : Control
{
    public static readonly StyledProperty<LineDashPattern> PatternProperty =
        AvaloniaProperty.Register<StructuralLinePatternIcon, LineDashPattern>(nameof(Pattern));

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<StructuralLinePatternIcon, IBrush>(nameof(Stroke), Brushes.Black);

    public LineDashPattern Pattern
    {
        get => GetValue(PatternProperty);
        set => SetValue(PatternProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var points = new[]
        {
            new Point(0, Bounds.Height / 2),
            new Point(Bounds.Width, Bounds.Height / 2)
        };
        var segments = AnnotatedPDFRenderer.GetPatternSegments(points, Pattern);
        var pen = new Pen(Stroke, 1.4);
        foreach (var segment in segments)
            context.DrawLine(pen, segment.start, segment.end);
    }
}
