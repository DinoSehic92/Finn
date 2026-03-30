using Avalonia;
using Avalonia.Animation;

namespace Finn.Controls;

/// <summary>
/// Smoothly interpolates an <see cref="Avalonia.Rect"/> AvaloniaProperty
/// over a configurable duration. Used to animate <c>DisplayArea</c> on
/// the PDF renderer so that pan and zoom gestures feel fluid instead of
/// snapping instantly to the new viewport.
/// </summary>
public class RectTransition : InterpolatingTransitionBase<Rect>
{
    protected override Rect Interpolate(double progress, Rect from, Rect to)
    {
        return new Rect(
            from.X + (to.X - from.X) * progress,
            from.Y + (to.Y - from.Y) * progress,
            from.Width + (to.Width - from.Width) * progress,
            from.Height + (to.Height - from.Height) * progress);
    }
}
