using Finn.Model;
using MuPDFCore;
using MuPDFCore.StructuredText;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.Services
{
    /// <summary>
    /// Word-level diff engine for PDF files. Extracts text from each page using
    /// MuPDF's structured text API, compares word sequences using a longest-common-
    /// subsequence algorithm, and returns bounding rectangles for changed/added/removed words.
    /// Separate from <see cref="PdfDiffService"/> (pixel-based) — both engines can be
    /// used independently or combined.
    /// </summary>
    public static class TextDiffService
    {
        /// <summary>
        /// A word extracted from a PDF page, with its text and bounding box in PDF coordinates.
        /// </summary>
        private readonly record struct PageWord(string Text, double X, double Y, double Width, double Height);

        /// <summary>
        /// Compares two PDF files word-by-word for each page pair and returns
        /// <see cref="DiffResultData"/> entries with populated <see cref="DiffResultData.Regions"/>.
        /// No images are produced — this is a text-only comparison.
        /// </summary>
        public static async Task<List<DiffResultData>> CompareAsync(
            string pathA, string pathB,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            return await Task.Run(() => Compare(pathA, pathB, progress, ct), ct);
        }

        private static List<DiffResultData> Compare(
            string pathA, string pathB,
            IProgress<int>? progress,
            CancellationToken ct)
        {
            using var ctxA = new MuPDFContext();
            using var ctxB = new MuPDFContext();
            using var docA = new MuPDFDocument(ctxA, pathA);
            using var docB = new MuPDFDocument(ctxB, pathB);

            int pagesA = docA.Pages.Count;
            int pagesB = docB.Pages.Count;
            int maxPages = Math.Max(pagesA, pagesB);

            // Extract words from all pages first.
            var wordsA = new List<PageWord>[pagesA];
            var wordsB = new List<PageWord>[pagesB];

            for (int i = 0; i < pagesA; i++)
            {
                ct.ThrowIfCancellationRequested();
                wordsA[i] = ExtractWords(docA, i);
                progress?.Report(i * 40 / Math.Max(1, pagesA + pagesB));
            }
            for (int i = 0; i < pagesB; i++)
            {
                ct.ThrowIfCancellationRequested();
                wordsB[i] = ExtractWords(docB, i);
                progress?.Report((pagesA + i) * 40 / Math.Max(1, pagesA + pagesB));
            }

            // Compare page pairs.
            var results = new List<DiffResultData>(maxPages);
            for (int i = 0; i < maxPages; i++)
            {
                ct.ThrowIfCancellationRequested();

                var wA = i < pagesA ? wordsA[i] : [];
                var wB = i < pagesB ? wordsB[i] : [];

                var (hasDiff, regions) = DiffWords(wA, wB);

                results.Add(new DiffResultData
                {
                    PageIndex = i,
                    HasDifferences = hasDiff,
                    Regions = regions
                });

                progress?.Report(40 + (i + 1) * 60 / Math.Max(1, maxPages));
            }

            return results;
        }

        /// <summary>
        /// Extracts all words from a page along with their bounding boxes.
        /// Words are derived from the structured text characters, split on whitespace.
        /// </summary>
        private static List<PageWord> ExtractWords(MuPDFDocument doc, int pageIndex)
        {
            var words = new List<PageWord>();
            using var disposable = doc.GetStructuredTextPage(pageIndex);
            var page = (MuPDFStructuredTextPage)disposable;

            foreach (var block in page)
            {
                foreach (var line in block)
                {
                    var chars = line.Characters;
                    if (chars == null || chars.Length == 0) continue;

                    int wordStart = -1;
                    double minX = double.MaxValue, minY = double.MaxValue;
                    double maxX = double.MinValue, maxY = double.MinValue;
                    var wordChars = new System.Text.StringBuilder();

                    for (int ci = 0; ci < chars.Length; ci++)
                    {
                        var ch = chars[ci];
                        bool isSpace = char.IsWhiteSpace(ch.Character, 0);

                        if (isSpace)
                        {
                            if (wordStart >= 0)
                            {
                                words.Add(new PageWord(wordChars.ToString(), minX, minY, maxX - minX, maxY - minY));
                                wordChars.Clear();
                                wordStart = -1;
                                minX = double.MaxValue; minY = double.MaxValue;
                                maxX = double.MinValue; maxY = double.MinValue;
                            }
                        }
                        else
                        {
                            if (wordStart < 0) wordStart = ci;
                            wordChars.Append(ch.Character);

                            var q = ch.BoundingQuad;
                            double x0 = Math.Min(Math.Min(q.UpperLeft.X, q.LowerLeft.X),
                                                  Math.Min(q.UpperRight.X, q.LowerRight.X));
                            double y0 = Math.Min(Math.Min(q.UpperLeft.Y, q.LowerLeft.Y),
                                                  Math.Min(q.UpperRight.Y, q.LowerRight.Y));
                            double x1 = Math.Max(Math.Max(q.UpperLeft.X, q.LowerLeft.X),
                                                  Math.Max(q.UpperRight.X, q.LowerRight.X));
                            double y1 = Math.Max(Math.Max(q.UpperLeft.Y, q.LowerLeft.Y),
                                                  Math.Max(q.UpperRight.Y, q.LowerRight.Y));

                            if (x0 < minX) minX = x0;
                            if (y0 < minY) minY = y0;
                            if (x1 > maxX) maxX = x1;
                            if (y1 > maxY) maxY = y1;
                        }
                    }

                    if (wordStart >= 0)
                        words.Add(new PageWord(wordChars.ToString(), minX, minY, maxX - minX, maxY - minY));
                }
            }

            return words;
        }

        /// <summary>
        /// Computes a word-level diff between two word lists using the LCS algorithm.
        /// Returns bounding rectangles for words that differ (present in A but not B,
        /// present in B but not A, or changed between the two).
        /// </summary>
        private static (bool HasDifferences, List<DiffRegion>? Regions) DiffWords(
            List<PageWord> wordsA, List<PageWord> wordsB)
        {
            int m = wordsA.Count;
            int n = wordsB.Count;

            if (m == 0 && n == 0)
                return (false, null);

            // Build LCS table.
            int[,] dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                {
                    if (string.Equals(wordsA[i - 1].Text, wordsB[j - 1].Text, StringComparison.Ordinal))
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }

            // Backtrack to find non-matching words.
            var regions = new List<DiffRegion>();
            int ia = m, ib = n;
            while (ia > 0 || ib > 0)
            {
                if (ia > 0 && ib > 0 &&
                    string.Equals(wordsA[ia - 1].Text, wordsB[ib - 1].Text, StringComparison.Ordinal))
                {
                    ia--; ib--;
                }
                else if (ib > 0 && (ia == 0 || dp[ia, ib - 1] >= dp[ia - 1, ib]))
                {
                    // Word added in B — highlight at B's position.
                    var w = wordsB[ib - 1];
                    regions.Add(new DiffRegion(w.X, w.Y, w.Width, w.Height));
                    ib--;
                }
                else
                {
                    // Word removed from A — highlight at A's position.
                    var w = wordsA[ia - 1];
                    regions.Add(new DiffRegion(w.X, w.Y, w.Width, w.Height));
                    ia--;
                }
            }

            if (regions.Count == 0)
                return (false, null);

            regions.Reverse();
            return (true, regions);
        }
    }
}
