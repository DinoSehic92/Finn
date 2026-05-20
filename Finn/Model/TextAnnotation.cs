using Avalonia;
using Avalonia.Media;
using System;
using System.Text.Json.Serialization;

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
    public int ZIndex { get; set; }

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

    private Color _color = Color.FromRgb(214, 64, 69);
    private double _opacity = 1.0;
    private Point? _arrowOrigin;

    public Color Color
    {
        get => _color;
        set { _color = value; InvalidateArrowPen(); }
    }

    public double Opacity
    {
        get => _opacity;
        set { _opacity = value; InvalidateArrowPen(); }
    }

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
    public Point? ArrowOrigin
    {
        get => _arrowOrigin;
        set { _arrowOrigin = value; InvalidateArrowPen(); }
    }

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

    /// <summary>
    /// When true, this annotation is rendered as a simple label (plain text with
    /// a subtle background pill, no textbox frame). Used for diff version labels.
    /// Label text is centered at <see cref="Position"/> and uses a fixed screen-space
    /// font size so it doesn't grow/shrink with zoom.
    /// </summary>
    public bool IsLabel { get; set; }

    /// <summary>
    /// When true (default), the annotation is rendered with a tinted-glass background
    /// (semi-transparent fill derived from the annotation color) and a colored border frame.
    /// When false, only the text is drawn with no background or border, so it appears as
    /// plain text overlaid directly on the page.
    /// </summary>
    public bool HasFrame { get; set; } = true;

    /// <summary>
    /// Page rotation in degrees at the time this text annotation was created.
    /// Used to preserve the textbox's visual orientation relative to its creation view.
    /// </summary>
    public double CreatedAtRotation { get; set; }

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

    // Cached arrow pen to avoid per-frame allocation during rendering.
    [JsonIgnore] private IPen? _cachedArrowPen;
    [JsonIgnore] private double _cachedArrowPenScale;

    internal IPen GetOrCreateArrowPen(double penScale)
    {
        if (_cachedArrowPen == null || Math.Abs(_cachedArrowPenScale - penScale) > 0.05)
        {
            _cachedArrowPenScale = penScale;
            byte alpha = Opacity < 1.0 ? (byte)(Opacity * 255) : (byte)255;
            var c = Color.FromArgb(alpha, Color.R, Color.G, Color.B);
            _cachedArrowPen = new Pen(new SolidColorBrush(c).ToImmutable(),
                1.2, lineCap: PenLineCap.Round);
        }
        return _cachedArrowPen;
    }

    internal void InvalidateArrowPen() => _cachedArrowPen = null;
}
