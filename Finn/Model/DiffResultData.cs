namespace Finn.Model
{
    /// <summary>
    /// Holds file paths to rendered page images and diff result for a single page pair.
    /// Images are stored as temp files to keep memory usage low for large documents.
    /// </summary>
    public class DiffResultData
    {
        public int PageIndex { get; set; }
        public string? OriginalPath { get; set; }
        public string? RevisedPath { get; set; }
        public string? DiffPath { get; set; }
        public bool HasDifferences { get; set; }
    }
}
