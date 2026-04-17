using Avalonia;
using Avalonia.Controls;
using System;
using Avalonia.Interactivity;
using System.Linq;
using Finn.ViewModels;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Input;
using System.Collections.Generic;
using Finn.Model;
using System.IO;
using Avalonia.Platform.Storage;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Finn.Views;

public partial class MainView
{
#region Metadata Worker

    private async void OnFetchThumbnails(object? sender, RoutedEventArgs e)
    {
        if (_ctx.PreviewVM.BackgroundTaskActive) return;

        var cts = new CancellationTokenSource();
        _ctx.PreviewVM.SetBackgroundTaskCts(cts);
        _ctx.PreviewVM.BackgroundTaskMessage = "Generating Thumbnails";
        _ctx.PreviewVM.BackgroundTaskActive = true;
        _ctx.PreviewVM.BackgroundTaskProgress = 0;

        try
        {
            var progress = new Progress<int>(p =>
                _ctx.PreviewVM.BackgroundTaskProgress = p);
            await _ctx.Data.GenerateThumbnailsAsync(progress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _ctx.PreviewVM.BackgroundTaskMessage = "Thumbnails cancelled";
        }
        catch (Exception ex)
        {
            Utils.ErrorLogger.Log(ex, "OnFetchThumbnails");
        }
        finally
        {
            _ctx.PreviewVM.SetBackgroundTaskCts(null);
            _ctx.PreviewVM.BackgroundTaskMessage = "";
            _ctx.PreviewVM.BackgroundTaskProgress = 0;
            _ctx.PreviewVM.BackgroundTaskActive = false;
            cts.Dispose();
        }
    }

    private async void OnFetchIndex(object? sender, RoutedEventArgs e)
    {
        if (_ctx.PreviewVM.BackgroundTaskActive) return;

        var cts = new CancellationTokenSource();
        _ctx.PreviewVM.SetBackgroundTaskCts(cts);
        _ctx.PreviewVM.BackgroundTaskMessage = "Indexing Files";
        _ctx.PreviewVM.BackgroundTaskActive = true;
        _ctx.PreviewVM.BackgroundTaskProgress = 0;

        try
        {
            var progress = new Progress<int>(p =>
                _ctx.PreviewVM.BackgroundTaskProgress = p);
            var statusProgress = new Progress<string>(name =>
                _ctx.PreviewVM.BackgroundTaskMessage = $"Indexing: {name}");

            await _ctx.Data.GetContentAsync(progress, statusProgress, cts.Token);
        }
        catch (OperationCanceledException)
        {
            _ctx.PreviewVM.BackgroundTaskMessage = "Indexing cancelled";
        }
        catch (Exception ex)
        {
            Utils.ErrorLogger.Log(ex, "OnFetchIndex");
        }
        finally
        {
            _ctx.PreviewVM.SetBackgroundTaskCts(null);
            _ctx.PreviewVM.BackgroundTaskMessage = "";
            _ctx.PreviewVM.BackgroundTaskProgress = 0;
            _ctx.PreviewVM.BackgroundTaskActive = false;
            cts.Dispose();
        }
    }

    private async void OnFetchSingleMeta(object? sender, RoutedEventArgs e) => await RunMetaWorkerAsync(singleFile: true);
    private async void OnFetchFullMeta(object? sender, RoutedEventArgs e) => await RunMetaWorkerAsync(singleFile: false);

    private async Task RunMetaWorkerAsync(bool singleFile)
    {
        if (_ctx.PreviewVM.BackgroundTaskActive) return;

        _ctx.PreviewVM.BackgroundTaskMessage = "Fetching Metadata";
        _ctx.PreviewVM.BackgroundTaskActive = true;
        _ctx.PreviewVM.BackgroundTaskProgress = 0;
        _ctx.Data.SelectFilesForMetaworker(singleFile);

        try
        {
            int total = _ctx.Data.GetNrSelectedFiles();
            var progress = new Progress<int>(p =>
                _ctx.PreviewVM.BackgroundTaskProgress = p);

            await Task.Run(() =>
            {
                for (int k = 0; k < total; k++)
                {
                    _ctx.Data.GetMetadata(k);
                    ((IProgress<int>)progress).Report((k + 1) * 100 / Math.Max(1, total));
                }
            });

            _ctx.Data.SetMeta();
            _ctx.MarkDirty();
        }
        catch (Exception ex)
        {
            Utils.ErrorLogger.Log(ex, "RunMetaWorkerAsync");
        }
        finally
        {
            _ctx.PreviewVM.BackgroundTaskMessage = "";
            _ctx.PreviewVM.BackgroundTaskProgress = 0;
            _ctx.PreviewVM.BackgroundTaskActive = false;
        }
    }

    

    #endregion
}
