using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Finn.Controls;

/// <summary>
/// Overlay control that inverts colors of everything already drawn on the canvas
/// using SKBlendMode.Difference with white. Place AFTER the content to invert
/// in the visual tree and set IsHitTestVisible="False".
/// </summary>
public class InvertColorControl : Control
{
    public static readonly StyledProperty<bool> IsInvertedProperty =
        AvaloniaProperty.Register<InvertColorControl, bool>(nameof(IsInverted));

    public bool IsInverted
    {
        get => GetValue(IsInvertedProperty);
        set => SetValue(IsInvertedProperty, value);
    }

    static InvertColorControl()
    {
        AffectsRender<InvertColorControl>(IsInvertedProperty);
    }

    public override void Render(DrawingContext context)
    {
        if (!IsInverted)
            return;

        context.Custom(new InvertDrawOperation(new Rect(Bounds.Size)));
    }

    private sealed class InvertDrawOperation(Rect bounds) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public void Dispose() { }
        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            // Difference with white: |dst - 1| = 1 - dst (inversion)
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                BlendMode = SKBlendMode.Difference
            };

            var rect = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);

            canvas.Save();
            canvas.ClipRect(rect);
            canvas.DrawRect(rect, paint);
            canvas.Restore();
        }
    }
}