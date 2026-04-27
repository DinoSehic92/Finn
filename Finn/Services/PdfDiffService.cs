using Finn.Model;
using MuPDFCore;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.Services
{
    /// <summary>
    /// Renders two PDF files page-by-page and produces pixel-level diff images.
    /// Unchanged pixels are shown as dimmed grayscale; differences are highlighted in red.
    /// Uses SkiaSharp for cross-platform pixel operations and MuPDF's in-memory
    /// <see cref="MuPDFDocument.Render"/> for zero-disk-IO source rendering.
    /// Only diff result PNGs are written to a temp directory for overlay caching.
    /// Source page PNGs are written for the A/B toggle and side-by-side views.
    /// </summary>
    public static class PdfDiffService
    {
        public const int DefaultTolerance = 30;
        public const int MaxTolerance = 400;
        /// <summary>
        /// Zoom factor used when rendering PDF pages to images.
        /// At 2.0 the rendered bitmaps are 144 DPI (2× PDF points).
        /// DrawOps must multiply DisplayArea coordinates by this factor
        /// when sampling diff/slider bitmaps.
        /// </summary>
        public const float ZOOM = 2.0f;
        private const int REGION_CELL_SIZE = 8;

        /// <summary>Default B-side highlight color — cool blue.</summary>
        public const byte DefaultHighlightB_R = 60, DefaultHighlightB_G = 145, DefaultHighlightB_B = 220;

        /// <summary>
        /// Compare two PDF files asynchronously, returning per-page diff results
        /// and the path to the temporary directory holding rendered images.
        /// </summary>
        public static async Task<(List<DiffResultData> Results, string TempDir)> CompareAsync(
            string pathA, string pathB,
            IProgress<int>? progress = null,
            CancellationToken ct = default,
            int tolerance = DefaultTolerance,
            byte highlightR = 230, byte highlightG = 60, byte highlightB = 60)
        {
            return await Task.Run(() => Compare(pathA, pathB, progress, ct, tolerance, highlightR, highlightG, highlightB), ct);
        }

        /// <summary>
        /// Recomputes only the diff images for already-rendered page pairs using a new tolerance.
        /// Much faster than a full compare since PDF rendering is skipped.
        /// Reads source PNGs from disk and recomputes pixel diffs + regions.
        /// </summary>
        public static async Task RecomputeDiffsAsync(
            IList<DiffResultData> results,
            int tolerance,
            byte highlightR = 230, byte highlightG = 60, byte highlightB = 60,
            CancellationToken ct = default)
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(results, new ParallelOptions { CancellationToken = ct }, result =>
                {
                    ct.ThrowIfCancellationRequested();

                    if (result.OriginalPath == null || result.RevisedPath == null || result.DiffPath == null)
                        return;
                    if (!File.Exists(result.OriginalPath) || !File.Exists(result.RevisedPath))
                        return;

                    using var bmpA = SKBitmap.Decode(result.OriginalPath);
                    using var bmpB = SKBitmap.Decode(result.RevisedPath);
                    if (bmpA == null || bmpB == null) return;

                    var (hasDiff, regions) = ComputeAndSaveDiff(bmpA, bmpB, result.DiffPath, tolerance, highlightR, highlightG, highlightB);
                    result.HasDifferences = hasDiff;
                    result.Regions = regions;

                    // Regenerate B-side diff image with the alternate color.
                    if (hasDiff && result.DiffPathB != null)
                        RecolorDiffImage(result.DiffPath, result.DiffPathB, DefaultHighlightB_R, DefaultHighlightB_G, DefaultHighlightB_B);
                });
            }, ct);
        }

        private static (List<DiffResultData> Results, string TempDir) Compare(
            string pathA, string pathB,
            IProgress<int>? progress,
            CancellationToken ct,
            int tolerance,
            byte highlightR, byte highlightG, byte highlightB)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FinnDiff_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            try
            {
                using var ctxA = new MuPDFContext();
                using var ctxB = new MuPDFContext();
                using var docA = new MuPDFDocument(ctxA, pathA);
                using var docB = new MuPDFDocument(ctxB, pathB);

                int pagesA = docA.Pages.Count;
                int pagesB = docB.Pages.Count;
                int renderedCount = 0;
                int totalRenderPages = pagesA + pagesB;
                progress?.Report(0);

                // Phase 1 (0–50 %): compute lightweight perceptual hashes for both
                // documents. Render each page only long enough to hash it, then
                // dispose the bitmap immediately to avoid holding every page of
                // both PDFs in memory at once.
                ulong[] hashA = new ulong[pagesA];
                ulong[] hashB = new ulong[pagesB];

                Parallel.Invoke(
                    new ParallelOptions { CancellationToken = ct },
                    () =>
                    {
                        for (int i = 0; i < pagesA; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            using var bitmap = RenderPageToBitmap(docA, i);
                            hashA[i] = ComputePageHash(bitmap);
                            progress?.Report(Interlocked.Increment(ref renderedCount) * 50 / Math.Max(1, totalRenderPages));
                        }
                    },
                    () =>
                    {
                        for (int i = 0; i < pagesB; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            using var bitmap = RenderPageToBitmap(docB, i);
                            hashB[i] = ComputePageHash(bitmap);
                            progress?.Report(Interlocked.Increment(ref renderedCount) * 50 / Math.Max(1, totalRenderPages));
                        }
                    }
                );

                // Phase 2 (50–100 %): align pages to handle insertions/removals,
                // then compute diffs in parallel and write images.
                //
                // Naive index-based matching (page 0↔0, 1↔1, …) breaks when one
                // version has pages inserted or removed — every page after the
                // insertion shows as "different."  Instead, compute a quick
                // perceptual hash per page and use LCS to find the best alignment.
                var alignment = AlignPages(hashA, hashB, pagesA, pagesB);
                var results = new DiffResultData[alignment.Count];

                // Render aligned pages sequentially. This keeps peak memory low while
                // avoiding concurrent Render() calls against the same MuPDFDocument,
                // which is safer for native document lifetime/threading.
                for (int idx = 0; idx < alignment.Count; idx++)
                {
                    ct.ThrowIfCancellationRequested();
                    var (idxA, idxB) = alignment[idx];

                    using var bmpA = idxA >= 0 ? RenderPageToBitmap(docA, idxA) : null;
                    using var bmpB = idxB >= 0 ? RenderPageToBitmap(docB, idxB) : null;
                    string? fileA = null;
                    string? fileB = null;
                    string? fileD = null;
                    bool hasDiff;
                    List<DiffRegion>? regions = null;

                    string? fileDiffB = null;
                    if (bmpA != null && bmpB != null)
                    {
                        fileD = Path.Combine(tempDir, $"d_{idx}.png");
                        (hasDiff, regions) = ComputeAndSaveDiff(bmpA, bmpB, fileD, tolerance, highlightR, highlightG, highlightB);

                        if (!hasDiff && File.Exists(fileD))
                        {
                            try { File.Delete(fileD); } catch { }
                            fileD = null;
                        }
                        else if (hasDiff)
                        {
                            fileDiffB = Path.Combine(tempDir, $"d_{idx}_b.png");
                            RecolorDiffImage(fileD, fileDiffB, DefaultHighlightB_R, DefaultHighlightB_G, DefaultHighlightB_B);
                        }

                        fileA = Path.Combine(tempDir, $"a_{idx}.png");
                        fileB = Path.Combine(tempDir, $"b_{idx}.png");
                        SaveBitmapAsPng(bmpA, fileA);
                        SaveBitmapAsPng(bmpB, fileB);
                    }
                    else
                    {
                        hasDiff = bmpA != null || bmpB != null;
                        if (bmpA != null)
                        {
                            fileA = Path.Combine(tempDir, $"a_{idx}.png");
                            SaveBitmapAsPng(bmpA, fileA);
                        }
                        if (bmpB != null)
                        {
                            fileB = Path.Combine(tempDir, $"b_{idx}.png");
                            SaveBitmapAsPng(bmpB, fileB);
                        }
                    }

                    results[idx] = new DiffResultData
                    {
                        PageIndex = idx,
                        OriginalPath = fileA,
                        RevisedPath = fileB,
                        DiffPath = fileD,
                        DiffPathB = fileDiffB,
                        HasDifferences = hasDiff,
                        Regions = regions,
                        PageLabelA = idxA >= 0 ? idxA + 1 : null,
                        PageLabelB = idxB >= 0 ? idxB + 1 : null
                    };

                    progress?.Report(50 + (idx + 1) * 50 / Math.Max(1, alignment.Count));
                }

                return (new List<DiffResultData>(results), tempDir);
            }
            catch
            {
                try { Directory.Delete(tempDir, true); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Renders a single PDF page to an in-memory <see cref="SKBitmap"/> using
        /// MuPDF's native <c>Render</c> method. Returns BGRA pixel data.
        /// </summary>
        private static SKBitmap RenderPageToBitmap(MuPDFDocument doc, int pageIndex)
        {
            // MuPDF Render returns a byte[] in the requested pixel format.
            byte[] pixels = doc.Render(pageIndex, ZOOM, PixelFormats.BGRA);

            // Compute dimensions from the page size and zoom.
            var page = doc.Pages[pageIndex];
            int width = (int)Math.Ceiling(page.Bounds.Width * ZOOM);
            int height = (int)Math.Ceiling(page.Bounds.Height * ZOOM);

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var raw = new SKBitmap(info);

            // Copy rendered pixels into the raw bitmap.
            int bitmapBytes = info.RowBytes * height;
            int copyLen = Math.Min(pixels.Length, bitmapBytes);
            Marshal.Copy(pixels, 0, raw.GetPixels(), copyLen);

            // MuPDF renders BGRA with a transparent background (A=0). With
            // premultiplied alpha the RGB values of transparent pixels are all
            // zero, making two different pages look identical in an RGB-only
            // comparison. Flatten onto an opaque white background so the
            // pixel comparison sees the same colors the user sees on screen.
            var bitmap = new SKBitmap(info);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawBitmap(raw, 0, 0);
            }

            return bitmap;
        }

        /// <summary>
        /// Computes a pixel-level diff between two bitmaps, saves the diff image as PNG,
        /// and returns diff regions computed during the same pixel scan.
        /// </summary>
        private static (bool HasDifferences, List<DiffRegion>? Regions) ComputeAndSaveDiff(
            SKBitmap a, SKBitmap b, string outputPath, int tolerance,
            byte highlightR = 230, byte highlightG = 60, byte highlightB = 60)
        {
            int width = Math.Max(a.Width, b.Width);
            int height = Math.Max(a.Height, b.Height);

            using var padA = PadToSize(a, width, height);
            using var padB = PadToSize(b, width, height);

            var diffInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var diff = new SKBitmap(diffInfo);

            bool hasDifferences = false;

            // Grid for region extraction (computed during the same pixel scan).
            int cols = (width + REGION_CELL_SIZE - 1) / REGION_CELL_SIZE;
            int rows = (height + REGION_CELL_SIZE - 1) / REGION_CELL_SIZE;
            var grid = new bool[rows, cols];

            {
                var spanA = padA.GetPixelSpan();
                var spanB = padB.GetPixelSpan();
                // GetPixelSpan is read-only; we need writable access for the diff bitmap.
                // Copy into a managed buffer, write diffs, then copy back.
                int diffBytes = diff.RowBytes * height;
                byte[] bufD = new byte[diffBytes];
                int strideA = padA.RowBytes;
                int strideB = padB.RowBytes;
                int strideD = diff.RowBytes;

                for (int y = 0; y < height; y++)
                {
                    int rowOffA = y * strideA;
                    int rowOffB = y * strideB;
                    int rowOffD = y * strideD;

                    for (int x = 0; x < width; x++)
                    {
                        int offA = rowOffA + x * 4;
                        int offB = rowOffB + x * 4;
                        int offD = rowOffD + x * 4;
                        // BGRA format
                        int db = Math.Abs(spanA[offA] - spanB[offB]);
                        int dg = Math.Abs(spanA[offA + 1] - spanB[offB + 1]);
                        int dr = Math.Abs(spanA[offA + 2] - spanB[offB + 2]);

                        if (dr + dg + db > tolerance)
                        {
                            hasDifferences = true;
                            bufD[offD]     = highlightB; // B
                            bufD[offD + 1] = highlightG; // G
                            bufD[offD + 2] = highlightR; // R
                            bufD[offD + 3] = 255;        // A

                            grid[y / REGION_CELL_SIZE, x / REGION_CELL_SIZE] = true;
                        }
                        else
                        {
                            int gray = (spanA[offA] + spanA[offA + 1] + spanA[offA + 2]) / 3;
                            byte dimmed = (byte)Math.Clamp(gray / 2 + 128, 0, 255);
                            bufD[offD] = dimmed;
                            bufD[offD + 1] = dimmed;
                            bufD[offD + 2] = dimmed;
                            bufD[offD + 3] = 255;
                        }
                    }
                }

                Marshal.Copy(bufD, 0, diff.GetPixels(), diffBytes);
            }

            // Save diff image as PNG.
            using (var image = SKImage.FromBitmap(diff))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(outputPath))
            {
                data.SaveTo(stream);
            }

            // Extract regions from the grid computed during the pixel scan.
            var regions = hasDifferences ? ExtractRegionsFromGrid(grid, rows, cols, ZOOM) : null;

            return (hasDifferences, regions);
        }

        /// <summary>
        /// Pads/copies a bitmap to the target size with consistent BGRA8888 format.
        /// Always returns a NEW bitmap safe for the caller to dispose independently
        /// of the source. Extra space (if any) is filled with white.
        /// </summary>
        private static SKBitmap PadToSize(SKBitmap source, int width, int height)
        {
            var target = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(target);
            if (source.Width != width || source.Height != height)
                canvas.Clear(SKColors.White);
            canvas.DrawBitmap(source, 0, 0);
            return target;
        }

        private static void SaveBitmapAsPng(SKBitmap bitmap, string path)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(path);
            data.SaveTo(stream);
        }

        /// <summary>
        /// Greedy rectangle merge on a boolean grid. Returns bounding rectangles
        /// in PDF-space coordinates (pixel coords divided by zoom).
        /// </summary>
        private static List<DiffRegion> ExtractRegionsFromGrid(bool[,] grid, int rows, int cols, float zoom)
        {
            var regions = new List<DiffRegion>();
            var visited = new bool[rows, cols];

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (!grid[r, c] || visited[r, c]) continue;

                    int c2 = c;
                    while (c2 + 1 < cols && grid[r, c2 + 1] && !visited[r, c2 + 1]) c2++;

                    int r2 = r;
                    while (r2 + 1 < rows)
                    {
                        bool fullRow = true;
                        for (int cc = c; cc <= c2; cc++)
                            if (!grid[r2 + 1, cc] || visited[r2 + 1, cc]) { fullRow = false; break; }
                        if (!fullRow) break;
                        r2++;
                    }

                    for (int rr = r; rr <= r2; rr++)
                        for (int cc = c; cc <= c2; cc++)
                            visited[rr, cc] = true;

                    regions.Add(new DiffRegion(
                        c * REGION_CELL_SIZE / (double)zoom,
                        r * REGION_CELL_SIZE / (double)zoom,
                        (c2 - c + 1) * REGION_CELL_SIZE / (double)zoom,
                        (r2 - r + 1) * REGION_CELL_SIZE / (double)zoom));
                }

            return regions;
        }

        /// <summary>
        /// Scans a diff image and returns bounding rectangles (in PDF-space coordinates)
        /// for contiguous regions of changed pixels. Only used as a fallback when
        /// <see cref="DiffResultData.Regions"/> is not populated.
        /// </summary>
        public static List<DiffRegion> ExtractDiffRegions(
            string diffImagePath, float zoom, int cellSize = 8)
        {
            var regions = new List<DiffRegion>();
            if (!File.Exists(diffImagePath)) return regions;

            using var bmp = SKBitmap.Decode(diffImagePath);
            if (bmp == null) return regions;

            int w = bmp.Width, h = bmp.Height;
            int cols = (w + cellSize - 1) / cellSize;
            int rows = (h + cellSize - 1) / cellSize;
            var grid = new bool[rows, cols];

            {
                var pixels = bmp.GetPixelSpan();
                int stride = bmp.RowBytes;

                for (int cy = 0; cy < rows; cy++)
                    for (int cx = 0; cx < cols; cx++)
                    {
                        bool found = false;
                        int pyEnd = Math.Min((cy + 1) * cellSize, h);
                        int pxEnd = Math.Min((cx + 1) * cellSize, w);
                        for (int py = cy * cellSize; py < pyEnd && !found; py++)
                            for (int px = cx * cellSize; px < pxEnd && !found; px++)
                            {
                                int idx = py * stride + px * 4; // BGRA
                                    // Detect highlight pixels: unchanged pixels are dimmed grayscale
                                    // (R==G==B), so any pixel where channels differ is a highlight.
                                    byte pB = pixels[idx], pG = pixels[idx + 1], pR = pixels[idx + 2];
                                    if (pR != pG || pG != pB)
                                        found = true;
                            }
                        grid[cy, cx] = found;
                    }
            }

            return ExtractRegionsFromGrid(grid, rows, cols, zoom);
        }

        /// <summary>
        /// Reads a diff image and replaces all highlight pixels (non-grayscale)
        /// with a new color. Grayscale (unchanged) pixels are kept as-is.
        /// Used to produce the B-side overlay with a different highlight color.
        /// </summary>
        public static void RecolorDiffImage(string sourcePath, string destPath,
            byte newR, byte newG, byte newB)
        {
            using var bmp = SKBitmap.Decode(sourcePath);
            if (bmp == null) return;

            int w = bmp.Width, h = bmp.Height;
            var span = bmp.GetPixelSpan();
            int stride = bmp.RowBytes;
            int totalBytes = stride * h;
            byte[] buf = new byte[totalBytes];
            span.CopyTo(buf);

            for (int y = 0; y < h; y++)
            {
                int rowOff = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int off = rowOff + x * 4; // BGRA
                    byte pB = buf[off], pG = buf[off + 1], pR = buf[off + 2];
                    // Highlight pixels have channels that differ; grayscale has R==G==B.
                    if (pR != pG || pG != pB)
                    {
                        buf[off]     = newB;
                        buf[off + 1] = newG;
                        buf[off + 2] = newR;
                        // Alpha stays the same.
                    }
                }
            }

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var outBmp = new SKBitmap(info);
            Marshal.Copy(buf, 0, outBmp.GetPixels(), totalBytes);

            using var image = SKImage.FromBitmap(outBmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(destPath);
            data.SaveTo(stream);
        }

        #region Page Alignment

        /// <summary>
        /// Aligns pages from documents A and B using perceptual hashing and
        /// longest-common-subsequence (LCS). Returns a list of (indexA, indexB)
        /// pairs where -1 means "no match" (inserted or removed page).
        /// When both documents have the same page count and content hasn't
        /// shifted, the result is simply (0,0), (1,1), … — zero overhead.
        /// </summary>
        private static List<(int A, int B)> AlignPages(
            ulong[] hashA, ulong[] hashB, int countA, int countB)
        {
            // Fast path: same page count — skip alignment entirely.
            if (countA == countB)
            {
                var simple = new List<(int, int)>(countA);
                for (int i = 0; i < countA; i++) simple.Add((i, i));
                return simple;
            }

            // LCS on the hash sequences to find matching pages.
            int[,] dp = new int[countA + 1, countB + 1];
            for (int i = 1; i <= countA; i++)
                for (int j = 1; j <= countB; j++)
                    dp[i, j] = hashA[i - 1] == hashB[j - 1]
                        ? dp[i - 1, j - 1] + 1
                        : Math.Max(dp[i - 1, j], dp[i, j - 1]);

            // Back-trace to build aligned pairs.
            var aligned = new List<(int, int)>();
            int ia = countA, ib = countB;
            var matchesReverse = new List<(int, int)>();
            while (ia > 0 && ib > 0)
            {
                if (hashA[ia - 1] == hashB[ib - 1])
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

            // Merge matches with unmatched pages (insertions/removals).
            int prevA = 0, prevB = 0;
            foreach (var (ma, mb) in matchesReverse)
            {
                // Pages only in A (removed in B)
                while (prevA < ma) { aligned.Add((prevA, -1)); prevA++; }
                // Pages only in B (inserted)
                while (prevB < mb) { aligned.Add((-1, prevB)); prevB++; }
                // Matched pair
                aligned.Add((ma, mb));
                prevA = ma + 1;
                prevB = mb + 1;
            }
            // Trailing unmatched pages
            while (prevA < countA) { aligned.Add((prevA, -1)); prevA++; }
            while (prevB < countB) { aligned.Add((-1, prevB)); prevB++; }

            return aligned;
        }

        /// <summary>
        /// Computes a 64-bit perceptual hash by downsampling the bitmap to
        /// 8×8 grayscale and comparing each pixel to the mean.
        /// Two pages with the same visual content produce the same hash even
        /// if they're at different indices in their respective documents.
        /// </summary>
        private static ulong ComputePageHash(SKBitmap bitmap)
        {
            // Downsample to 8×8 using high-quality resize.
            using var small = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(small))
            {
                canvas.Clear(SKColors.White);
                var dest = new SKRect(0, 0, 8, 8);
                using var paint = new SKPaint { IsAntialias = true };
                canvas.DrawBitmap(bitmap, dest, paint);
            }

            // Convert to grayscale values.
            var span = small.GetPixelSpan();
            int stride = small.RowBytes;
            double[] gray = new double[64];
            double sum = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    int off = y * stride + x * 4;
                    double g = (span[off] + span[off + 1] + span[off + 2]) / 3.0;
                    gray[y * 8 + x] = g;
                    sum += g;
                }

            // Build hash: each bit = 1 if pixel > mean.
            double mean = sum / 64.0;
            ulong hash = 0;
            for (int i = 0; i < 64; i++)
                if (gray[i] > mean)
                    hash |= 1UL << i;
            return hash;
        }

        #endregion
    }
}
