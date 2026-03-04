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
        private const float ZOOM = 1.0f;
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
        /// Compares two sets of PDFs treating each set as a single concatenated document.
        /// Pages from each list are rendered sequentially and matched by global page index.
        /// </summary>
        public static async Task<(List<DiffResultData> Results, string TempDir)> CompareAsync(
            IReadOnlyList<string> pathsA, IReadOnlyList<string> pathsB,
            IProgress<int>? progress = null,
            CancellationToken ct = default,
            int tolerance = DefaultTolerance)
        {
            return await Task.Run(() => CompareMulti(pathsA, pathsB, progress, ct, tolerance), ct);
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

            byte[] bytesA = File.ReadAllBytes(pathA);
            byte[] bytesB = File.ReadAllBytes(pathB);

            using var ctxA = new MuPDFContext();
            using var ctxB = new MuPDFContext();
            using var docA = new MuPDFDocument(ctxA, bytesA, InputFileTypes.PDF);
            using var docB = new MuPDFDocument(ctxB, bytesB, InputFileTypes.PDF);

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

        private static (List<DiffResultData> Results, string TempDir) CompareMulti(
            IReadOnlyList<string> pathsA, IReadOnlyList<string> pathsB,
            IProgress<int>? progress,
            CancellationToken ct,
            int tolerance)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "FinnDiff_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            var filesA = RenderSide(pathsA, "a", tempDir, ct);
            var filesB = RenderSide(pathsB, "b", tempDir, ct);
            progress?.Report(50);

            int maxPages = Math.Max(filesA.Count, filesB.Count);
            var results = new DiffResultData[maxPages];
            int diffedCount = 0;

            Parallel.For(0, maxPages, new ParallelOptions { CancellationToken = ct }, i =>
            {
                ct.ThrowIfCancellationRequested();

                string? fileA = i < filesA.Count ? filesA[i] : null;
                string? fileB = i < filesB.Count ? filesB[i] : null;
                string? fileD = null;
                bool hasDiff;

                if (fileA != null && fileB != null)
                {
                    using var bmpA = new SysBitmap(fileA);
                    using var bmpB = new SysBitmap(fileB);
                    fileD = Path.Combine(tempDir, $"d_{i}.png");
                    hasDiff = ComputeAndSaveDiff(bmpA, bmpB, fileD, tolerance);
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

        private static List<string> RenderSide(
            IReadOnlyList<string> paths, string prefix, string tempDir, CancellationToken ct)
        {
            var files = new List<string>();
            foreach (string path in paths)
            {
                ct.ThrowIfCancellationRequested();
                byte[] bytes = File.ReadAllBytes(path);
                using var ctx = new MuPDFContext();
                using var doc = new MuPDFDocument(ctx, bytes, InputFileTypes.PDF);
                for (int i = 0; i < doc.Pages.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    string file = Path.Combine(tempDir, $"{prefix}_{files.Count}.jpg");
                    doc.SaveImageAsJPEG(i, ZOOM, file, JPEG_QUALITY);
                    files.Add(file);
                }
            }
            return files;
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
            var padded = new SysBitmap(width, height, SysPixelFormat.Format32bppArgb);
            using var g = SysGraphics.FromImage(padded);
            g.Clear(SysColor.White);
            g.DrawImage(source, 0, 0, source.Width, source.Height);
            return padded;
        }
    }
}
