using Avalonia.Media.Imaging;
using Finn.Model;
using Finn.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
        private IReadOnlyList<string>? multiPathsA;
        private IReadOnlyList<string>? multiPathsB;

        private List<DiffResultData> pageInfos = [];
        private string? tempDir;

        private int currentPageIndex;
        private int viewMode = 2; // 0 = SideBySide, 1 = Overlay, 2 = DiffOnly
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

        // Annotation state
        private AnnotationTool activeTool = AnnotationTool.None;
        private string annotationColor = "#D64045";
        private double annotationStrokeWidth = 3;
        private readonly Dictionary<int, List<DiffAnnotation>> _pageAnnotations = [];

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

        public IReadOnlyList<string>? MultiPathsA
        {
            get => multiPathsA;
            set => SetProperty(ref multiPathsA, value);
        }

        public IReadOnlyList<string>? MultiPathsB
        {
            get => multiPathsB;
            set => SetProperty(ref multiPathsB, value);
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
            bool isMulti = multiPathsA != null && multiPathsB != null;
            if (!isMulti && (string.IsNullOrEmpty(FilePathA) || string.IsNullOrEmpty(FilePathB)))
                return;

            IsBusy = true;
            Progress = 0;
            Summary = "Comparing...";

            try
            {
                cts = new CancellationTokenSource();
                var progressReporter = new Progress<int>(p => Progress = p);

                var (results, dir) = isMulti
                    ? await PdfDiffService.CompareAsync(multiPathsA!, multiPathsB!, progressReporter, cts.Token, tolerance)
                    : await PdfDiffService.CompareAsync(FilePathA, FilePathB, progressReporter, cts.Token, tolerance);

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

        #region Annotations

        public AnnotationTool ActiveTool
        {
            get => activeTool;
            set
            {
                if (SetProperty(ref activeTool, value))
                {
                    OnPropertyChanged(nameof(IsAnnotating));
                    OnPropertyChanged(nameof(IsDrawTool));
                    OnPropertyChanged(nameof(IsHighlightTool));
                }
            }
        }

        public bool IsAnnotating => activeTool != AnnotationTool.None;
        public bool IsDrawTool => activeTool == AnnotationTool.Draw;
        public bool IsHighlightTool => activeTool == AnnotationTool.Highlight;

        public string AnnotationColor
        {
            get => annotationColor;
            set => SetProperty(ref annotationColor, value);
        }

        public double AnnotationStrokeWidth
        {
            get => annotationStrokeWidth;
            set => SetProperty(ref annotationStrokeWidth, value);
        }

        /// <summary>
        /// Returns annotations for the current page.
        /// </summary>
        public List<DiffAnnotation> CurrentAnnotations =>
            _pageAnnotations.TryGetValue(currentPageIndex, out var list) ? list : [];

        /// <summary>
        /// Adds a completed annotation to the current page.
        /// </summary>
        public void AddAnnotation(DiffAnnotation annotation)
        {
            if (!_pageAnnotations.TryGetValue(currentPageIndex, out var list))
            {
                list = [];
                _pageAnnotations[currentPageIndex] = list;
            }
            list.Add(annotation);
            OnPropertyChanged(nameof(CurrentAnnotations));
        }

        /// <summary>
        /// Removes the last annotation on the current page (undo).
        /// </summary>
        public void UndoAnnotation()
        {
            if (_pageAnnotations.TryGetValue(currentPageIndex, out var list) && list.Count > 0)
            {
                list.RemoveAt(list.Count - 1);
                OnPropertyChanged(nameof(CurrentAnnotations));
            }
        }

        /// <summary>
        /// Clears all annotations on the current page.
        /// </summary>
        public void ClearAnnotations()
        {
            if (_pageAnnotations.TryGetValue(currentPageIndex, out var list))
            {
                list.Clear();
                OnPropertyChanged(nameof(CurrentAnnotations));
            }
        }

        #endregion

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
            _pageAnnotations.Clear();
        }
    }
}
