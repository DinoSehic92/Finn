using Avalonia;
using Avalonia.Media;
using Newtonsoft.Json;

namespace Finn.Model;

/// <summary>
/// A text annotation placed at a specific PDF-space coordinate.
/// Does not modify the original file — purely an overlay.
/// </summary>
public class TextAnnotation
{
    private string _text = "";
    private double _fontSize = 10;
    private string _fontFamily = "";
    private double _maxWidth;

    public Point Position { get; set; }

    public string Text
    {
        get => _text;
        set { _text = value; InvalidateCachedBounds(); }
    }

    public double FontSize
    {
        get => _fontSize;
        set { _fontSize = value; InvalidateCachedBounds(); }
    }

    public Color Color { get; set; } = Color.FromRgb(214, 64, 69);
    public double Opacity { get; set; } = 1.0;

    /// <summary>Font family name. Empty or null uses the system default.</summary>
    public string FontFamily
    {
        get => _fontFamily;
        set { _fontFamily = value; InvalidateCachedBounds(); }
    }

    /// <summary>
    /// When set, an arrow is drawn from this anchor point to <see cref="Position"/>.
    /// Used by the ArrowText tool. Null for plain text annotations.
    /// </summary>
    public Point? ArrowOrigin { get; set; }

    /// <summary>
    /// Maximum width (PDF units) for word wrapping. 0 = no wrapping (single line).
    /// Set automatically for new annotations; the user can resize via the right-edge handle.
    /// </summary>
    public double MaxWidth
    {
        get => _maxWidth;
        set { _maxWidth = value; InvalidateCachedBounds(); }
    }

    /// <summary>
    /// When true, this annotation is rendered as a sticky-note icon (folded corner)
    /// and exported as a native PDF comment annotation (/Subtype /Text).
    /// The text content is shown in a hover popup rather than stamped on the page.
    /// </summary>
    public bool IsStickyNote { get; set; }

    // ── Cached measured bounds (set by renderer, used for accurate hit-testing) ──

    /// <summary>
    /// Pixel-accurate width of this annotation's rendered text, measured by SkiaSharp
    /// during the last render pass. In PDF-space units. 0 = not yet measured.
    /// </summary>
    [JsonIgnore]
    public double MeasuredWidth { get; set; }

    /// <summary>
    /// Pixel-accurate height of this annotation's rendered text, measured by SkiaSharp
    /// during the last render pass. In PDF-space units. 0 = not yet measured.
    /// </summary>
    [JsonIgnore]
    public double MeasuredHeight { get; set; }

    /// <summary>True when <see cref="MeasuredWidth"/>/<see cref="MeasuredHeight"/> are valid.</summary>
    [JsonIgnore]
    public bool HasMeasuredBounds => MeasuredWidth > 0 && MeasuredHeight > 0;

    private void InvalidateCachedBounds()
    {
        MeasuredWidth = 0;
        MeasuredHeight = 0;
    }
}
