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
    /// <para>
    /// Uses a cross-page document-stream comparison:
    /// 1. <b>Page alignment</b> — Jaccard similarity LCS handles inserted/removed pages.
    /// 2. <b>Segment-based stream comparison</b> — consecutive aligned pages are merged
    ///    into segments spanning page boundaries, so text that reflows between pages
    ///    is matched correctly instead of being flagged as removed-then-added.
    /// 3. <b>Confidence-based regions</b> — each diff region carries a confidence value
    ///    reflecting the alignment quality, rendered as opacity in the annotation layer.
    /// </para>
    /// Separate from <see cref="PdfDiffService"/> (pixel-based) — both engines can be
    /// used independently or combined.
    /// </summary>
    public static class TextDiffService
    {
        #region Types

        /// <summary>
        /// A word extracted from a PDF page, with its text and bounding box in PDF coordinates.
        /// </summary>
        private readonly record struct PageWord(string Text, string NormalizedText, double X, double Y, double Width, double Height);

        /// <summary>
        /// A word in the flat document stream with its source page index for
        /// mapping diff results back to per-page output.
        /// </summary>
        private readonly record struct DocumentWord(string NormalizedText, double X, double Y, double Width, double Height, int PageIndex);

        /// <summary>
        /// A paragraph/block of words extracted from a PDF page.
        /// Groups words that belong to the same structural text block.
        /// The <see cref="WordSet"/> is used for page-level Jaccard similarity.
        /// </summary>
        private sealed class TextBlock
        {
            public List<PageWord> Words { get; } = [];
            public HashSet<string> WordSet { get; } = new(StringComparer.OrdinalIgnoreCase);

            public void AddWord(PageWord word)
            {
                Words.Add(word);
                if (!string.IsNullOrEmpty(word.NormalizedText))
                    WordSet.Add(word.NormalizedText);
            }
        }

        /// <summary>
        /// An aligned page pair with a confidence score indicating alignment quality.
        /// </summary>
        private readonly record struct AlignedPair(int PageA, int PageB, double Confidence);

        #endregion

        #region Constants

        /// <summary>
        /// Minimum Jaccard similarity for two pages to be considered a "same page"
        /// match during page alignment.
        /// </summary>
        private const double PageMatchThreshold = 0.4;

        /// <summary>
        /// Minimum fraction of pages a word sequence must appear on to be
        /// classified as a header or footer (removed before comparison).
        /// </summary>
        private const double HeaderFooterPageFraction = 0.5;

        /// <summary>
        /// Maximum number of words at the top/bottom of each page to consider
        /// as potential header/footer content.
        /// </summary>
        private const int HeaderFooterMaxWords = 25;

        /// <summary>
        /// Maximum gap size (words per side) for standard LCS within the
        /// recursive patience diff. Gaps smaller than this use full-matrix LCS;
        /// larger gaps recurse with patience anchoring first.
        /// </summary>
        private const int MaxLcsGap = 400;

        /// <summary>
        /// Maximum recursion depth for the patience diff. At each level,
        /// locally-unique words become anchors, producing smaller gaps.
        /// Three levels handles virtually all real documents.
        /// </summary>
        private const int MaxPatienceDepth = 3;

        #endregion

        #region Public API

        /// <summary>
        /// Compares two PDF files word-by-word using cross-page stream comparison
        /// and returns <see cref="DiffResultData"/> entries with populated
        /// <see cref="DiffResultData.Regions"/>. No images are produced.
        /// </summary>
        public static async Task<List<DiffResultData>> CompareAsync(
            string pathA, string pathB,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            return await Task.Run(() => Compare(pathA, pathB, progress, ct), ct);
        }

        #endregion

        #region Core Comparison

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

            // Phase 1 (0–30 %): extract text from all pages.
            var blocksA = new List<TextBlock>[pagesA];
            var blocksB = new List<TextBlock>[pagesB];

            for (int i = 0; i < pagesA; i++)
            {
                ct.ThrowIfCancellationRequested();
                blocksA[i] = ExtractBlocks(docA, i);
                progress?.Report(i * 30 / Math.Max(1, pagesA + pagesB));
            }
            for (int i = 0; i < pagesB; i++)
            {
                ct.ThrowIfCancellationRequested();
                blocksB[i] = ExtractBlocks(docB, i);
                progress?.Report((pagesA + i) * 30 / Math.Max(1, pagesA + pagesB));
            }

            // Phase 2 (30–35 %): strip repeated headers and footers.
            StripHeadersAndFooters(blocksA, pagesA);
            StripHeadersAndFooters(blocksB, pagesB);
            progress?.Report(35);

            // Phase 3 (35–40 %): align pages using Jaccard similarity.
            var alignment = AlignPages(blocksA, blocksB, pagesA, pagesB);
            progress?.Report(40);

            // Phase 4 (40–50 %): build flat document word streams.
            var streamA = BuildDocumentStream(blocksA, pagesA);
            var streamB = BuildDocumentStream(blocksB, pagesB);
            var rangesA = BuildPageWordRanges(streamA, pagesA);
            var rangesB = BuildPageWordRanges(streamB, pagesB);
            progress?.Report(50);

            // Phase 5 (50–90 %): recursive patience diff on the full streams.
            // No segmentation — the entire document is compared as one stream.
            // Patience anchoring ensures paragraphs that reflow across pages
            // are matched correctly regardless of position.
            var changedA = new bool[streamA.Count];
            var changedB = new bool[streamB.Count];
            PatienceDiffMark(streamA, 0, streamA.Count, streamB, 0, streamB.Count,
                             changedA, changedB, MaxPatienceDepth, ct);

            // Phase 5b: suppress reflow false positives. When pages shift
            // (e.g. a page inserted in B), repeated text like section headers
            // can be misaligned by the LCS. If the same word is flagged as
            // "removed from A" AND "added to B" within the same aligned page
            // pair, it's a reflow artifact, not a real change.
            SuppressReflowFalsePositives(streamA, streamB, changedA, changedB,
                                          rangesA, rangesB, alignment);
            progress?.Report(90);

            // Phase 6 (90–100 %): map changed words to per-page results.
            var results = new List<DiffResultData>(alignment.Count);
            for (int idx = 0; idx < alignment.Count; idx++)
            {
                var pair = alignment[idx];
                var regions = new List<DiffRegion>();
                int totalWords = 0;

                if (pair.PageA >= 0)
                {
                    var (start, end) = rangesA[pair.PageA];
                    totalWords += end - start;
                    for (int i = start; i < end; i++)
                        if (changedA[i])
                        {
                            var w = streamA[i];
                            regions.Add(new DiffRegion(w.X, w.Y, w.Width, w.Height, pair.Confidence, DiffSide.RemovedFromA));
                        }
                }

                if (pair.PageB >= 0)
                {
                    var (start, end) = rangesB[pair.PageB];
                    totalWords += end - start;
                    for (int i = start; i < end; i++)
                        if (changedB[i])
                        {
                            var w = streamB[i];
                            regions.Add(new DiffRegion(w.X, w.Y, w.Width, w.Height, pair.Confidence, DiffSide.AddedToB));
                        }
                }

                results.Add(new DiffResultData
                {
                    PageIndex = idx,
                    HasDifferences = regions.Count > 0,
                    Regions = regions.Count > 0 ? regions : null,
                    PageLabelA = pair.PageA >= 0 ? pair.PageA + 1 : null,
                    PageLabelB = pair.PageB >= 0 ? pair.PageB + 1 : null,
                    TotalWords = totalWords,
                    ChangedWords = regions.Count
                });

                progress?.Report(90 + (idx + 1) * 10 / Math.Max(1, alignment.Count));
            }

            return results;
        }

        #endregion

        #region Text Extraction

        /// <summary>
        /// Extracts text from a page as a list of <see cref="TextBlock"/>s,
        /// preserving the structural block boundaries from the PDF.
        /// Each block contains its words with bounding boxes.
        /// </summary>
        private static List<TextBlock> ExtractBlocks(MuPDFDocument doc, int pageIndex)
        {
            var blocks = new List<TextBlock>();
            using var disposable = doc.GetStructuredTextPage(pageIndex);
            var page = (MuPDFStructuredTextPage)disposable;

            foreach (var block in page)
            {
                var tb = new TextBlock();

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
                                string raw = wordChars.ToString();
                                tb.AddWord(new PageWord(raw, NormalizeWord(raw), minX, minY, maxX - minX, maxY - minY));
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
                    {
                        string raw = wordChars.ToString();
                        tb.AddWord(new PageWord(raw, NormalizeWord(raw), minX, minY, maxX - minX, maxY - minY));
                    }
                }

                if (tb.Words.Count > 0)
                    blocks.Add(tb);
            }

            return blocks;
        }

        #endregion

        #region Page Alignment

        /// <summary>
        /// Aligns pages from documents A and B using Jaccard word-set similarity
        /// and LCS. Returns <see cref="AlignedPair"/>s with confidence scores.
        /// </summary>
        private static List<AlignedPair> AlignPages(
            List<TextBlock>[] blocksA, List<TextBlock>[] blocksB,
            int countA, int countB)
        {
            var pageSetsA = new HashSet<string>[countA];
            var pageSetsB = new HashSet<string>[countB];
            for (int i = 0; i < countA; i++)
                pageSetsA[i] = BuildPageWordSet(blocksA[i]);
            for (int i = 0; i < countB; i++)
                pageSetsB[i] = BuildPageWordSet(blocksB[i]);

            var sim = new double[countA, countB];
            for (int i = 0; i < countA; i++)
                for (int j = 0; j < countB; j++)
                    sim[i, j] = JaccardSimilarity(pageSetsA[i], pageSetsB[j]);

            int[,] dp = new int[countA + 1, countB + 1];
            for (int i = 1; i <= countA; i++)
                for (int j = 1; j <= countB; j++)
                {
                    if (sim[i - 1, j - 1] >= PageMatchThreshold)
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }

            var matchesReverse = new List<(int, int)>();
            int ia = countA, ib = countB;
            while (ia > 0 && ib > 0)
            {
                if (sim[ia - 1, ib - 1] >= PageMatchThreshold
                    && dp[ia, ib] == dp[ia - 1, ib - 1] + 1)
                {
                    matchesReverse.Add((ia - 1, ib - 1));
                    ia--; ib--;
                }
                else if (dp[ia - 1, ib] >= dp[ia, ib - 1])
                    ia--;
                else
                    ib--;
            }
            matchesReverse.Reverse();

            var aligned = new List<AlignedPair>();
            int prevA = 0, prevB = 0;
            foreach (var (ma, mb) in matchesReverse)
            {
                PairGap(aligned, prevA, ma, prevB, mb);
                aligned.Add(new AlignedPair(ma, mb, 1.0));
                prevA = ma + 1;
                prevB = mb + 1;
            }
            PairGap(aligned, prevA, countA, prevB, countB);

            return aligned;
        }

        /// <summary>
        /// Pairs unmatched pages in a gap by position with reduced confidence.
        /// Paired pages (both sides present) get 0.7; one-sided pages get 0.4.
        /// </summary>
        private static void PairGap(List<AlignedPair> aligned,
            int startA, int endA, int startB, int endB)
        {
            int gapA = endA - startA;
            int gapB = endB - startB;
            int paired = Math.Min(gapA, gapB);
            for (int k = 0; k < paired; k++)
                aligned.Add(new AlignedPair(startA + k, startB + k, 0.7));
            for (int k = paired; k < gapA; k++)
                aligned.Add(new AlignedPair(startA + k, -1, 0.4));
            for (int k = paired; k < gapB; k++)
                aligned.Add(new AlignedPair(-1, startB + k, 0.4));
        }

        private static HashSet<string> BuildPageWordSet(List<TextBlock> blocks)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var block in blocks)
                foreach (var word in block.WordSet)
                    set.Add(word);
            return set;
        }

        #endregion

        #region Document Stream

        /// <summary>
        /// Detects repeated header/footer word sequences that appear on a majority
        /// of pages, and removes them from the block lists in-place. This prevents
        /// the LCS from misaligning when pages are inserted/removed, since these
        /// repeated sequences would produce many ambiguous matches.
        /// </summary>
        private static void StripHeadersAndFooters(List<TextBlock>[] allBlocks, int pageCount)
        {
            if (pageCount < 3) return; // Need enough pages to detect repetition.

            int threshold = Math.Max(2, (int)(pageCount * HeaderFooterPageFraction));

            // Build a fingerprint for the first N and last N words of each page.
            // A fingerprint is the concatenation of normalized words, joined by '\0'.
            var headerFingerprints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var footerFingerprints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int p = 0; p < pageCount; p++)
            {
                var blocks = allBlocks[p];
                var pageWords = FlattenBlockWords(blocks);
                if (pageWords.Count == 0) continue;

                // Try header fingerprints of decreasing length.
                int headerMax = Math.Min(HeaderFooterMaxWords, pageWords.Count);
                for (int len = headerMax; len >= 2; len--)
                {
                    string fp = BuildFingerprint(pageWords, 0, len);
                    headerFingerprints[fp] = headerFingerprints.GetValueOrDefault(fp) + 1;
                }

                // Try footer fingerprints of decreasing length.
                int footerMax = Math.Min(HeaderFooterMaxWords, pageWords.Count);
                for (int len = footerMax; len >= 2; len--)
                {
                    string fp = BuildFingerprint(pageWords, pageWords.Count - len, len);
                    footerFingerprints[fp] = footerFingerprints.GetValueOrDefault(fp) + 1;
                }
            }

            // Find the longest header fingerprint that meets the threshold.
            string? bestHeader = FindLongestFrequentFingerprint(headerFingerprints, threshold);
            string? bestFooter = FindLongestFrequentFingerprint(footerFingerprints, threshold);

            if (bestHeader == null && bestFooter == null) return;

            int headerWordCount = bestHeader?.Split('\0').Length ?? 0;
            int footerWordCount = bestFooter?.Split('\0').Length ?? 0;

            // Strip matching words from each page's blocks.
            for (int p = 0; p < pageCount; p++)
            {
                var blocks = allBlocks[p];
                var pageWords = FlattenBlockWords(blocks);
                if (pageWords.Count == 0) continue;

                // Mark header word indices for removal.
                var removeIndices = new HashSet<int>();
                if (bestHeader != null && pageWords.Count >= headerWordCount)
                {
                    string fp = BuildFingerprint(pageWords, 0, headerWordCount);
                    if (string.Equals(fp, bestHeader, StringComparison.OrdinalIgnoreCase))
                        for (int i = 0; i < headerWordCount; i++)
                            removeIndices.Add(i);
                }
                if (bestFooter != null && pageWords.Count >= footerWordCount)
                {
                    string fp = BuildFingerprint(pageWords, pageWords.Count - footerWordCount, footerWordCount);
                    if (string.Equals(fp, bestFooter, StringComparison.OrdinalIgnoreCase))
                        for (int i = pageWords.Count - footerWordCount; i < pageWords.Count; i++)
                            removeIndices.Add(i);
                }

                if (removeIndices.Count == 0) continue;

                // Rebuild blocks without the removed words.
                int globalIdx = 0;
                for (int bi = 0; bi < blocks.Count; bi++)
                {
                    var oldBlock = blocks[bi];
                    var newBlock = new TextBlock();
                    foreach (var word in oldBlock.Words)
                    {
                        if (!removeIndices.Contains(globalIdx))
                            newBlock.AddWord(word);
                        globalIdx++;
                    }
                    blocks[bi] = newBlock;
                }
                // Remove empty blocks.
                blocks.RemoveAll(b => b.Words.Count == 0);
            }
        }

        /// <summary>Flattens all words across all blocks on a page into a single list.</summary>
        private static List<PageWord> FlattenBlockWords(List<TextBlock> blocks)
        {
            var words = new List<PageWord>();
            foreach (var block in blocks)
                words.AddRange(block.Words);
            return words;
        }

        /// <summary>
        /// Builds a '\0'-separated fingerprint from a slice of words.
        /// Digit-only tokens are replaced with "#" so that varying page numbers
        /// in headers/footers (e.g. "Page 5" vs "Page 6") still match.
        /// Digits embedded in longer tokens (e.g. URL fragments) are stripped
        /// so "url4900019" and "url4900020" produce the same fingerprint.
        /// </summary>
        private static string BuildFingerprint(List<PageWord> words, int start, int count)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = start; i < start + count; i++)
            {
                if (sb.Length > 0) sb.Append('\0');
                string normalized = words[i].NormalizedText;
                if (string.IsNullOrEmpty(normalized))
                {
                    sb.Append('#');
                    continue;
                }
                bool allDigits = true;
                foreach (char c in normalized)
                    if (!char.IsDigit(c)) { allDigits = false; break; }
                if (allDigits)
                {
                    sb.Append('#');
                }
                else
                {
                    // Strip digits from mixed tokens so embedded page numbers
                    // (e.g. in URLs) don't break matching.
                    foreach (char c in normalized)
                        if (!char.IsDigit(c))
                            sb.Append(c);
                }
            }
            return sb.ToString();
        }

        /// <summary>Finds the longest fingerprint that appears at least <paramref name="threshold"/> times.</summary>
        private static string? FindLongestFrequentFingerprint(Dictionary<string, int> fingerprints, int threshold)
        {
            string? best = null;
            int bestLen = 0;
            foreach (var (fp, count) in fingerprints)
            {
                if (count < threshold) continue;
                int len = fp.Split('\0').Length;
                if (len > bestLen)
                {
                    bestLen = len;
                    best = fp;
                }
            }
            return best;
        }

        /// <summary>
        /// Flattens all pages into a single word stream in reading order.
        /// Each word carries its source page index for mapping results back.
        /// </summary>
        private static List<DocumentWord> BuildDocumentStream(List<TextBlock>[] allBlocks, int pageCount)
        {
            var stream = new List<DocumentWord>();
            for (int p = 0; p < pageCount; p++)
                foreach (var block in allBlocks[p])
                    foreach (var word in block.Words)
                    {
                        // Skip words that normalize to empty (punctuation-only).
                        // These all compare as equal in the LCS, causing the
                        // alignment to drift and producing cascading false positives
                        // deeper into the document.
                        if (!string.IsNullOrEmpty(word.NormalizedText))
                            stream.Add(new DocumentWord(word.NormalizedText, word.X, word.Y, word.Width, word.Height, p));
                    }
            return stream;
        }

        /// <summary>
        /// Builds a start/end index pair for each page's words within the flat stream.
        /// </summary>
        private static (int Start, int End)[] BuildPageWordRanges(List<DocumentWord> stream, int pageCount)
        {
            var ranges = new (int, int)[pageCount];
            if (stream.Count == 0) return ranges;

            int currentPage = -1;
            int rangeStart = 0;

            for (int i = 0; i < stream.Count; i++)
            {
                if (stream[i].PageIndex != currentPage)
                {
                    if (currentPage >= 0 && currentPage < pageCount)
                        ranges[currentPage] = (rangeStart, i);
                    currentPage = stream[i].PageIndex;
                    rangeStart = i;
                }
            }
            if (currentPage >= 0 && currentPage < pageCount)
                ranges[currentPage] = (rangeStart, stream.Count);

            return ranges;
        }

        /// <summary>
        /// Post-processing pass that suppresses reflow false positives.
        /// When content shifts between pages (e.g. a page inserted in B),
        /// the LCS can misalign repeated text like section headers, flagging
        /// identical words as "removed from A" AND "added to B".
        /// <para>
        /// For each aligned page pair, words that appear as changed on BOTH
        /// sides with the same normalized text are unmarked. This is safe
        /// because a word genuinely removed from A would not appear as added
        /// to B with the same text — the diff would flag it only on one side.
        /// </para>
        /// </summary>
        private static void SuppressReflowFalsePositives(
            List<DocumentWord> streamA, List<DocumentWord> streamB,
            bool[] changedA, bool[] changedB,
            (int Start, int End)[] rangesA, (int Start, int End)[] rangesB,
            List<AlignedPair> alignment)
        {
            foreach (var pair in alignment)
            {
                if (pair.PageA < 0 || pair.PageB < 0) continue;

                var (sA, eA) = rangesA[pair.PageA];
                var (sB, eB) = rangesB[pair.PageB];

                // Build a multiset of changed words on B's page.
                var bBag = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                for (int i = sB; i < eB; i++)
                {
                    if (!changedB[i]) continue;
                    string text = streamB[i].NormalizedText;
                    if (string.IsNullOrEmpty(text)) continue;
                    if (!bBag.TryGetValue(text, out var list))
                        bBag[text] = list = [];
                    list.Add(i);
                }

                if (bBag.Count == 0) continue;

                // For each changed word in A, if the same word is changed in B
                // within this page pair, unmark both — it's a reflow artifact.
                for (int i = sA; i < eA; i++)
                {
                    if (!changedA[i]) continue;
                    string text = streamA[i].NormalizedText;
                    if (string.IsNullOrEmpty(text)) continue;
                    if (!bBag.TryGetValue(text, out var bList) || bList.Count == 0)
                        continue;

                    changedA[i] = false;
                    int bIdx = bList[^1]; // Take from end for O(1) removal
                    changedB[bIdx] = false;
                    bList.RemoveAt(bList.Count - 1);
                }
            }
        }

        #endregion

        #region Recursive Patience Diff

        /// <summary>
        /// Recursive patience diff: finds words unique within the current range
        /// in both streams, uses them as unambiguous anchors, then fills gaps
        /// between anchors. Large gaps recurse (finding new locally-unique
        /// anchors in the narrower context); small gaps use standard LCS.
        /// <para>
        /// This eliminates segmentation entirely — the full document is compared
        /// as one stream. Cross-page reflow is handled naturally because there
        /// are no artificial boundaries where words could be split between
        /// comparison windows.
        /// </para>
        /// </summary>
        private static void PatienceDiffMark(
            List<DocumentWord> streamA, int startA, int endA,
            List<DocumentWord> streamB, int startB, int endB,
            bool[] changedA, bool[] changedB,
            int depth, CancellationToken ct)
        {
            int m = endA - startA;
            int n = endB - startB;

            if (m == 0 && n == 0) return;
            if (m == 0) { for (int i = startB; i < endB; i++) changedB[i] = true; return; }
            if (n == 0) { for (int i = startA; i < endA; i++) changedA[i] = true; return; }

            // Small enough for standard LCS — no anchoring needed.
            if (m <= MaxLcsGap && n <= MaxLcsGap)
            {
                StandardLcsMark(streamA, startA, endA, streamB, startB, endB, changedA, changedB);
                return;
            }

            ct.ThrowIfCancellationRequested();

            // ── Find patience anchors: words unique within this sub-range ──
            var anchors = FindLocalAnchors(streamA, startA, endA, streamB, startB, endB);

            if (anchors.Count > 0)
            {
                // Process each gap between consecutive anchors.
                int prevA = startA, prevB = startB;
                foreach (var (ancA, ancB) in anchors)
                {
                    int gapA = ancA - prevA;
                    int gapB = ancB - prevB;

                    if (gapA > 0 || gapB > 0)
                    {
                        if (depth > 0 && (gapA > MaxLcsGap || gapB > MaxLcsGap))
                            PatienceDiffMark(streamA, prevA, ancA, streamB, prevB, ancB,
                                             changedA, changedB, depth - 1, ct);
                        else
                            StandardLcsMark(streamA, prevA, ancA, streamB, prevB, ancB,
                                           changedA, changedB);
                    }

                    // Anchor word itself is a match — not changed.
                    prevA = ancA + 1;
                    prevB = ancB + 1;
                }

                // Trailing gap after last anchor.
                int tailA = endA - prevA;
                int tailB = endB - prevB;
                if (tailA > 0 || tailB > 0)
                {
                    if (depth > 0 && (tailA > MaxLcsGap || tailB > MaxLcsGap))
                        PatienceDiffMark(streamA, prevA, endA, streamB, prevB, endB,
                                         changedA, changedB, depth - 1, ct);
                    else
                        StandardLcsMark(streamA, prevA, endA, streamB, prevB, endB,
                                       changedA, changedB);
                }
                return;
            }

            // No local anchors found — fall back to standard LCS.
            // Cap at MaxLcsGap to avoid huge matrices; mark excess as changed.
            if (m > MaxLcsGap || n > MaxLcsGap)
            {
                // Best-effort: LCS on what we can, mark the rest.
                int capA = Math.Min(m, MaxLcsGap);
                int capB = Math.Min(n, MaxLcsGap);
                StandardLcsMark(streamA, startA, startA + capA, streamB, startB, startB + capB,
                               changedA, changedB);
                for (int i = startA + capA; i < endA; i++) changedA[i] = true;
                for (int i = startB + capB; i < endB; i++) changedB[i] = true;
            }
            else
            {
                StandardLcsMark(streamA, startA, endA, streamB, startB, endB, changedA, changedB);
            }
        }

        /// <summary>
        /// Finds words that appear exactly once in each sub-range, then runs
        /// LCS on those positions to produce ordered anchor pairs.
        /// </summary>
        private static List<(int A, int B)> FindLocalAnchors(
            List<DocumentWord> streamA, int startA, int endA,
            List<DocumentWord> streamB, int startB, int endB)
        {
            // Count frequencies within the sub-range.
            var freqA = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var freqB = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = startA; i < endA; i++)
            {
                var t = streamA[i].NormalizedText;
                if (t.Length > 0)
                    freqA[t] = freqA.TryGetValue(t, out int c) ? c + 1 : 1;
            }
            for (int i = startB; i < endB; i++)
            {
                var t = streamB[i].NormalizedText;
                if (t.Length > 0)
                    freqB[t] = freqB.TryGetValue(t, out int c) ? c + 1 : 1;
            }

            // Patience words: unique in both sub-ranges.
            var patience = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in freqA)
                if (kvp.Value == 1 && freqB.TryGetValue(kvp.Key, out int bc) && bc == 1)
                    patience.Add(kvp.Key);

            if (patience.Count == 0) return [];

            // Collect positions of patience words.
            var pIdxA = new List<int>();
            var pIdxB = new List<int>();
            for (int i = startA; i < endA; i++)
                if (patience.Contains(streamA[i].NormalizedText))
                    pIdxA.Add(i);
            for (int i = startB; i < endB; i++)
                if (patience.Contains(streamB[i].NormalizedText))
                    pIdxB.Add(i);

            if (pIdxA.Count == 0 || pIdxB.Count == 0) return [];

            // LCS on patience word sequences — matching is unambiguous.
            int m = pIdxA.Count, n = pIdxB.Count;
            int[,] dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                {
                    if (string.Equals(streamA[pIdxA[i - 1]].NormalizedText,
                                      streamB[pIdxB[j - 1]].NormalizedText,
                                      StringComparison.OrdinalIgnoreCase))
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }

            var anchors = new List<(int, int)>();
            int ia = m, ib = n;
            while (ia > 0 && ib > 0)
            {
                if (string.Equals(streamA[pIdxA[ia - 1]].NormalizedText,
                                  streamB[pIdxB[ib - 1]].NormalizedText,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    anchors.Add((pIdxA[ia - 1], pIdxB[ib - 1]));
                    ia--; ib--;
                }
                else if (dp[ia, ib - 1] >= dp[ia - 1, ib])
                    ib--;
                else
                    ia--;
            }

            anchors.Reverse();
            return anchors;
        }

        /// <summary>
        /// Standard word-level LCS on a small sub-range. Used to fill gaps
        /// between patience-diff anchors. Gaps are bounded by
        /// <see cref="MaxLcsGap"/> so the matrix stays small.
        /// </summary>
        private static void StandardLcsMark(
            List<DocumentWord> streamA, int startA, int endA,
            List<DocumentWord> streamB, int startB, int endB,
            bool[] changedA, bool[] changedB)
        {
            int m = endA - startA;
            int n = endB - startB;

            if (m == 0 && n == 0) return;
            if (m == 0) { for (int i = startB; i < endB; i++) changedB[i] = true; return; }
            if (n == 0) { for (int i = startA; i < endA; i++) changedA[i] = true; return; }

            int[,] dp = new int[m + 1, n + 1];
            for (int i = 1; i <= m; i++)
                for (int j = 1; j <= n; j++)
                {
                    var textA = streamA[startA + i - 1].NormalizedText;
                    var textB = streamB[startB + j - 1].NormalizedText;
                    if (textA.Length > 0 && string.Equals(textA, textB, StringComparison.OrdinalIgnoreCase))
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }

            int ia = m, ib = n;
            while (ia > 0 || ib > 0)
            {
                var btA = ia > 0 ? streamA[startA + ia - 1].NormalizedText : "";
                var btB = ib > 0 ? streamB[startB + ib - 1].NormalizedText : "";
                if (ia > 0 && ib > 0 && btA.Length > 0 &&
                    string.Equals(btA, btB, StringComparison.OrdinalIgnoreCase))
                {
                    ia--; ib--;
                }
                else if (ib > 0 && (ia == 0 || dp[ia, ib - 1] >= dp[ia - 1, ib]))
                {
                    changedB[startB + ib - 1] = true;
                    ib--;
                }
                else
                {
                    changedA[startA + ia - 1] = true;
                    ia--;
                }
            }
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Computes Jaccard similarity between two word sets: |intersection| / |union|.
        /// </summary>
        private static double JaccardSimilarity(HashSet<string> setA, HashSet<string> setB)
        {
            if (setA.Count == 0 && setB.Count == 0) return 1.0;
            if (setA.Count == 0 || setB.Count == 0) return 0.0;

            int intersection = 0;
            var (smaller, larger) = setA.Count <= setB.Count ? (setA, setB) : (setB, setA);
            foreach (var word in smaller)
                if (larger.Contains(word))
                    intersection++;

            int union = setA.Count + setB.Count - intersection;
            return union == 0 ? 1.0 : (double)intersection / union;
        }

        #endregion

        #region Normalization

        /// <summary>
        /// Normalizes a word for comparison by resolving font-dependent differences:
        /// - Unicode normalization (NFC) to merge composed/decomposed forms
        /// - Expand common typographic ligatures (fi, fl, ff, ffi, ffl)
        /// - Strip everything except letters and digits
        /// </summary>
        private static string NormalizeWord(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            text = text.Normalize(System.Text.NormalizationForm.FormC);

            var sb = new System.Text.StringBuilder(text.Length + 4);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '\uFB01': sb.Append("fi"); break;
                    case '\uFB02': sb.Append("fl"); break;
                    case '\uFB00': sb.Append("ff"); break;
                    case '\uFB03': sb.Append("ffi"); break;
                    case '\uFB04': sb.Append("ffl"); break;
                    default:
                        if (char.IsLetterOrDigit(c))
                            sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        #endregion
    }
}
