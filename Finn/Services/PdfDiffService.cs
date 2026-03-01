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
        private const int DIFF_THRESHOLD = 30;
        private const float ZOOM = 1.0f;
        private const int JPEG_QUALITY = 90;

        /// <summary>
        /// Compare two PDF files asynchronously, returning per-page diff results
        /// and the path to the temporary directory holding rendered images.
        /// </summary>
        public static async Task<(List<DiffResultData> Results, string TempDir)> CompareAsync(
            string pathA, string pathB,
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            return await Task.Run(() => Compare(pathA, pathB, progress, ct), ct);
        }

        private static (List<DiffResultData> Results, string TempDir) Compare(
            string pathA, string pathB,
            IProgress<int>? progress,
            CancellationToken ct)
        {
            var results = new List<DiffResultData>();
            string tempDir = Path.Combine(Path.GetTempPath(), "FinnDiff_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempDir);

            byte[] bytesA = File.ReadAllBytes(pathA);
            byte[] bytesB = File.ReadAllBytes(pathB);

            using var ctxA = new MuPDFContext(1);
            using var ctxB = new MuPDFContext(1);
            using var docA = new MuPDFDocument(ctxA, bytesA, InputFileTypes.PDF);
            using var docB = new MuPDFDocument(ctxB, bytesB, InputFileTypes.PDF);

            int maxPages = Math.Max(docA.Pages.Count, docB.Pages.Count);

            for (int i = 0; i < maxPages; i++)
            {
                ct.ThrowIfCancellationRequested();

                bool hasA = i < docA.Pages.Count;
                bool hasB = i < docB.Pages.Count;

                string? fileA = null;
                string? fileB = null;
                string? fileD = null;
                bool hasDiff;

                if (hasA)
                {
                    fileA = Path.Combine(tempDir, $"a_{i}.jpg");
                    docA.SaveImageAsJPEG(i, ZOOM, fileA, JPEG_QUALITY);
                }

                if (hasB)
                {
                    fileB = Path.Combine(tempDir, $"b_{i}.jpg");
                    docB.SaveImageAsJPEG(i, ZOOM, fileB, JPEG_QUALITY);
                }

                if (hasA && hasB)
                {
                    using var bmpA = new SysBitmap(fileA!);
                    using var bmpB = new SysBitmap(fileB!);
                    fileD = Path.Combine(tempDir, $"d_{i}.png");
                    hasDiff = ComputeAndSaveDiff(bmpA, bmpB, fileD);
                }
                else
                {
                    hasDiff = true;
                }

                results.Add(new DiffResultData
                {
                    PageIndex = i,
                    OriginalPath = fileA,
                    RevisedPath = fileB,
                    DiffPath = fileD,
                    HasDifferences = hasDiff
                });

                progress?.Report((i + 1) * 100 / Math.Max(1, maxPages));
            }

            return (results, tempDir);
        }

        private static bool ComputeAndSaveDiff(SysBitmap a, SysBitmap b, string outputPath)
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

                        if (dr + dg + db > DIFF_THRESHOLD)
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
