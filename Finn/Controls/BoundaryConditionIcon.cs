using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Finn.Model;
using System;
using System.Linq;

namespace Finn.Controls;

public sealed class BoundaryConditionIcon : Control
{
    private static readonly (Point start, Point end)[][] s_lines = Enum.GetValues<BoundaryConditionType>()
        .Select(type => CreateShape(type))
        .Select(AnnotatedPDFRenderer.GetBoundaryConditionLines)
        .ToArray();
    private static readonly Point[][] s_wheels = Enum.GetValues<BoundaryConditionType>()
        .Select(type => CreateShape(type))
        .Select(AnnotatedPDFRenderer.GetBoundaryConditionRollerCenters)
        .ToArray();

    public static readonly StyledProperty<BoundaryConditionType> BoundaryConditionProperty =
        AvaloniaProperty.Register<BoundaryConditionIcon, BoundaryConditionType>(nameof(BoundaryCondition));

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<BoundaryConditionIcon, IBrush>(nameof(Stroke), Brushes.Black);

    public BoundaryConditionType BoundaryCondition
    {
        get => GetValue(BoundaryConditionProperty);
        set => SetValue(BoundaryConditionProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private static ShapeAnnotation CreateShape(BoundaryConditionType type) => new()
    {
        ShapeType = InlineAnnotationTool.BoundaryCondition,
        BoundaryCondition = type,
        BoundaryOrientation = BoundaryConditionOrientation.Down,
        Start = new Point(0, 0)
    };

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        double scale = Math.Min((Bounds.Width - 2) / 24, (Bounds.Height - 2) / 28);
        var pen = new Pen(Stroke, 1.4);
        Point ToIcon(Point point) => new(Bounds.Width / 2 + point.X * scale, 1 + point.Y * scale);

        foreach (var line in s_lines[(int)BoundaryCondition])
            context.DrawLine(pen, ToIcon(line.start), ToIcon(line.end));
        foreach (var center in s_wheels[(int)BoundaryCondition])
            context.DrawEllipse(null, pen, ToIcon(center), 2 * scale, 2 * scale);
    }
}
