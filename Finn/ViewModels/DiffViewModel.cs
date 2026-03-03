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
        public UISettingsViewModel UI { get; set; } = new();

        private string fileNameA = string.Empty;
        private string fileNameB = string.Empty;
        private string filePathA = string.Empty;
        private string filePathB = string.Empty;

        private List<DiffResultData> pageInfos = [];
        private string? tempDir;

        private int currentPageIndex;
        private int viewMode; // 0 = SideBySide, 1 = Overlay, 2 = DiffOnly
        private double overlayOpacity = 0.5;
        private int tolerance = PdfDiffService.DefaultTolerance;
        private string summary = string.Empty;
        private bool isBusy;
        private int progress;

        private Bitmap? currentOriginal;
        private Bitmap? currentRevised;
        private Bitmap? currentDiff;

        private CancellationTokenSource? cts;

        private List<DiffResultData> diffPages = [];
        private DiffResultData? selectedDiffPage;
        private bool changePanelOpen = true;

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
                    selectedDiffPage = diffPages.FirstOrDefault(p => p.PageIndex == value);
                    OnPropertyChanged(nameof(SelectedDiffPage));
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

        public int Tolerance
        {
            get => tolerance;
            set => SetProperty(ref tolerance, value);
        }

        public IReadOnlyList<DiffResultData> DiffPages => diffPages;
        public int DiffPageCount => diffPages.Count;

        public bool ChangePanelOpen
        {
            get => changePanelOpen;
            set => SetProperty(ref changePanelOpen, value);
        }

        public DiffResultData? SelectedDiffPage
        {
            get => selectedDiffPage;
            set
            {
                if (selectedDiffPage == value) return;
                selectedDiffPage = value;
                OnPropertyChanged(nameof(SelectedDiffPage));
                if (value != null)
                    CurrentPageIndex = value.PageIndex;
            }
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
                    FilePathA, FilePathB, progressReporter, cts.Token, tolerance);

                pageInfos = results;
                tempDir = dir;

                int diffCount = results.Count(r => r.HasDifferences);
                Summary = diffCount == 0
                    ? $"{TotalPages} pages — identical"
                    : $"{TotalPages} pages — {diffCount} with differences";

                OnPropertyChanged(nameof(TotalPages));
                OnPropertyChanged(nameof(PageDisplay));

                diffPages = [.. pageInfos.Where(r => r.HasDifferences)];
                OnPropertyChanged(nameof(DiffPages));
                OnPropertyChanged(nameof(DiffPageCount));

                if (pageInfos.Count > 0)
                {
                    currentPageIndex = -1; // ensure setter detects a change when resetting to page 0
                    CurrentPageIndex = 0;
                }
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

        public async Task ApplyToleranceAsync()
        {
            if (pageInfos.Count == 0 || isBusy) return;

            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();

            IsBusy = true;
            try
            {
                CurrentOriginal = null;
                CurrentRevised = null;
                CurrentDiff = null;

                await PdfDiffService.RecomputeDiffsAsync(pageInfos, tolerance, cts.Token);

                int diffCount = pageInfos.Count(r => r.HasDifferences);
                Summary = diffCount == 0
                    ? $"{TotalPages} pages — identical"
                    : $"{TotalPages} pages — {diffCount} with differences";

                LoadPageImages();

                diffPages = [.. pageInfos.Where(r => r.HasDifferences)];
                OnPropertyChanged(nameof(DiffPages));
                OnPropertyChanged(nameof(DiffPageCount));
                selectedDiffPage = diffPages.FirstOrDefault(p => p.PageIndex == currentPageIndex);
                OnPropertyChanged(nameof(SelectedDiffPage));
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

            return new Bitmap(new MemoryStream(File.ReadAllBytes(path)));
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
