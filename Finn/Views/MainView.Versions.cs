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
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Finn.Views;

public partial class MainView
{
#region Bookmarks

    private void BookmarkSelected(object? sender, RoutedEventArgs e)
    {
        if ((_ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen) && BookmarkGrid.SelectedItem is PageData page)
            _ctx.Collections.SetBookmark(page);
    }

    private void OnAddBookmark(object? sender, RoutedEventArgs e)
    {
        if (_ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen)
        {
            _ctx.Collections.AddBookmark(BookmarkInput.Text ?? string.Empty);
            BookmarkInput.Clear();
            UpdateBookmarksEmptyHint();
        }
    }

    private void OnRenameBookmark(object? sender, RoutedEventArgs e)
    {
        _ctx.Collections.RenameBookmark(BookmarkInput.Text ?? string.Empty);
        BookmarkInput.Clear();
    }

    private void OnRemoveBookmark(object? sender, RoutedEventArgs e)
    {
        if (BookmarkGrid.SelectedItem is PageData page)
            _ctx.Collections.RemoveBookmark(page);
        UpdateBookmarksEmptyHint();
    }

    private void OnImportPdfBookmarks(object? sender, RoutedEventArgs e)
    {
        if (!(_ctx.UI.PreviewEmbeddedOpen || _ctx.PreviewWindowOpen)) return;

        var renderer = AnnotationRenderer;
        if (renderer == null) return;

        var outlineItems = renderer.GetOutlineBookmarks();
        if (outlineItems.Count == 0) return;

        _ctx.Collections.ImportBookmarks(outlineItems);
        UpdateBookmarksEmptyHint();
    }

    #endregion

    #region Versions

    private async void SelectVersion(object? sender, RoutedEventArgs e)
    {
        if (VersionsGrid.SelectedItem is not FileVersionData version) return;
        // Block version preview while in Diff or Dual-File mode
        if (_pwr.DualFileMode || _pwr.DiffOverlayActive) return;
        _ctx.SelectedVersion = version;
        string? searchText = _ctx.IndexedSearch ? SearchText.Text : null;
        await _ctx.PreviewVersionAsync(version, searchText);
    }

    private async void OnRemoveVersion(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile == null || _ctx.SelectedVersion == null) return;

        var window = ParentWindow;
        await _ctx.ConfirmDeleteDia(window);

        if (_ctx.Confirmed)
        {
            _ctx.RemoveSelectedVersion();
            UpdateVersionsEmptyHint();
        }
    }

    private void OnVersionDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        _ctx.SetActiveVersion();
    }

    private void OnSetCurrentVersion(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not MenuItem { DataContext: FileVersionData version }) return;
        _ctx.SetCurrentVersionOnSelected(version.Label);
    }

    private void OnLabelFirstVersion(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { SelectedItem: string label }) return;
        _ctx.LabelFirstVersionOnSelected(label);
    }

    private void OnLabelLatestVersion(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { SelectedItem: string label }) return;
        _ctx.LabelLastVersionOnSelected(label);
    }

    private void OnCompareVersionWithOriginal(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile is not { HasVersions: true } file) return;
        if (VersionsGrid.SelectedItem is not FileVersionData selected) return;
        if (string.IsNullOrEmpty(file.OriginalPath)) return;

        string pathA = file.OriginalPath;
        string pathB = selected.Sökväg;

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)
            || !pathA.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !pathB.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(pathA) || !File.Exists(pathB))
            return;

        _ctx.RunDiffInPreviewer(pathA, pathB);
    }

    private void OnCompareVersionWithPrevious(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile is not { HasVersions: true } file) return;
        if (VersionsGrid.SelectedItem is not FileVersionData selected) return;

        int idx = file.Versions.IndexOf(selected);
        if (idx < 0) return;

        // First version: compare against original file.
        // Other versions: compare against the preceding version.
        string pathA;
        if (idx == 0)
        {
            if (string.IsNullOrEmpty(file.OriginalPath)) return;
            pathA = file.OriginalPath;
        }
        else
        {
            var prev = file.Versions[idx - 1];
            pathA = prev.Sökväg;
        }

        string pathB = selected.Sökväg;

        if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB)
            || !pathA.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !pathB.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(pathA) || !File.Exists(pathB))
            return;

        _ctx.RunDiffInPreviewer(pathA, pathB);
    }

    private void OnLabelSelectedVersion(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { SelectedItem: string label }) return;
        if (_ctx.CurrentFile == null || _ctx.SelectedVersion == null) return;
        _ctx.CurrentFile.SetVersionLabel(_ctx.SelectedVersion, label);
        _ctx.MarkDirty();
    }

    private void OnLabelFromFolderDate(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile == null || _ctx.SelectedVersion == null) return;
        _ctx.LabelVersionFromFolderDate();
    }

    private async void OnAddManualVersion(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentFile == null) return;
        var window = ParentWindow;
        var (confirmed, path, label) = await _ctx.ShowManualVersionDialogAsync(window);
        if (confirmed && path != null)
        {
            _ctx.AddManualVersion(path, label);
            UpdateVersionsEmptyHint();
        }
    }

    #endregion

    #region Annotation Layers

    private void OnPreviewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Re-sync the layer list whenever the active layers change.
        // PropertyChanged may fire from a background thread (e.g. CurrentFile
        // is set after ConfigureAwait(false) in SetFileAsync), so dispatch
        // to the UI thread to avoid cross-thread access on LayerList.
        if (e.PropertyName is "CurrentFile" or "WhiteboardMode" or "LayersChanged")
            Dispatcher.UIThread.Post(() => SyncLayerList(), DispatcherPriority.Background);
    }

    private Controls.AnnotatedPDFRenderer? AnnotationRenderer
        => (EmbeddedPreview as Views.PreView)?.MuPDFRenderer;

    private void SyncLayerList()
    {
        var renderer = AnnotationRenderer;
        if (renderer == null) return;
        renderer.EnsureDefaultLayer();
        // Keep the taskbar layer label in sync after a file switch.
        if (EmbeddedPreview is Views.PreView preview)
            preview.RefreshLayerLabel();
    }

    #endregion
}
