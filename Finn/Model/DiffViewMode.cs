namespace Finn.Model
{
    /// <summary>
    /// How diff results are displayed in the main previewer.
    /// </summary>
    public enum DiffViewMode
    {
        /// <summary>Red highlights overlaid on the current page.</summary>
        Overlay,
        /// <summary>A/B toggle — quickly swap between original and revised document.</summary>
        Toggle,
        /// <summary>Side-by-side using the dual page infrastructure.</summary>
        SideBySide
    }
}
