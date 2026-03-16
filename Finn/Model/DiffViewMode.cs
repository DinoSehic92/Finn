namespace Finn.Model
{
    /// <summary>
    /// How diff results are displayed in the main previewer.
    /// </summary>
    public enum DiffViewMode
    {
        /// <summary>Red highlights overlaid on the current page.</summary>
        Overlay,
        /// <summary>A/B wipe slider — left shows original, right shows revised.</summary>
        Slider,
        /// <summary>Side-by-side using the dual page infrastructure.</summary>
        SideBySide
    }
}
