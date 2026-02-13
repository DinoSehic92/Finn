using System.ComponentModel;
using System.Threading.Tasks;
using System.Diagnostics;
using MuPDFCore;
using System.Threading;
using System.IO;
using Finn.Model;
using System;
using Avalonia.Threading;
using MuPDFCore.MuPDFRenderer;
using Avalonia.Media;
using System.Text.RegularExpressions;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using MuPDFCore.StructuredText;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Finn.ViewModels
{
    public class PreviewViewModel : ViewModelBase, IAsyncDisposable
    {
        #region Constants
        private const int MAX_RECENT_FILES = 20;
        private const double ZOOM_LEVEL = 0.2;
        private const int BUFFER_SIZE = 64 * 1024; // 64 KB — larger buffer = fewer syscalls
        private const int PROGRESS_UPDATE_INTERVAL = 20;
        private const int RENDER_DELAY = 20;
        private const int POLLING_DELAY = 25;
        private const int DISPOSE_POLLING_DELAY = 50;
        private const int MAX_RETRY_ATTEMPTS = 3;
        #endregion

        #region Fields
        private readonly SemaphoreSlim fileOperationSemaphore = new(1, 1);
        private readonly SemaphoreSlim renderSemaphore = new(1, 1);
        private bool disposed = false;
        private int fileGeneration = 0;
        #endregion

        #region Constructor
        public PreviewViewModel(ILogger<PreviewViewModel>? logger = null) : base(logger)
        {
            recentFiles = new AvaloniaList<FileData>();
            searchPages = new AvaloniaList<int>();
            searchPagesText = new AvaloniaList<string>();

            mainCts = new CancellationTokenSource();
            searchCts = new CancellationTokenSource();

            statusMessage = "Ready!";
            linkedPageMode = true;
        }
        #endregion

        #region PDF Document Properties
        private MuPDFDocument? mainPreviewFile = null;
        public MuPDFDocument? MainPreviewFile
        {
            get => mainPreviewFile;
            set => SetProperty(ref mainPreviewFile, value);
        }

        private MuPDFContext? context = null;
        private byte[]? bytes;
        private bool fileAvailable = false;
        #endregion

        #region File Properties
        private FileData? currentFile = null;
        public FileData? CurrentFile
        {
            get => currentFile;
            set => SetProperty(ref currentFile, value);
        }

        private FileData? requestFile = null;
        public FileData? RequestFile
        {
            get => requestFile;
            set => SetProperty(ref requestFile, value);
        }

        private AvaloniaList<FileData> recentFiles;
        public AvaloniaList<FileData> RecentFiles
        {
            get => recentFiles;
            set => SetProperty(ref recentFiles, value);
        }
        #endregion

        #region Page Management Properties
        private int requestPage1 = 0;
        public int RequestPage1
        {
            get => requestPage1;
            set
            {
                SetProperty(ref requestPage1, value);

                if (PageInRange(requestPage1))
                    _ = SetMainPageAsync();

                if (LinkedPageMode)
                {
                    SetProperty(ref requestPage2, requestPage1 + 1);
                    if (PageInRange(requestPage2))
                        _ = SetSecondaryPageAsync();
                }
            }
        }

        private int requestPage2 = 0;
        public int RequestPage2
        {
            get => requestPage2;
            set
            {
                SetProperty(ref requestPage2, value);
                if (PageInRange(requestPage2))
                    _ = SetSecondaryPageAsync();
            }
        }

        private int currentPage1 = 0;
        public int CurrentPage1
        {
            get => currentPage1;
            set
            {
                if (SetProperty(ref currentPage1, value) && CurrentFile != null)
                    CurrentFile.DefaultPage = value;
            }
        }

        private int currentPage2 = 0;
        public int CurrentPage2
        {
            get => currentPage2;
            set => SetProperty(ref currentPage2, value);
        }

        private int pagecount = 0;
        public int Pagecount
        {
            get => pagecount;
            set => SetProperty(ref pagecount, value);
        }
        #endregion

        #region View Mode Properties
        private bool twopageMode = false;
        public bool TwopageMode
        {
            get => twopageMode;
            set => SetProperty(ref twopageMode, value, () => _ = ToggleDualViewAsync());
        }

        private bool linkedPageMode = true;
        public bool LinkedPageMode
        {
            get => linkedPageMode;
            set => SetProperty(ref linkedPageMode, value, ToggleLinkedMode);
        }

        private bool dimmedBackground = false;
        public bool DimmedBackground
        {
            get => dimmedBackground;
            set
            {
                if (fileAvailable && SetProperty(ref dimmedBackground, value))
                    SetDimmedMode();
            }
        }

        private double rotation = 0;
        public double Rotation
        {
            get => rotation;
            set => SetProperty(ref rotation, value);
        }

        private bool darkMode = false;
        public bool DarkMode
        {
            get => darkMode;
            set
            {
                if (SetProperty(ref darkMode, value))
                {
                    OnPropertyChanged(nameof(PreviewBackground));
                    _ = SetMainPageAsync();
                }
            }
        }

        public void ToggleDarkMode() => DarkMode = !DarkMode;

        private Color themeRegionColor = Colors.White;
        /// <summary>
        /// The current theme region/background color, set externally by MainViewModel
        /// when UI colors change. Used by <see cref="PreviewBackground"/> to compute
        /// the correct inverse for the Difference blend.
        /// </summary>
        public Color ThemeRegionColor
        {
            get => themeRegionColor;
            set
            {
                if (SetProperty(ref themeRegionColor, value))
                {
                    OnPropertyChanged(nameof(PreviewBackground));
                    OnPropertyChanged(nameof(UiBackground));
                }
            }
        }

        public IBrush UiBackground => new SolidColorBrush(ThemeRegionColor);

        /// <summary>
        /// Background for the preview area. When DarkMode is on, this returns the
        /// RGB-inverse of the actual theme background so that after the
        /// InvertColorControl's Difference blend the visible result matches
        /// the original theme color.
        /// </summary>
        public IBrush PreviewBackground
        {
            get
            {
                if (!DarkMode)
                    return Brushes.Transparent;

                // Compute the RGB inverse: the Difference blend does |dst - src|
                // with white (1,1,1), i.e. 1 - dst.  If we set dst = inverse(bg),
                // the result is 1 - (1 - bg) = bg — the original theme color.
                var inverted = Color.FromRgb(
                    (byte)(255 - ThemeRegionColor.R),
                    (byte)(255 - ThemeRegionColor.G),
                    (byte)(255 - ThemeRegionColor.B));

                return new SolidColorBrush(inverted);
            }
        }
        #endregion

        #region Renderer Properties
        private PDFRenderer? mainRenderer;
        private PDFRenderer? secondaryRenderer;
        #endregion

        #region Search Properties
        private Regex? regex = null;

        private AvaloniaList<int> searchPages;
        public AvaloniaList<int> SearchPages
        {
            get => searchPages;
            set => SetProperty(ref searchPages, value);
        }

        private AvaloniaList<string> searchPagesText;
        public AvaloniaList<string> SearchPagesText
        {
            get => searchPagesText;
            set => SetProperty(ref searchPagesText, value);
        }

        private int searchPageIndex = 0;
        public int SearchPageIndex
        {
            get => searchPageIndex;
            set => SetProperty(ref searchPageIndex, value, SetSearchPage);
        }

        private int searchItems = 0;
        public int SearchItems
        {
            get => searchItems;
            set => SetProperty(ref searchItems, value);
        }

        private bool searchMode = false;
        public bool SearchMode
        {
            get => searchMode;
            set => SetProperty(ref searchMode, value);
        }

        private bool searchBusy = false;
        public bool SearchBusy
        {
            get => searchBusy;
            set => SetProperty(ref searchBusy, value);
        }
        #endregion

        #region Status Properties
        private bool fileWorkerBusy = false;
        public bool FileWorkerBusy
        {
            get => fileWorkerBusy;
            set => SetProperty(ref fileWorkerBusy, value);
        }

        private bool renderWorkerBusy = false;

        private string statusMessage;
        public string StatusMessage
        {
            get => statusMessage;
            set => SetProperty(ref statusMessage, value);
        }

        private int progress = 0;
        public int Progress
        {
            get => progress;
            set => SetProperty(ref progress, value);
        }
        #endregion

        #region Cancellation Tokens
        private CancellationTokenSource mainCts;
        private CancellationTokenSource searchCts;
        #endregion

        #region Renderer Management
        public void GetRenderControl(PDFRenderer mainRenderer, PDFRenderer secondaryRenderer)
        {
            try
            {
                var background = new SolidColorBrush(Colors.White);

                if (this.mainRenderer != null)
                    this.mainRenderer.ReleaseResources();

                this.mainRenderer = mainRenderer;
                this.secondaryRenderer = secondaryRenderer;

                this.mainRenderer.PageBackground = background;
                this.secondaryRenderer.PageBackground = background;

                if (CurrentFile != null)
                    _ = SetMainPageAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error getting render control");
            }
        }

        public void ClearRenderer()
        {
            try
            {
                mainRenderer?.ReleaseResources();
                secondaryRenderer?.ReleaseResources();
                mainRenderer = null;
                secondaryRenderer = null;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error clearing renderer");
            }
        }

        public void SetupPage(int page = 0)
        {
            RequestPage1 = Math.Max(0, page);
        }
        #endregion

        #region File Operations
        public async Task SetFileAsync(string? search = null, CancellationToken cancellationToken = default)
        {
            if (disposed || RequestFile?.Sökväg == null)
                return;

            int myGeneration = Interlocked.Increment(ref fileGeneration);

            // Cancel previous load
            try
            {
                await mainCts.CancelAsync().ConfigureAwait(false);
                mainCts.Dispose();
            }
            catch { }

            mainCts = new CancellationTokenSource();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, mainCts.Token);
            var token = linkedCts.Token;

            StatusMessage = "Setting File";
            fileAvailable = false;
            FileWorkerBusy = true;

            try
            {
                // Dispose old document quickly on UI thread (no polling)
                await DisposeCurrentDocumentAsync(token).ConfigureAwait(false);

                if (IsStale(myGeneration)) return;

                // Read bytes on background thread
                string path = RequestFile.Sökväg;
                bytes = await Task.Run(() => ReadFileBytes(path, token)).ConfigureAwait(false);

                if (IsStale(myGeneration) || bytes == null) return;

                var localBytes = bytes;

                // Create PDF document on background thread
                MuPDFContext localContext = null!;
                MuPDFDocument doc = null!;
                await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return;
                    localContext = new MuPDFContext();
                    doc = new MuPDFDocument(localContext, localBytes, InputFileTypes.PDF);
                }).ConfigureAwait(false);

                if (IsStale(myGeneration))
                {
                    // Another call superseded us — dispose what we just created
                    doc?.Dispose();
                    localContext?.Dispose();
                    return;
                }

                // Swap fields atomically: capture old refs first
                var oldDoc = MainPreviewFile;
                var oldCtx = context;

                MainPreviewFile = doc;
                context = localContext;
                Pagecount = doc.Pages.Count;
                CurrentFile = RequestFile;

                // Dispose old refs in correct order (document before context)
                oldDoc?.Dispose();
                oldCtx?.Dispose();

                fileAvailable = true;

                // Render on UI thread
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (IsStale(myGeneration)) return;

                    LinkedPageMode = true;
                    SetDefaultPage();

                    if (!string.IsNullOrEmpty(search))
                    {
                        SearchMode = true;
                        _ = SearchAsync(search, token);
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                logger?.LogInformation("File load cancelled (gen {Generation})", myGeneration);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in SetFileAsync");
            }
            finally
            {
                if (!IsStale(myGeneration))
                    FileWorkerBusy = false;
            }
        }

        /// <summary>
        /// Returns true if a newer SetFileAsync call has superseded this one.
        /// </summary>
        private bool IsStale(int myGeneration)
            => Volatile.Read(ref fileGeneration) != myGeneration;

        /// <summary>
        /// Quickly disposes the current document. Cancels search, releases
        /// renderer resources, and disposes MuPDF objects — all on the UI
        /// thread in a single dispatch (no polling loops).
        /// </summary>
        private async Task DisposeCurrentDocumentAsync(CancellationToken token)
        {
            // Cancel any running search without polling
            try
            {
                await searchCts.CancelAsync().ConfigureAwait(false);
                searchCts.Dispose();
            }
            catch { }
            searchCts = new CancellationTokenSource();
            SearchBusy = false;
            ClearSearch();

            if (MainPreviewFile == null) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (mainRenderer?.HighlightedRegions != null)
                        mainRenderer.HighlightedRegions = null;
                    if (secondaryRenderer?.HighlightedRegions != null)
                        secondaryRenderer.HighlightedRegions = null;

                    mainRenderer?.ReleaseResources();
                    secondaryRenderer?.ReleaseResources();
                    MainPreviewFile?.Dispose();
                    context?.Dispose();
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Error during quick dispose");
                }
            }).GetTask().ConfigureAwait(false);

            fileAvailable = false;
            MainPreviewFile = null;
            context = null;
            bytes = null; // release memory early
        }

        /// <summary>
        /// Reads file bytes synchronously (called via Task.Run) with
        /// cancellation support and progress reporting.
        /// Returns null if cancelled or on error — caller checks for null.
        /// </summary>
        private byte[]? ReadFileBytes(string path, CancellationToken token)
        {
            try
            {
                if (token.IsCancellationRequested) return null;

                var fileInfo = new FileInfo(path);
                if (!fileInfo.Exists) return null;

                long total = fileInfo.Length;
                StatusMessage = $"Reading: {total / 1_000_000.0:F1} MB";
                Progress = 0;

                // Small files (< 10 MB): read all at once — fastest path
                if (total < 10_000_000)
                {
                    if (token.IsCancellationRequested) return null;
                    byte[] result = File.ReadAllBytes(path);
                    Progress = 0;
                    return token.IsCancellationRequested ? null : result;
                }

                // Large files: buffered read with progress
                using var source = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.Read, BUFFER_SIZE, FileOptions.SequentialScan);
                using var ms = new MemoryStream((int)Math.Min(total, int.MaxValue));

                byte[] buffer = new byte[BUFFER_SIZE];
                int steps = Math.Max(1, (int)(total / buffer.Length));
                int leap = Math.Max(1, steps / PROGRESS_UPDATE_INTERVAL);
                int i = 0;
                int bytesRead;

                while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (token.IsCancellationRequested) return null;

                    ms.Write(buffer, 0, bytesRead);

                    if (i % leap == 0)
                        Progress = Math.Min(100, 100 * (i + 1) / steps);
                    i++;
                }

                Progress = 0;
                return token.IsCancellationRequested ? null : ms.ToArray();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger?.LogError(ex, "Error reading {Path}", path);
                return null;
            }
        }

        [Obsolete("Use SetFileAsync instead")]
        public async void SetFile(string? search = null)
        {
            try { await SetFileAsync(search).ConfigureAwait(false); }
            catch (Exception ex) { logger?.LogError(ex, "Error in legacy SetFile"); }
        }
        #endregion

        #region Recent Files Management
        public void AddRecentFile(FileData? file)
        {
            if (file?.Uppdrag == string.Empty || file == null) return;

            try
            {
                if (RecentFiles.Contains(file))
                    RecentFiles.Remove(file);

                RecentFiles.Insert(0, file);

                while (RecentFiles.Count > MAX_RECENT_FILES)
                    RecentFiles.RemoveAt(RecentFiles.Count - 1);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error adding recent file");
            }
        }
        #endregion

        #region Page Navigation & Default Page
        private void SetDefaultPage()
        {
            if (RequestFile == null || MainPreviewFile == null) return;

            try
            {
                int desired = Math.Clamp(RequestFile.DefaultPage, 0,
                    Math.Max(0, MainPreviewFile.Pages.Count - 1));

                requestPage1 = desired;
                OnPropertyChanged(nameof(RequestPage1));
                CurrentPage1 = desired;
                Rotation = 0;

                _ = RenderCurrentPageAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error setting default page");
            }
        }

        private async Task RenderCurrentPageAsync()
        {
            if (disposed || !PageInRange(requestPage1) || mainRenderer == null || MainPreviewFile == null)
                return;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    mainRenderer.IsVisible = false;
                    mainRenderer.HighlightedRegions = null;
                    mainRenderer.Initialize(MainPreviewFile, 1, requestPage1, ZOOM_LEVEL);
                    mainRenderer.IsVisible = true;
                    SetSearchResults();
                    CurrentPage1 = requestPage1;

                    if (LinkedPageMode && TwopageMode && PageInRange(requestPage1 + 1) && secondaryRenderer != null)
                    {
                        requestPage2 = requestPage1 + 1;
                        OnPropertyChanged(nameof(RequestPage2));
                        secondaryRenderer.IsVisible = false;
                        secondaryRenderer.HighlightedRegions = null;
                        secondaryRenderer.Initialize(MainPreviewFile, 1, requestPage2, ZOOM_LEVEL);
                        secondaryRenderer.IsVisible = true;
                        SetSecondarySearchResults();
                        CurrentPage2 = requestPage2;
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in RenderCurrentPageAsync");
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

        public void NextPage(bool secondPage = false)
        {
            int lastPage = Pagecount - 1;

            if (!TwopageMode) { if (RequestPage1 < lastPage) RequestPage1++; }
            else if (LinkedPageMode) { if (RequestPage1 + 2 <= lastPage) RequestPage1 += 2; }
            else if (!secondPage) { if (RequestPage1 < lastPage) RequestPage1++; }
            else { if (RequestPage2 < lastPage) RequestPage2++; }
        }

        public void PrevPage(bool secondPage = false)
        {
            if (!TwopageMode) { if (RequestPage1 > 0) RequestPage1--; }
            else if (LinkedPageMode) { if (RequestPage1 >= 2) RequestPage1 -= 2; }
            else if (!secondPage) { if (RequestPage1 > 0) RequestPage1--; }
            else { if (RequestPage2 > 0) RequestPage2--; }
        }

        public bool PageInRange(int pageNr) => pageNr >= 0 && pageNr < Pagecount;
        #endregion

        #region View Mode Methods
        public async Task ToggleDualViewAsync()
        {
            try
            {
                await Task.Delay(RENDER_DELAY).ConfigureAwait(false);

                if (TwopageMode && RequestPage1 % 2 != 0)
                    requestPage1 = RequestPage1 - 1;

                requestPage2 = requestPage1 + 1;
                await SetMainPageAsync().ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    mainRenderer?.Contain();
                    if (TwopageMode)
                    {
                        _ = SetSecondaryPageAsync();
                        secondaryRenderer?.Contain();
                    }
                    if (!LinkedPageMode)
                        LinkedPageMode = true;
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error toggling dual view");
            }
        }

        public void ToggleDimmed() => DimmedBackground = !DimmedBackground;

        public void SetDimmedMode()
        {
            var bg = DimmedBackground
                ? new SolidColorBrush(Colors.AntiqueWhite)
                : new SolidColorBrush(Colors.White);

            if (mainRenderer != null) mainRenderer.PageBackground = bg;
            if (secondaryRenderer != null) secondaryRenderer.PageBackground = bg;

            _ = SetMainPageAsync();
            if (TwopageMode) _ = SetSecondaryPageAsync();
        }

        public void ToggleLinkedMode()
        {
            if (LinkedPageMode)
            {
                if (CurrentPage1 % 2 != 0)
                    RequestPage1 = CurrentPage1 - 1;
                RequestPage2 = CurrentPage1 + 1;
            }
        }

        public void ToggleVisibility(bool isVisible)
        {
            if (mainRenderer != null) mainRenderer.IsVisible = isVisible;
            if (TwopageMode && secondaryRenderer != null) secondaryRenderer.IsVisible = isVisible;
        }

        public async void ToggleDualView() => await ToggleDualViewAsync().ConfigureAwait(false);
        #endregion

        #region Page Rendering
        private async Task SetMainPageAsync()
        {
            if (disposed || FileWorkerBusy || SearchBusy || !PageInRange(RequestPage1) || mainRenderer == null)
                return;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    mainRenderer.IsVisible = false;
                    mainRenderer.HighlightedRegions = null;
                    if (MainPreviewFile != null)
                    {
                        mainRenderer.Initialize(MainPreviewFile, 1, RequestPage1, ZOOM_LEVEL);
                        mainRenderer.IsVisible = true;
                        SetSearchResults();
                        CurrentPage1 = RequestPage1;
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error setting main page");
            }
            finally
            {
                renderSemaphore.Release();
            }
        }

        private async Task SetSecondaryPageAsync()
        {
            if (disposed || FileWorkerBusy || SearchBusy || !PageInRange(RequestPage2) ||
                !TwopageMode || secondaryRenderer == null)
                return;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    secondaryRenderer.IsVisible = false;
                    secondaryRenderer.HighlightedRegions = null;
                    if (MainPreviewFile != null)
                    {
                        secondaryRenderer.Initialize(MainPreviewFile, 1, RequestPage2, ZOOM_LEVEL);
                        secondaryRenderer.IsVisible = true;
                        SetSecondarySearchResults();
                        CurrentPage2 = RequestPage2;
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error setting secondary page");
            }
            finally
            {
                renderSemaphore.Release();
            }
        }
        #endregion

        #region Search Methods
        public async Task SearchAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!fileAvailable || string.IsNullOrWhiteSpace(text) || disposed || MainPreviewFile == null)
                return;

            ClearSearch();
            regex = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            try
            {
                await searchCts.CancelAsync().ConfigureAwait(false);
                searchCts.Dispose();
            }
            catch { }
            searchCts = new CancellationTokenSource();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, searchCts.Token);

            await SearchDocumentAsync(linkedCts.Token).ConfigureAwait(false);
        }

        private async Task SearchDocumentAsync(CancellationToken cancellationToken)
        {
            if (MainPreviewFile == null || disposed) return;

            try
            {
                SearchBusy = true;

                var foundPages = new List<(int pageIndex, int matchCount)>();

                await Task.Run(() =>
                {
                    for (int i = 0; i < Pagecount; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            using var disposable = MainPreviewFile.GetStructuredTextPage(i);
                            var structuredPage = (MuPDFStructuredTextPage)disposable;
                            int matchCount = structuredPage.Search(regex!).Count();
                            if (matchCount > 0)
                                foundPages.Add((i, matchCount));
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "Error searching page {Page}", i);
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SearchPagesText.Clear();
                    foreach (var (pageIndex, matchCount) in foundPages)
                    {
                        SearchPages.Add(pageIndex);
                        SearchPagesText.Add($"Page: {pageIndex + 1} - {matchCount} items");
                    }
                    SearchItems = foundPages.Count;
                    if (SearchItems > 0)
                    {
                        SearchPageIndex = 0;
                        RequestPage1 = SearchPages[SearchPageIndex];
                        mainRenderer?.Search(regex!);
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during document search");
            }
            finally
            {
                SearchBusy = false;
            }
        }

        public void NextSearchPage()
        {
            if (regex != null && SearchPages != null && SearchPageIndex < SearchPages.Count - 1)
                SearchPageIndex++;
        }

        public void PrevSearchPage()
        {
            if (regex != null && SearchPages != null && SearchPageIndex > 0)
                SearchPageIndex--;
        }

        private void SetSearchPage()
        {
            if (SearchMode && SearchItems != 0 && SearchPages.Count > SearchPageIndex)
                RequestPage1 = SearchPages[SearchPageIndex];
        }

        private void SetSearchResults()
        {
            if (SearchPages?.Contains(RequestPage1) == true && regex != null)
            {
                mainRenderer?.Search(regex);
                searchPageIndex = SearchPages.IndexOf(RequestPage1);
                OnPropertyChanged(nameof(SearchPageIndex));
            }
        }

        private void SetSecondarySearchResults()
        {
            if (SearchPages?.Contains(RequestPage2) == true && regex != null)
            {
                secondaryRenderer?.Search(regex);
                searchPageIndex = SearchPages.IndexOf(RequestPage2);
                OnPropertyChanged(nameof(SearchPageIndex));
            }
        }

        public async Task StopSearchAsync()
        {
            if (!SearchBusy) return;
            try
            {
                await searchCts.CancelAsync().ConfigureAwait(false);
                while (SearchBusy)
                    await Task.Delay(POLLING_DELAY).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error stopping search");
            }
        }

        public void ClearSearch()
        {
            SearchItems = 0;
            SearchPageIndex = 0;
            SearchPagesText.Clear();
            SearchPages.Clear();
        }

        [Obsolete("Use SearchAsync instead")]
        public void Search(string text)
        {
            _ = Task.Run(async () =>
            {
                try { await SearchAsync(text).ConfigureAwait(false); }
                catch (Exception ex) { logger?.LogError(ex, "Error in legacy Search"); }
            });
        }

        public async Task StopSearch() => await StopSearchAsync().ConfigureAwait(false);
        #endregion

        #region Clipboard Operations
        public void CopyToClipboard(Avalonia.Visual window)
        {
            if (CurrentFile != null && mainRenderer != null)
            {
                string selectedText = mainRenderer.GetSelectedText();
                if (!string.IsNullOrEmpty(selectedText))
                    TopLevel.GetTopLevel(window)?.Clipboard?.SetTextAsync(selectedText);
            }
        }
        #endregion

        #region Cleanup and Disposal
        protected override async ValueTask DisposeAsyncCore()
        {
            if (disposed) return;
            try
            {
                await DisposeCurrentDocumentAsync(CancellationToken.None).ConfigureAwait(false);

                await mainCts.CancelAsync().ConfigureAwait(false);
                await searchCts.CancelAsync().ConfigureAwait(false);
                mainCts.Dispose();
                searchCts.Dispose();
                fileOperationSemaphore.Dispose();
                renderSemaphore.Dispose();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during async dispose");
            }
            finally
            {
                disposed = true;
                await base.DisposeAsyncCore().ConfigureAwait(false);
            }
        }

        public async Task SafeDisposeAsync()
        {
            if (disposed) return;
            await DisposeCurrentDocumentAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public async Task CloseRendererAsync()
        {
            await StopSearchAsync().ConfigureAwait(false);
            ClearSearch();
            await SafeDisposeAsync().ConfigureAwait(false);
        }

        public async Task SafeDispose() => await SafeDisposeAsync().ConfigureAwait(false);
        public async Task CloseRenderer() => await CloseRendererAsync().ConfigureAwait(false);
        #endregion

        /// <summary>
        /// Updates the theme region color and forces re-evaluation of
        /// <see cref="PreviewBackground"/>.
        /// </summary>
        public void UpdateThemeRegionColor(Color color)
            => ThemeRegionColor = color;
    }
}