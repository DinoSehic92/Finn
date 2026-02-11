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
        private const int BUFFER_SIZE = 2 * 8192;
        private const int PROGRESS_UPDATE_INTERVAL = 20;
        private const int RENDER_DELAY = 20;
        private const int POLLING_DELAY = 25;
        private const int DISPOSE_POLLING_DELAY = 100;
        private const int MAX_RETRY_ATTEMPTS = 3;
        #endregion

        #region Fields
        private readonly SemaphoreSlim fileOperationSemaphore = new(1, 1);
        private readonly SemaphoreSlim renderSemaphore = new(1, 1);
        private bool disposed = false;
        #endregion

        #region Constructor - Enhanced
        public PreviewViewModel(ILogger<PreviewViewModel>? logger = null) : base(logger)
        {
            // Initialize collections
            recentFiles = new AvaloniaList<FileData>();
            searchPages = new AvaloniaList<int>();
            searchPagesText = new AvaloniaList<string>();

            // Initialize cancellation tokens
            mainCts = new CancellationTokenSource();
            searchCts = new CancellationTokenSource();

            // Set initial state
            statusMessage = "Ready!";
            linkedPageMode = true;

            logger?.LogInformation("PreviewViewModel initialized");
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
                if (PageInRange(value) && SetProperty(ref requestPage1, value))
                {
                    _ = SetMainPageAsync(); // Fire and forget with proper error handling

                    if (LinkedPageMode)
                    {
                        RequestPage2 = RequestPage1 + 1;
                    }
                }
            }
        }

        private int requestPage2 = 0;
        public int RequestPage2
        {
            get => requestPage2;
            set
            {
                if (PageInRange(value) && SetProperty(ref requestPage2, value))
                {
                    _ = SetSecondaryPageAsync(); // Fire and forget with proper error handling
                }
            }
        }

        private int currentPage1 = 0;
        public int CurrentPage1
        {
            get => currentPage1;
            set
            {
                if (SetProperty(ref currentPage1, value) && CurrentFile != null)
                {
                    CurrentFile.DefaultPage = value;
                }
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
                {
                    SetDimmedMode();
                }
            }
        }

        private double rotation = 0;
        public double Rotation
        {
            get => rotation;
            set => SetProperty(ref rotation, value);
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
                {
                    this.mainRenderer.ReleaseResources();
                }

                this.mainRenderer = mainRenderer;
                this.secondaryRenderer = secondaryRenderer;

                this.mainRenderer.PageBackground = background;
                this.secondaryRenderer.PageBackground = background;

                if (CurrentFile != null)
                {
                    _ = SetMainPageAsync();
                }
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
            requestPage1 = Math.Max(0, page);
        }
        #endregion

        #region File Operations - Enhanced
        public async Task SetFileAsync(string? search = null, CancellationToken cancellationToken = default)
        {
            if (disposed || RequestFile?.Sökväg == null)
            {
                logger?.LogWarning("Cannot set file: ViewModel disposed or RequestFile is null");
                return;
            }

            await ExecuteAsync(async () =>
            {
                StatusMessage = "Setting File";
                fileAvailable = false;

                await mainCts.CancelAsync().ConfigureAwait(false);
                mainCts.Dispose();
                mainCts = new CancellationTokenSource();

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, mainCts.Token);

                FileWorkerBusy = true;
                try
                {
                    await SetMainFileAsync(linkedCts.Token).ConfigureAwait(false);

                    if (fileAvailable)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            LinkedPageMode = true;
                            SetDefaultPage();

                            if (!string.IsNullOrEmpty(search))
                            {
                                SearchMode = true;
                                _ = SearchAsync(search, linkedCts.Token);
                            }
                        }).GetTask().ConfigureAwait(false);
                    }
                }
                finally
                {
                    FileWorkerBusy = false;
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task SetMainFileAsync(CancellationToken cancellationToken)
        {
            if (RequestFile?.Sökväg == null) return;

            string path = RequestFile.Sökväg;
            int retryCount = 0;

            while (retryCount < MAX_RETRY_ATTEMPTS)
            {
                try
                {
                    bytes = await ReadBytesWithProgressAsync(path, cancellationToken).ConfigureAwait(false);

                    if (RequestFile.Sökväg == path && bytes != null)
                    {
                        await SafeDisposeAsync().ConfigureAwait(false);
                        await SetupMainFileAsync().ConfigureAwait(false);

                        fileAvailable = true;
                        return;
                    }
                    break;
                }
                catch (IOException ex) when (retryCount < MAX_RETRY_ATTEMPTS - 1)
                {
                    logger?.LogWarning(ex, "File operation failed, retrying... Attempt {Attempt}", retryCount + 1);
                    retryCount++;
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * retryCount), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    logger?.LogInformation("File operation cancelled");
                    throw;
                }
            }

            // If we get here, all retries failed
            logger?.LogWarning("Failed to set file after {MaxAttempts} attempts", MAX_RETRY_ATTEMPTS);
        }

        private async Task<byte[]?> ReadBytesWithProgressAsync(string path, CancellationToken cancellationToken)
        {
            try
            {
                Progress = 0;

                await using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    BUFFER_SIZE, useAsync: true);
                using var ms = new MemoryStream();

                long total = source.Length;
                StatusMessage = $"Reading Bytes: {total / 1_000_000:F1} MB";

                byte[] buffer = new byte[BUFFER_SIZE];
                int steps = (int)(total / buffer.Length);
                int leap = Math.Max(1, steps / PROGRESS_UPDATE_INTERVAL);
                int i = 0;

                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);

                    if (leap > 0 && i % leap == 0)
                    {
                        Progress = Math.Min(100, 100 * (i + 1) / steps);
                    }
                    i++;
                }

                Progress = 0;
                return ms.ToArray();
            }
            catch (OperationCanceledException)
            {
                logger?.LogInformation("Read bytes operation was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error reading file bytes from {Path}", path);
                return null;
            }
        }

        private async Task SetupMainFileAsync()
        {
            if (bytes == null) return;

            await Task.Run(() =>
            {
                try
                {
                    context = new MuPDFContext();
                    MainPreviewFile = new MuPDFDocument(context, bytes, InputFileTypes.PDF);
                    Pagecount = MainPreviewFile.Pages.Count;
                    CurrentFile = RequestFile;
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error setting up main file");
                    throw;
                }
            }).ConfigureAwait(false);
        }

        // Legacy method for compatibility
        [Obsolete("Use SetFileAsync instead")]
        public async void SetFile(string? search = null)
        {
            try
            {
                await SetFileAsync(search).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error in legacy SetFile method");
            }
        }
        #endregion

        #region Recent Files Management
        public void AddRecentFile(FileData? file)
        {
            if (file?.Uppdrag == string.Empty || file == null) return;

            try
            {
                if (RecentFiles.Contains(file))
                {
                    RecentFiles.Remove(file);
                }

                RecentFiles.Insert(0, file);

                while (RecentFiles.Count > MAX_RECENT_FILES)
                {
                    RecentFiles.RemoveAt(RecentFiles.Count - 1);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error adding recent file");
            }
        }
        #endregion

        #region Page Navigation
        private void SetDefaultPage()
        {
            if (RequestFile == null || MainPreviewFile == null) return;

            try
            {
                if (RequestFile.DefaultPage >= MainPreviewFile.Pages.Count)
                {
                    RequestPage1 = 0;
                }
                else
                {
                    RequestPage1 = Math.Max(0, RequestFile.DefaultPage);
                }

                CurrentPage1 = RequestPage1;
                Rotation = 0;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error setting default page");
            }
        }

        public void NextPage(bool secondPage = false)
        {
            try
            {
                if (!TwopageMode)
                {
                    RequestPage1++;
                }
                else if (LinkedPageMode)
                {
                    RequestPage1 += 2;
                }
                else if (!secondPage)
                {
                    RequestPage1++;
                }
                else
                {
                    RequestPage2++;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error navigating to next page");
            }
        }

        public void PrevPage(bool secondPage = false)
        {
            try
            {
                if (!TwopageMode)
                {
                    RequestPage1--;
                }
                else if (LinkedPageMode)
                {
                    RequestPage1 -= 2;
                }
                else if (!secondPage)
                {
                    RequestPage1--;
                }
                else
                {
                    RequestPage2--;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error navigating to previous page");
            }
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
                {
                    requestPage1 = RequestPage1 - 1;
                }

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
                    {
                        LinkedPageMode = true;
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error toggling dual view");
            }
        }

        public void ToggleDimmed()
        {
            DimmedBackground = !DimmedBackground;
        }

        public void SetDimmedMode()
        {
            try
            {
                var backgroundColor = DimmedBackground ?
                    new SolidColorBrush(Colors.AntiqueWhite) :
                    new SolidColorBrush(Colors.White);

                if (mainRenderer != null) mainRenderer.PageBackground = backgroundColor;
                if (secondaryRenderer != null) secondaryRenderer.PageBackground = backgroundColor;

                _ = SetMainPageAsync();

                if (TwopageMode)
                {
                    _ = SetSecondaryPageAsync();
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error setting dimmed mode");
            }
        }

        public void ToggleLinkedMode()
        {
            try
            {
                if (LinkedPageMode)
                {
                    if (CurrentPage1 % 2 != 0)
                    {
                        RequestPage1 = CurrentPage1 - 1;
                    }

                    RequestPage2 = CurrentPage1 + 1;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error toggling linked mode");
            }
        }

        public void ToggleVisibility(bool isVisible)
        {
            try
            {
                if (TwopageMode)
                {
                    if (mainRenderer != null) mainRenderer.IsVisible = isVisible;
                    if (secondaryRenderer != null) secondaryRenderer.IsVisible = isVisible;
                }
                else
                {
                    if (mainRenderer != null) mainRenderer.IsVisible = isVisible;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error toggling visibility");
            }
        }

        // Legacy method for compatibility
        public async void ToggleDualView() => await ToggleDualViewAsync().ConfigureAwait(false);
        #endregion

        #region Page Rendering - Enhanced
        private async Task SetMainPageAsync()
        {
            if (disposed || FileWorkerBusy || SearchBusy || !PageInRange(RequestPage1) || mainRenderer == null)
                return;

            await renderSemaphore.WaitAsync().ConfigureAwait(false);

            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    logger?.LogDebug("Setting main page to {Page}", RequestPage1);

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
                    logger?.LogDebug("Setting secondary page to {Page}", RequestPage2);

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

        #region Search Methods - Enhanced
        public async Task SearchAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!fileAvailable || string.IsNullOrWhiteSpace(text) || disposed || MainPreviewFile == null)
                return;

            await ExecuteAsync(async () =>
            {
                ClearSearch();
                regex = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.Compiled);

                await searchCts.CancelAsync().ConfigureAwait(false);
                searchCts.Dispose();
                searchCts = new CancellationTokenSource();

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, searchCts.Token);

                await SearchDocumentAsync(linkedCts.Token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }

        private async Task SearchDocumentAsync(CancellationToken cancellationToken)
        {
            if (MainPreviewFile == null || disposed) return;

            try
            {
                SearchBusy = true;
                await Dispatcher.UIThread.InvokeAsync(() => SearchPagesText.Clear()).GetTask().ConfigureAwait(false);

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
                            {
                                foundPages.Add((i, matchCount));
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "Error searching page {Page}", i);
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
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
            catch (OperationCanceledException)
            {
                logger?.LogInformation("Search operation was cancelled");
            }
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
            {
                SearchPageIndex++;
            }
        }

        public void PrevSearchPage()
        {
            if (regex != null && SearchPages != null && SearchPageIndex > 0)
            {
                SearchPageIndex--;
            }
        }

        private void SetSearchPage()
        {
            if (SearchMode && SearchItems != 0 && SearchPages.Count > SearchPageIndex)
            {
                RequestPage1 = SearchPages[SearchPageIndex];
            }
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
                {
                    await Task.Delay(POLLING_DELAY).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error stopping search");
            }
        }

        public void ClearSearch()
        {
            try
            {
                SearchItems = 0;
                SearchPageIndex = 0;
                SearchPagesText.Clear();
                SearchPages.Clear();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error clearing search");
            }
        }

        // Legacy methods for compatibility
        [Obsolete("Use SearchAsync instead")]
        public void Search(string text)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SearchAsync(text).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error in legacy Search method");
                }
            });
        }

        public async Task StopSearch() => await StopSearchAsync().ConfigureAwait(false);
        #endregion

        #region Clipboard Operations
        public void CopyToClipboard(Avalonia.Visual window)
        {
            try
            {
                if (CurrentFile != null && mainRenderer != null)
                {
                    string selectedText = mainRenderer.GetSelectedText();
                    if (!string.IsNullOrEmpty(selectedText))
                    {
                        TopLevel.GetTopLevel(window)?.Clipboard?.SetTextAsync(selectedText);
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error copying to clipboard");
            }
        }
        #endregion

        #region Cleanup and Disposal - Enhanced
        protected override async ValueTask DisposeAsyncCore()
        {
            if (disposed) return;

            try
            {
                await SafeDisposeAsync().ConfigureAwait(false);

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

            try
            {
                StatusMessage = "Disposing File";
                await StopSearchAsync().ConfigureAwait(false);
                ClearSearch();

                while (renderWorkerBusy || SearchBusy)
                {
                    logger?.LogDebug("Waiting for workers to finish...");
                    await Task.Delay(DISPOSE_POLLING_DELAY).ConfigureAwait(false);
                }

                if (MainPreviewFile != null)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        DisposeHighlight();
                        mainRenderer?.ReleaseResources();
                        secondaryRenderer?.ReleaseResources();
                        MainPreviewFile?.Dispose();
                        context?.Dispose();
                    }).GetTask().ConfigureAwait(false);

                    fileAvailable = false;
                    MainPreviewFile = null;
                    context = null;
                }

                StatusMessage = "File Disposed";
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during safe dispose");
            }
        }

        private void DisposeHighlight()
        {
            try
            {
                if (mainRenderer?.HighlightedRegions != null)
                {
                    mainRenderer.HighlightedRegions = null;
                }

                if (secondaryRenderer?.HighlightedRegions != null)
                {
                    secondaryRenderer.HighlightedRegions = null;
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error disposing highlights");
            }
        }

        public async Task CloseRendererAsync()
        {
            await StopSearchAsync().ConfigureAwait(false);
            ClearSearch();
            await SafeDisposeAsync().ConfigureAwait(false);
        }

        // Legacy methods for compatibility
        public async Task SafeDispose() => await SafeDisposeAsync().ConfigureAwait(false);
        public async Task CloseRenderer() => await CloseRendererAsync().ConfigureAwait(false);
        #endregion
    }
}
