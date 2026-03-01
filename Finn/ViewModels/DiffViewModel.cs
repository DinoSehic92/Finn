using Avalonia.Media.Imaging;
using Finn.Model;
using Finn.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.ViewModels
{
    public class DiffViewModel : ViewModelBase
    {
        private string fileNameA = string.Empty;
        private string fileNameB = string.Empty;
        private string filePathA = string.Empty;
        private string filePathB = string.Empty;

        private List<DiffResultData> pageInfos = [];
        private string? tempDir;

        private int currentPageIndex;
        private int viewMode; // 0 = SideBySide, 1 = Overlay, 2 = DiffOnly
        private double overlayOpacity = 0.5;
        private string summary = string.Empty;
        private bool isBusy;
        private int progress;

        private Bitmap? currentOriginal;
        private Bitmap? currentRevised;
        private Bitmap? currentDiff;

        private CancellationTokenSource? cts;

        public string FileNameA
        {
            get => fileNameA;
            set => SetProperty(ref fileNameA, value);
        }

        public string FileNameB
        {
            get => fileNameB;
            set => SetProperty(ref fileNameB, value);
        }

        public string FilePathA
        {
            get => filePathA;
            set => SetProperty(ref filePathA, value);
        }

        public string FilePathB
        {
            get => filePathB;
            set => SetProperty(ref filePathB, value);
        }

        public int CurrentPageIndex
        {
            get => currentPageIndex;
            set
            {
                if (SetProperty(ref currentPageIndex, value))
                {
                    OnPropertyChanged(nameof(PageDisplay));
                    LoadPageImages();
                }
            }
        }

        public int TotalPages => pageInfos.Count;

        public string PageDisplay => TotalPages > 0
            ? $"{CurrentPageIndex + 1} / {TotalPages}"
            : "0 / 0";

        public int ViewMode
        {
            get => viewMode;
            set
            {
                if (SetProperty(ref viewMode, value))
                {
                    OnPropertyChanged(nameof(IsSideBySide));
                    OnPropertyChanged(nameof(IsOverlay));
                    OnPropertyChanged(nameof(IsDiffOnly));
                }
            }
        }

        public bool IsSideBySide => viewMode == 0;
        public bool IsOverlay => viewMode == 1;
        public bool IsDiffOnly => viewMode == 2;

        public double OverlayOpacity
        {
            get => overlayOpacity;
            set => SetProperty(ref overlayOpacity, value);
        }

        public string Summary
        {
            get => summary;
            set => SetProperty(ref summary, value);
        }

        public bool IsBusy
        {
            get => isBusy;
            set => SetProperty(ref isBusy, value);
        }

        public int Progress
        {
            get => progress;
            set => SetProperty(ref progress, value);
        }

        public Bitmap? CurrentOriginal
        {
            get => currentOriginal;
            set
            {
                var old = currentOriginal;
                if (SetProperty(ref currentOriginal, value))
                    old?.Dispose();
            }
        }

        public Bitmap? CurrentRevised
        {
            get => currentRevised;
            set
            {
                var old = currentRevised;
                if (SetProperty(ref currentRevised, value))
                    old?.Dispose();
            }
        }

        public Bitmap? CurrentDiff
        {
            get => currentDiff;
            set
            {
                var old = currentDiff;
                if (SetProperty(ref currentDiff, value))
                    old?.Dispose();
            }
        }

        public async Task RunDiffAsync()
        {
            if (string.IsNullOrEmpty(FilePathA) || string.IsNullOrEmpty(FilePathB))
                return;

            IsBusy = true;
            Progress = 0;
            Summary = "Comparing...";

            try
            {
                cts = new CancellationTokenSource();
                var progressReporter = new Progress<int>(p => Progress = p);

                var (results, dir) = await PdfDiffService.CompareAsync(
                    FilePathA, FilePathB, progressReporter, cts.Token);

                pageInfos = results;
                tempDir = dir;

                int diffCount = results.Count(r => r.HasDifferences);
                Summary = diffCount == 0
                    ? $"{TotalPages} pages — identical"
                    : $"{TotalPages} pages — {diffCount} with differences";

                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageDisplay));

                if (pageInfos.Count > 0)
                    CurrentPageIndex = 0;
                else
                    LoadPageImages();
            }
            catch (OperationCanceledException)
            {
                Summary = "Cancelled";
            }
            catch (Exception ex)
            {
                Summary = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void NextPage()
        {
            if (CurrentPageIndex < TotalPages - 1)
                CurrentPageIndex++;
        }

        public void PreviousPage()
        {
            if (CurrentPageIndex > 0)
                CurrentPageIndex--;
        }

        private void LoadPageImages()
        {
            if (pageInfos.Count == 0 || currentPageIndex < 0 || currentPageIndex >= pageInfos.Count)
            {
                CurrentOriginal = null;
                CurrentRevised = null;
                CurrentDiff = null;
                return;
            }

            var info = pageInfos[currentPageIndex];

            CurrentOriginal = LoadBitmapFromFile(info.OriginalPath);
            CurrentRevised = LoadBitmapFromFile(info.RevisedPath);
            CurrentDiff = LoadBitmapFromFile(info.DiffPath);
        }

        private static Bitmap? LoadBitmapFromFile(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            using var stream = File.OpenRead(path);
            return new Bitmap(stream);
        }

        public void Cleanup()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = null;

            CurrentOriginal = null;
            CurrentRevised = null;
            CurrentDiff = null;

            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); }
                catch { /* best effort */ }
            }

            tempDir = null;
            pageInfos.Clear();
        }
    }
}
