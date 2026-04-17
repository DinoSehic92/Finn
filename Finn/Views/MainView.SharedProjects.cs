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
#region Shared Projects

    /// <summary>
    /// Shows/hides shared-project menu items based on the current project state
    /// and whether superuser mode is enabled.
    /// </summary>
    private void OnTreeContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        bool isShared = _ctx.CurrentProject?.IsShared == true;
        bool isViewer = _ctx.CurrentProject?.IsViewer == true;
        bool isSuperuser = _ctx.UI.SuperuserMode;

        foreach (var child in menu.Items)
        {
            if (child is MenuItem mi)
            {
                switch (mi.Name)
                {
                    case "MakeSharedMenuItem":
                        mi.IsVisible = isSuperuser && !isShared;
                        break;
                    case "ImportSharedMenuItem":
                        mi.IsVisible = isSuperuser && !isShared;
                        break;
                    case "PushMenuItem":
                        mi.IsVisible = isSuperuser && isShared && !isViewer;
                        break;
                    case "PullMenuItem":
                    case "UnshareMenuItem":
                    case "RestoreBackupMenuItem":
                        mi.IsVisible = isSuperuser && isShared;
                        break;
                }
            }
        }
    }

    private async void OnMakeProjectShared(object? sender, RoutedEventArgs e)
    {
        var topLevel = (TopLevel)ParentWindow;
        if (topLevel == null) return;

        var window = topLevel as MainWindow;
        if (window == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select shared server folder",
                AllowMultiple = false
            });

        if (folders.Count == 0) return;

        string serverFolder = folders[0].Path.LocalPath;

        // Link the project to the shared path first
        _ctx.MakeProjectShared(serverFolder);

        // Show push options dialog for the initial push
        var dialog = new Finn.Dialogs.xSharedPushDia();
        dialog.DataContext = _ctx;
        dialog.FontFamily = window.FontFamily;
        dialog.RequestedThemeVariant = window.ActualThemeVariant;
        dialog.SetOneWayShare(_ctx.CurrentProject.OneWayShare);

        await dialog.ShowDialog(window);

        if (dialog.Confirmed)
        {
            _ctx.PushProjectFiltered(dialog);
            _ctx.BuildTreeData();
        }
        else
        {
            // User cancelled — undo the shared link
            _ctx.UnshareProject();
        }
    }

    private async void OnImportSharedProject(object? sender, RoutedEventArgs e)
    {
        var topLevel = (TopLevel)ParentWindow;
        if (topLevel == null) return;

        var jsonType = new FilePickerFileType("Shared Project") { Patterns = ["*.json"] };
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = "Import shared project",
                AllowMultiple = false,
                FileTypeFilter = [jsonType]
            });

        if (files.Count == 0) return;

        if (_ctx.ImportSharedProject(files[0].Path.LocalPath))
            _ctx.RefreshFolderWatchers();
    }

    private async void OnPushProject(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentProject?.SharedPath == null) return;
        if (_ctx.CurrentProject.IsViewer) return;

        var window = ParentWindow;
        if (window == null) return;

        var dialog = new Finn.Dialogs.xSharedPushDia();
        dialog.DataContext = _ctx;
        dialog.FontFamily = window.FontFamily;
        dialog.RequestedThemeVariant = window.ActualThemeVariant;
        dialog.SetOneWayShare(_ctx.CurrentProject.OneWayShare);

        // Check for server-side changes since last push
        string? conflict = _ctx.CheckPushConflict();
        if (conflict != null)
            dialog.SetWarning(conflict);

        // Hint when server is already up to date (no local changes)
        if (_ctx.CurrentProject.SharedSyncStatus == SharedSyncState.InSync)
            dialog.SetWarning("Server is already up to date with your local copy.");

        await dialog.ShowDialog(window);

        if (dialog.Confirmed)
            _ctx.PushProjectFiltered(dialog);
    }

    private async void OnPullProject(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentProject?.SharedPath == null) return;

        var window = ParentWindow;
        if (window == null) return;

        // Read the server copy for diffing
        var serverProject = MainViewModel.ReadServerProject(_ctx.CurrentProject.SharedPath);
        if (serverProject == null)
        {
            string msg = File.Exists(_ctx.CurrentProject.SharedPath)
                ? "Pull failed: could not parse server file"
                : $"Pull failed: server file not found at {_ctx.CurrentProject.SharedPath}";
            _ctx.PreviewVM.StatusMessage = msg;
            return;
        }

        // Build and show diff
        var entries = _ctx.BuildPullDiff(_ctx.CurrentProject, serverProject);
        string summary = MainViewModel.BuildPullSummary(entries);

        var dialog = new Finn.Dialogs.xSharedPullDia();
        dialog.DataContext = _ctx;
        dialog.FontFamily = window.FontFamily;
        dialog.RequestedThemeVariant = window.ActualThemeVariant;
        dialog.SetDiff(entries, summary);

        if (_ctx.CurrentProject.IsViewer)
            dialog.SetViewerMode();

        await dialog.ShowDialog(window);

        if (dialog.Confirmed)
            _ctx.MergeProject(serverProject, dialog.KeepLocalEntries);
    }

    private async void OnUnshareProject(object? sender, RoutedEventArgs e)
    {
        var window = ParentWindow;
        if (window == null) return;

        var dialog = new Finn.Dialogs.xMessageDia();
        _ctx.ConfigureWindow(dialog, window);
        dialog.SetMessage("This will make the project local-only. You can re-import the shared copy later.");
        await dialog.ShowDialog(window);

        _ctx.UnshareProject();
    }

    private async void OnRestoreBackup(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentProject == null) return;

        var window = ParentWindow;
        if (window == null) return;

        var dialog = new Finn.Dialogs.xBackupBrowserDia();
        _ctx.ConfigureWindow(dialog, window);
        dialog.SetBackups(_ctx.GetBackupDirectory(), _ctx.CurrentProject.Namn);

        await dialog.ShowDialog(window);

        if (dialog.Confirmed && !string.IsNullOrEmpty(dialog.SelectedBackupPath))
            _ctx.RestoreFromBackup(dialog.SelectedBackupPath);
    }

    #endregion
}
