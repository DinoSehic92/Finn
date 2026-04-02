using System;
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
/// using SKBlendMode.Difference. An optional tint additively lifts dark areas
/// (inverted paper) toward a colored shade while keeping bright areas (inverted
/// text/lines) pure white. Place AFTER the content to invert in the visual tree
/// and set IsHitTestVisible="False".
/// </summary>
public class InvertColorControl : Control
{
    public static readonly StyledProperty<bool> IsInvertedProperty =
        AvaloniaProperty.Register<InvertColorControl, bool>(nameof(IsInverted));

    public static readonly StyledProperty<Color> BackgroundColorProperty =
        AvaloniaProperty.Register<InvertColorControl, Color>(nameof(BackgroundColor), Colors.Transparent);

    public static readonly StyledProperty<Color> TintColorProperty =
        AvaloniaProperty.Register<InvertColorControl, Color>(nameof(TintColor), Colors.Black);

    public static readonly StyledProperty<int> TintIntensityProperty =
        AvaloniaProperty.Register<InvertColorControl, int>(nameof(TintIntensity), 15);

    public bool IsInverted
    {
        get => GetValue(IsInvertedProperty);
        set => SetValue(IsInvertedProperty, value);
    }

    public Color BackgroundColor
    {
        get => GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>
    /// Tint applied via an additive (Plus) pass after the inversion.
    /// Black = no tint (standard inversion). Any other color lifts the
    /// dark inverted areas (paper) toward the tint shade while leaving
    /// bright areas (text/lines) pure white.
    /// </summary>
    public Color TintColor
    {
        get => GetValue(TintColorProperty);
        set => SetValue(TintColorProperty, value);
    }

    /// <summary>
    /// Strength of the Multiply pass that shifts colors toward the tint hue.
    /// 0 = no effect, 50 = maximum shift. Default 15.
    /// </summary>
    public int TintIntensity
    {
        get => GetValue(TintIntensityProperty);
        set => SetValue(TintIntensityProperty, value);
    }

    static InvertColorControl()
    {
        AffectsRender<InvertColorControl>(IsInvertedProperty);
        AffectsRender<InvertColorControl>(BackgroundColorProperty);
        AffectsRender<InvertColorControl>(TintColorProperty);
        AffectsRender<InvertColorControl>(TintIntensityProperty);
    }

    public override void Render(DrawingContext context)
    {
        if (!IsInverted)
            return;

        context.Custom(new InvertDrawOperation(new Rect(Bounds.Size), BackgroundColor, TintColor, TintIntensity));
    }

    private sealed class InvertDrawOperation(Rect bounds, Color backgroundColor, Color tintColor, int tintIntensity) : ICustomDrawOperation
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

            var rect = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);

            canvas.Save();
            canvas.ClipRect(rect);

            // Pass 1: invert via Difference with white.
            using var invertPaint = new SKPaint
            {
                Color = SKColors.White,
                BlendMode = SKBlendMode.Difference
            };
            canvas.DrawRect(rect, invertPaint);

            // Pass 2 (optional): tint via Plus (additive) blend.
            bool hasTint = tintColor.R != 0 || tintColor.G != 0 || tintColor.B != 0;
            if (hasTint)
            {
                using var tintPaint = new SKPaint
                {
                    Color = new SKColor(tintColor.R, tintColor.G, tintColor.B, 255),
                    BlendMode = SKBlendMode.Plus
                };
                canvas.DrawRect(rect, tintPaint);

                // Pass 2b: Multiply with a near-white color derived from the tint.
                int maxT = Math.Max(tintColor.R, Math.Max(tintColor.G, tintColor.B));
                int mulStrength = Math.Clamp(tintIntensity, 0, 50);
                if (maxT > 0 && mulStrength > 0)
                {
                    byte mR = (byte)(255 - (maxT - tintColor.R) * mulStrength / maxT);
                    byte mG = (byte)(255 - (maxT - tintColor.G) * mulStrength / maxT);
                    byte mB = (byte)(255 - (maxT - tintColor.B) * mulStrength / maxT);

                    using var mulPaint = new SKPaint
                    {
                        Color = new SKColor(mR, mG, mB, 255),
                        BlendMode = SKBlendMode.Multiply
                    };
                    canvas.DrawRect(rect, mulPaint);
                }
            }

            // Pass 3: preserve original alpha from the background color.
            using var alphaPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, backgroundColor.A),
                BlendMode = SKBlendMode.DstIn
            };
            canvas.DrawRect(rect, alphaPaint);

            canvas.Restore();
        }
    }
}