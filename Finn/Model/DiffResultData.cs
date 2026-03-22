using System.Collections.Generic;

namespace Finn.Model
{
    /// <summary>
    /// Which document a diff region originates from.
    /// </summary>
    public enum DiffSide
    {
        /// <summary>Region from pixel diff or unknown source — shown on both sides.</summary>
        Both,
        /// <summary>Word present in A but not B (removed). Bounding box is in A's coordinate space.</summary>
        RemovedFromA,
        /// <summary>Word present in B but not A (added). Bounding box is in B's coordinate space.</summary>
        AddedToB
    }

    /// <summary>
    /// Bounding rectangle for a contiguous region of changed text/pixels, in PDF-space coordinates.
    /// <see cref="Confidence"/> indicates alignment quality: 1.0 for changes found in
    /// well-matched page pairs, lower values for gap-filled or unmatched pages.
    /// <see cref="Side"/> indicates which document the region belongs to, enabling
    /// color-coded A/B highlighting.
    /// </summary>
    public record DiffRegion(double X, double Y, double Width, double Height, double Confidence = 1.0, DiffSide Side = DiffSide.Both);

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
        /// <summary>B-side diff image (different highlight color) for the secondary renderer.</summary>
        public string? DiffPathB { get; set; }
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

        /// <summary>Total word count across both page sides (A + B). Set by text diff.</summary>
        public int TotalWords { get; set; }
        /// <summary>Number of words flagged as changed. Set by text diff.</summary>
        public int ChangedWords { get; set; }

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
