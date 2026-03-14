using Avalonia;
using Avalonia.Media;

namespace Finn.Model;

/// <summary>
/// A text annotation placed at a specific PDF-space coordinate.
/// Does not modify the original file — purely an overlay.
/// </summary>
public class TextAnnotation
{
    public Point Position { get; set; }
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 14;
    public Color Color { get; set; } = Color.FromRgb(214, 64, 69);
    public double Opacity { get; set; } = 1.0;
    /// <summary>Font family name. Empty or null uses the system default.</summary>
    public string FontFamily { get; set; } = "";

    /// <summary>
    /// When set, an arrow is drawn from this anchor point to <see cref="Position"/>.
    /// Used by the ArrowText tool. Null for plain text annotations.
    /// </summary>
    public Point? ArrowOrigin { get; set; }
}
