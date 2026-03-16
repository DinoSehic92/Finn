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
    public double FontSize { get; set; } = 10;
    public Color Color { get; set; } = Color.FromRgb(214, 64, 69);
    public double Opacity { get; set; } = 1.0;
    /// <summary>Font family name. Empty or null uses the system default.</summary>
    public string FontFamily { get; set; } = "";

    /// <summary>
    /// When set, an arrow is drawn from this anchor point to <see cref="Position"/>.
    /// Used by the ArrowText tool. Null for plain text annotations.
    /// </summary>
    public Point? ArrowOrigin { get; set; }

    /// <summary>
    /// Maximum width (PDF units) for word wrapping. 0 = no wrapping (single line).
    /// Set automatically for new annotations; the user can resize via the right-edge handle.
    /// </summary>
    public double MaxWidth { get; set; }

    /// <summary>
    /// When true, this annotation is rendered as a sticky-note icon (folded corner)
    /// and exported as a native PDF comment annotation (/Subtype /Text).
    /// The text content is shown in a hover popup rather than stamped on the page.
    /// </summary>
    public bool IsStickyNote { get; set; }
}
