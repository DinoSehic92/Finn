using System.Collections.Generic;

namespace Finn.Model
{
    /// <summary>
    /// Bounding rectangle for a contiguous region of changed pixels, in PDF-space coordinates.
    /// </summary>
    public record DiffRegion(double X, double Y, double Width, double Height);

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
        /// <summary>
        /// Diff regions computed during the pixel comparison. Avoids re-reading
        /// the diff image from disk when creating annotation layers.
        /// </summary>
        public List<DiffRegion>? Regions { get; set; }

        /// <summary>1-based page number in document A, or null if this page only exists in B.</summary>
        public int? PageLabelA { get; set; }
        /// <summary>1-based page number in document B, or null if this page only exists in A.</summary>
        public int? PageLabelB { get; set; }

        public string Label
        {
            get
            {
                if (PageLabelA == null && PageLabelB != null) return $"Page B:{PageLabelB} (added)";
                if (PageLabelB == null && PageLabelA != null) return $"Page A:{PageLabelA} (removed)";
                if (PageLabelA != null && PageLabelB != null && PageLabelA != PageLabelB)
                    return $"Page A:{PageLabelA} ↔ B:{PageLabelB}";
                return $"Page {PageIndex + 1}";
            }
        }
    }
}
