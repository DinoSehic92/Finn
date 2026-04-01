using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using MuPDFCore.StructuredText;

namespace Finn.ViewModels
{
    public partial class PreviewViewModel
    {
        #region Search Methods
        public async Task SearchAsync(string text, CancellationToken cancellationToken = default)
        {
            if (!fileAvailable || string.IsNullOrWhiteSpace(text) || disposed || MainPreviewFile == null)
                return;

            ClearSearch();
            regex = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            try
            {
                searchCts.Cancel();
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
            // Capture current document reference to avoid races with SetFileAsync disposing
            var doc = MainPreviewFile;
            if (doc == null || disposed) return;

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            searchDone = tcs;
            try
            {
                SearchBusy = true;

                int localPageCount = Pagecount;

                // MuPDF structured-text APIs are not always safe to call from background threads
                // — batch pages per UI dispatch to reduce context-switch overhead.
                const int searchBatchSize = 10;
                List<(int pageIndex, int matchCount)> foundPagesLocal = [];
                var ct = cancellationToken;
                for (int batchStart = 0; batchStart < localPageCount; batchStart += searchBatchSize)
                {
                    ct.ThrowIfCancellationRequested();
                    // Bail out if the document was replaced since we started.
                    if (MainPreviewFile != doc) break;
                    int batchEnd = Math.Min(batchStart + searchBatchSize, localPageCount);
                    int bs = batchStart, be = batchEnd;
                    try
                    {
                        var batchResults = await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            var results = new List<(int, int)>();
                            for (int i = bs; i < be; i++)
                            {
                                // Check cancellation inside the batch so we don't
                                // block the UI thread for the remaining pages after
                                // a file switch has fired the cancellation token.
                                if (ct.IsCancellationRequested) break;
                                // Guard: document was swapped out while this batch
                                // was queued — stop before accessing disposed native memory.
                                if (MainPreviewFile != doc) break;
                                try
                                {
                                    using var disposable = doc.GetStructuredTextPage(i);
                                    var structuredPage = (MuPDFStructuredTextPage)disposable;
                                    int count = structuredPage.Search(regex!).Count();
                                    if (count > 0)
                                        results.Add((i, count));
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    logger?.LogWarning(ex, "Error searching page {Page}", i);
                                }
                            }
                            return results;
                        }).GetTask().ConfigureAwait(false);

                        foundPagesLocal.AddRange(batchResults);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (MainPreviewFile != doc)
                        return;

                    SearchPagesText.Clear();
                    foreach (var (pageIndex, matchCount) in foundPagesLocal)
                    {
                        SearchPages.Add(pageIndex);
                        SearchPagesText.Add($"Page: {pageIndex + 1} - {matchCount} items");
                    }
                    SearchItems = foundPagesLocal.Count;
                    if (SearchItems > 0)
                    {
                        SearchPageIndex = 0;
                        RequestPage1 = SearchPages[SearchPageIndex];
                        CurrentPage1 = requestPage1;
                        try
                        {
                            mainRenderer?.Search(regex!);
                        }
                        catch (Exception ex)
                        {
                            logger?.LogWarning(ex, "Renderer search failed after indexed search result navigation");
                        }
                    }
                }).GetTask().ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during document search");
                Finn.Utils.ErrorLogger.Log(ex, "PreviewViewModel.SearchDocumentAsync");
            }
            finally
            {
                SearchBusy = false;
                tcs.TrySetResult();
                // Only navigate to the first search result if the search completed
                // successfully. When cancelled (e.g. file switch), firing
                // SetMainPageAsync would compete for the render semaphore and
                // run Initialize() for a page that's about to be replaced.
                if (SearchItems > 0 && !cancellationToken.IsCancellationRequested)
                    FireAndForget(SetMainPageAsync(), nameof(SetMainPageAsync));
            }
        }

        public void NextSearchPage()
        {
            if (SearchPages != null && SearchPageIndex < SearchPages.Count - 1)
                SearchPageIndex++;
        }

        public void PrevSearchPage()
        {
            if (SearchPages != null && SearchPageIndex > 0)
                SearchPageIndex--;
        }

        private void SetSearchPage()
        {
            if (SearchMode && SearchItems != 0 && SearchPageIndex >= 0 && SearchPages.Count > SearchPageIndex)
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
                var done = searchDone;
                searchCts.Cancel();
                if (done != null)
                    await done.Task.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error stopping search");
            }
        }

        public void ClearSearch()
        {
            SearchItems = 0;
            searchPageIndex = -1;
            OnPropertyChanged(nameof(SearchPageIndex));
            SearchPagesText.Clear();
            SearchPages.Clear();
            DiffPageListMode = false;
        }
        #endregion
    }
}
