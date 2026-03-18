using Finn.Model;
using MuPDFCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SysBitmap = System.Drawing.Bitmap;
using SysColor = System.Drawing.Color;
using SysGraphics = System.Drawing.Graphics;
using SysImageFormat = System.Drawing.Imaging.ImageFormat;
using SysImageLockMode = System.Drawing.Imaging.ImageLockMode;
using SysPixelFormat = System.Drawing.Imaging.PixelFormat;
using SysRectangle = System.Drawing.Rectangle;

namespace Finn.Services
{
    /// <summary>
    /// Renders two PDF files page-by-page and produces pixel-level diff images.
    /// Unchanged pixels are shown as dimmed grayscale; differences are highlighted in red.
    /// All images are written to a temporary directory as files to keep memory bounded.
    /// </summary>
    public static class PdfDiffService
    {
        public const int DefaultTolerance = 250;
        public const int MaxTolerance = 1000;
        /// <summary>
        /// Zoom factor used when rendering PDF pages to images.
        /// At 2.0 the rendered bitmaps are 144 DPI (2× PDF points).
        /// DrawOps must multiply DisplayArea coordinates by this factor
        /// when sampling diff/slider bitmaps.
        /// </summary>
        public const float ZOOM = 2.0f;
        private const int JPEG_QUALITY = 90;

        /// <summary>
        /// Compare two PDF files asynchronously, returning per-page diff results
        /// and the path to the temporary directory holding rendered images.
        /// </summary>
        public static async Task<(List<DiffResultData> Results, string TempDir)> CompareAsync(
            string pathA, string pathB,
            IProgress<int>? progress = null,
            CancellationToken ct = default,
            int tolerance = DefaultTolerance)
        {
            return await Task.Run(() => Compare(pathA, pathB, progress, ct, tolerance), ct);
        }

        /// <summary>
        /// Recomputes only the diff images for already-rendered page pairs using a new tolerance.
        /// Much faster than a full compare since PDF rendering is skipped.
        /// </summary>
        public static async Task RecomputeDiffsAsync(
            IList<DiffResultData> results,
            int tolerance,
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

                    using var bmpA = new SysBitmap(result.OriginalPath);
                    using var bmpB = new SysBitmap(result.RevisedPath);
                    result.HasDifferences = ComputeAndSaveDiff(bmpA, bmpB, result.DiffPath, tolerance);
                });
            }, ct);
        }

        private static (List<DiffResultData> Results, string TempDir) Compare(
            string pathA, string pathB,
            IProgress<int>? progress,
            CancellationToken ct,
            int tolerance)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FinnDiff_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);
            try
            {
                // Use file-path constructor — avoids reading entire PDFs into managed memory.
                using var ctxA = new MuPDFContext();
                using var ctxB = new MuPDFContext();
                using var docA = new MuPDFDocument(ctxA, pathA);
                using var docB = new MuPDFDocument(ctxB, pathB);

                int pagesA = docA.Pages.Count;
                int pagesB = docB.Pages.Count;
                int maxPages = Math.Max(pagesA, pagesB);

                string?[] filesA = new string?[pagesA];
                string?[] filesB = new string?[pagesB];

                // Phase 1 (0–50 %): render both documents concurrently.
                // ctxA/docA and ctxB/docB are fully independent so this is thread-safe.
                int renderedCount = 0;
                int totalRenderPages = pagesA + pagesB;
                progress?.Report(0);

                Parallel.Invoke(
                    new ParallelOptions { CancellationToken = ct },
                    () =>
                    {
                        for (int i = 0; i < pagesA; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            filesA[i] = Path.Combine(tempDir, $"a_{i}.jpg");
                            docA.SaveImageAsJPEG(i, ZOOM, filesA[i], JPEG_QUALITY);
                            progress?.Report(Interlocked.Increment(ref renderedCount) * 50 / Math.Max(1, totalRenderPages));
                        }
                    },
                    () =>
                    {
                        for (int i = 0; i < pagesB; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            filesB[i] = Path.Combine(tempDir, $"b_{i}.jpg");
                            docB.SaveImageAsJPEG(i, ZOOM, filesB[i], JPEG_QUALITY);
                            progress?.Report(Interlocked.Increment(ref renderedCount) * 50 / Math.Max(1, totalRenderPages));
                        }
                    }
                );

                // Phase 2 (50–100 %): compute diffs in parallel — each page pair is independent.
                var results = new DiffResultData[maxPages];
                int diffedCount = 0;

                Parallel.For(0, maxPages, new ParallelOptions { CancellationToken = ct }, i =>
                {
                    ct.ThrowIfCancellationRequested();

                    string? fileA = i < pagesA ? filesA[i] : null;
                    string? fileB = i < pagesB ? filesB[i] : null;
                    string? fileD = null;
                    bool hasDiff;

                    if (fileA != null && fileB != null)
                    {
                        using var bmpA = new SysBitmap(fileA);
                        using var bmpB = new SysBitmap(fileB);
                        fileD = Path.Combine(tempDir, $"d_{i}.png");
                        hasDiff = ComputeAndSaveDiff(bmpA, bmpB, fileD, tolerance);
                        // Clean up the diff image if pages were identical —
                        // GetDiffImagePath returns null for !HasDifferences anyway.
                        if (!hasDiff && File.Exists(fileD))
                        {
                            try { File.Delete(fileD); } catch { }
                            fileD = null;
                        }
                    }
                    else
                    {
                        hasDiff = fileA != null || fileB != null;
                    }

                    results[i] = new DiffResultData
                    {
                        PageIndex = i,
                        OriginalPath = fileA,
                        RevisedPath = fileB,
                        DiffPath = fileD,
                        HasDifferences = hasDiff
                    };

                    progress?.Report(50 + Interlocked.Increment(ref diffedCount) * 50 / Math.Max(1, maxPages));
                });

                return (new List<DiffResultData>(results), tempDir);
            }
            catch
            {
                try { Directory.Delete(tempDir, true); } catch { }
                throw;
            }
        }

        private static bool ComputeAndSaveDiff(SysBitmap a, SysBitmap b, string outputPath, int tolerance)
        {
            int width = Math.Max(a.Width, b.Width);
            int height = Math.Max(a.Height, b.Height);

            using var padA = PadToSize(a, width, height);
            using var padB = PadToSize(b, width, height);
            using var diff = new SysBitmap(width, height, SysPixelFormat.Format32bppArgb);

            var rect = new SysRectangle(0, 0, width, height);
            var dataA = padA.LockBits(rect, SysImageLockMode.ReadOnly, SysPixelFormat.Format32bppArgb);
            var dataB = padB.LockBits(rect, SysImageLockMode.ReadOnly, SysPixelFormat.Format32bppArgb);
            var dataD = diff.LockBits(rect, SysImageLockMode.WriteOnly, SysPixelFormat.Format32bppArgb);

            bool hasDifferences = false;

            try
            {
                int stride = dataA.Stride;
                int totalBytes = stride * height;
                byte[] bufA = new byte[totalBytes];
                byte[] bufB = new byte[totalBytes];
                byte[] bufD = new byte[totalBytes];

                Marshal.Copy(dataA.Scan0, bufA, 0, totalBytes);
                Marshal.Copy(dataB.Scan0, bufB, 0, totalBytes);

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        int idx = rowOffset + x * 4;
                        // BGRA format
                        int db = Math.Abs(bufA[idx] - bufB[idx]);
                        int dg = Math.Abs(bufA[idx + 1] - bufB[idx + 1]);
                        int dr = Math.Abs(bufA[idx + 2] - bufB[idx + 2]);

                        if (dr + dg + db > tolerance)
                        {
                            hasDifferences = true;
                            bufD[idx] = 60;       // B
                            bufD[idx + 1] = 60;   // G
                            bufD[idx + 2] = 230;  // R
                            bufD[idx + 3] = 255;  // A
                        }
                        else
                        {
                            int gray = (bufA[idx] + bufA[idx + 1] + bufA[idx + 2]) / 3;
                            byte dimmed = (byte)Math.Clamp(gray / 2 + 128, 0, 255);
                            bufD[idx] = dimmed;
                            bufD[idx + 1] = dimmed;
                            bufD[idx + 2] = dimmed;
                            bufD[idx + 3] = 255;
                        }
                    }
                }

                Marshal.Copy(bufD, 0, dataD.Scan0, totalBytes);
            }
            finally
            {
                padA.UnlockBits(dataA);
                padB.UnlockBits(dataB);
                diff.UnlockBits(dataD);
            }

            diff.Save(outputPath, SysImageFormat.Png);
            return hasDifferences;
        }

        private static SysBitmap PadToSize(SysBitmap source, int width, int height)
        {
            // Skip the copy when the source already matches — avoids an allocation + blit.
            if (source.Width == width && source.Height == height)
            {
                // Clone into 32bppArgb so LockBits always gets a consistent format.
                return source.Clone(new SysRectangle(0, 0, width, height), SysPixelFormat.Format32bppArgb);
            }
            var padded = new SysBitmap(width, height, SysPixelFormat.Format32bppArgb);
            using var g = SysGraphics.FromImage(padded);
            g.Clear(SysColor.White);
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            return padded;
        }

        /// <summary>
        /// Scans a diff image and returns bounding rectangles (in PDF-space coordinates)
        /// for contiguous regions of changed pixels. The image was rendered at the given
        /// <paramref name="zoom"/> factor, so pixel coordinates are divided by zoom to
        /// convert back to PDF points.
        /// </summary>
        public static List<(double X, double Y, double Width, double Height)> ExtractDiffRegions(
            string diffImagePath, float zoom, int cellSize = 8)
        {
            var regions = new List<(double X, double Y, double Width, double Height)>();
            if (!File.Exists(diffImagePath)) return regions;

            using var bmp = new SysBitmap(diffImagePath);
            int w = bmp.Width, h = bmp.Height;
            int cols = (w + cellSize - 1) / cellSize;
            int rows = (h + cellSize - 1) / cellSize;
            var grid = new bool[rows, cols];

            var rect = new SysRectangle(0, 0, w, h);
            var data = bmp.LockBits(rect, SysImageLockMode.ReadOnly, SysPixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                byte[] buf = new byte[stride * h];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);

                // Mark grid cells that contain diff-red pixels (R≥200, G<100, B<100)
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
                                if (buf[idx + 2] >= 200 && buf[idx + 1] < 100 && buf[idx] < 100)
                                    found = true;
                            }
                        grid[cy, cx] = found;
                    }
            }
            finally { bmp.UnlockBits(data); }

            // Greedy rectangle merge: sweep marked cells into bounding rectangles
            var visited = new bool[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    if (!grid[r, c] || visited[r, c]) continue;
                    // Expand right
                    int c2 = c;
                    while (c2 + 1 < cols && grid[r, c2 + 1] && !visited[r, c2 + 1]) c2++;
                    // Expand down while full row of cells is marked
                    int r2 = r;
                    while (r2 + 1 < rows)
                    {
                        bool fullRow = true;
                        for (int cc = c; cc <= c2; cc++)
                            if (!grid[r2 + 1, cc] || visited[r2 + 1, cc]) { fullRow = false; break; }
                        if (!fullRow) break;
                        r2++;
                    }
                    // Mark visited
                    for (int rr = r; rr <= r2; rr++)
                        for (int cc = c; cc <= c2; cc++)
                            visited[rr, cc] = true;

                    double px = c * cellSize / (double)zoom;
                    double py = r * cellSize / (double)zoom;
                    double pw = (c2 - c + 1) * cellSize / (double)zoom;
                    double ph = (r2 - r + 1) * cellSize / (double)zoom;
                    regions.Add((px, py, pw, ph));
                }

            return regions;
        }
    }
}
