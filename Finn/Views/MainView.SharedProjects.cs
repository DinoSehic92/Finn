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
using Avalonia.VisualTree;

namespace Finn.Views;

public partial class MainView
{
#region Shared Projects

    private TreeNodeData? _lastRightClickedNode;
    /// <summary>
    /// The project resolved from the right-clicked node, used only for context-menu display.
    /// Never assigned to CurrentProject; keeps right-click purely read-only.
    /// </summary>
    private ProjectData? _contextMenuProject;

    /// <summary>
    /// Fires when the user right-clicks a tree node's StackPanel, before the context menu opens.
    /// Captures the node and resolves the associated project (if any) for menu rendering.
    /// Does NOT change CurrentProject — navigation is a separate, deliberate user action.
    /// </summary>
    private void OnTreeNodeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        // Clear state from any previous right-click before capturing the new one.
        _lastRightClickedNode = null;
        _contextMenuProject   = null;

        if (sender is Control control && control.DataContext is TreeNodeData node)
        {
            _lastRightClickedNode = node;
            _contextMenuProject = node.Tag == "All Types"
                ? _ctx.Storage.StoredProjects.FirstOrDefault(p => p.Namn == node.Header)
                : null;
        }
    }

    private void OnTreeNewProjectButton(object? sender, RoutedEventArgs e)
    {
        _ctx.OpenProjectNewDia(ParentWindow);
    }

    private void OnTreeContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        // If OnTreeNodeContextRequested wasn't called (right-click landed on indentation/empty space),
        // fall back to the tree's selected item so the menu still reflects the highlighted node.
        if (_lastRightClickedNode == null)
        {
            _lastRightClickedNode = MainTree.SelectedItem as TreeNodeData;
            _contextMenuProject = _lastRightClickedNode?.Tag == "All Types"
                ? _ctx.Storage.StoredProjects.FirstOrDefault(p => p.Namn == _lastRightClickedNode.Header)
                : null;
        }

        string tag = _lastRightClickedNode?.Tag ?? string.Empty;
        bool isProjectNode  = tag == "All Types";
        bool isGroupNode    = tag == "Group";
        bool isSubgroupNode = tag == "Subgroup";
        bool isAnyGroup     = isGroupNode || isSubgroupNode;
        // Read share state from the right-clicked project, NOT CurrentProject.
        bool isShared       = _contextMenuProject?.IsShared == true;
        bool isViewer       = _contextMenuProject?.IsViewer == true;
        bool isSuperuser    = _ctx.UI.SuperuserMode;

        bool showProjectActions = isProjectNode;
        bool showShareActions   = isSuperuser && isProjectNode;

        foreach (var child in menu.Items)
        {
            switch (child)
            {
                case Separator sep:
                    sep.IsVisible = sep.Name switch
                    {
                        "GroupSeparator"  => isAnyGroup,
                        "SharedSeparator" => showShareActions,
                        "RemoveSeparator" => showProjectActions,
                        _                 => true
                    };
                    break;
                case MenuItem mi:
                    mi.IsVisible = mi.Name switch
                    {
                        "NewProjectMenuItem"         => true,
                        "NewGroupMenuItem"           => !isAnyGroup,
                        "RenameGroupMenuItem"        => isAnyGroup,
                        "RemoveGroupMenuItem"        => isAnyGroup,
                        "EditProjectMenuItem"        => showProjectActions,
                        "ExportProjectMenuItem"      => showProjectActions,
                        "TreeColorMenuItem"          => showProjectActions || isAnyGroup,
                        "ShareMenuItem"              => showShareActions || isSuperuser && !isProjectNode && !isAnyGroup,
                        "RemoveProjectMenuItem"      => showProjectActions,
                        _                            => true
                    };

                    if (mi.Name == "ShareMenuItem")
                    {
                        foreach (var shareChild in mi.Items)
                        {
                            if (shareChild is MenuItem shareItem)
                            {
                                shareItem.IsVisible = shareItem.Name switch
                                {
                                    // Import doesn't need a project selected — always available to superusers.
                                    "ImportSharedMenuItem"   => isSuperuser,
                                    // All other share actions require a project node.
                                    "MakeSharedMenuItem"     => showShareActions && !isShared,
                                    "PushMenuItem"           => showShareActions && isShared && !isViewer,
                                    "PullMenuItem"           => showShareActions && isShared,
                                    // Sharing settings only for the original owner of a shared project.
                                    "ShareSettingsMenuItem"  => showShareActions && isShared && (_contextMenuProject?.IsOriginalOwner == true),
                                    "UnshareMenuItem"        => showShareActions && isShared,
                                    "RestoreBackupMenuItem"  => showShareActions && isShared,
                                    _                        => true
                                };
                            }
                        }
                    }
                    break;
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

        // Let the owner choose the sharing mode before the first push
        var settingsDia = new Finn.Dialogs.xShareSettingsDia();
        _ctx.ConfigureWindow(settingsDia, window);
        settingsDia.SetCurrentMode(_ctx.CurrentProject.OneWayShare);
        await settingsDia.ShowDialog(window);

        if (!settingsDia.Confirmed)
        {
            // User cancelled — undo the shared link
            _ctx.UnshareProject();
            return;
        }

        _ctx.SetSharingMode(settingsDia.OneWayShare);

        // Now show the push confirmation
        var pushDia = new Finn.Dialogs.xSharedPushDia();
        _ctx.ConfigureWindow(pushDia, window);
        await pushDia.ShowDialog(window);

        if (pushDia.Confirmed)
        {
            _ctx.PushProjectFiltered();
            _ctx.BuildTreeData();
        }
        else
        {
            // User cancelled push — undo the shared link
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
        _ctx.ConfigureWindow(dialog, window);

        // Check for server-side changes since last push
        string? conflict = _ctx.CheckPushConflict();
        if (conflict != null)
            dialog.SetWarning(conflict);

        // Hint when server is already up to date (no local changes)
        if (_ctx.CurrentProject.SharedSyncStatus == SharedSyncState.InSync)
            dialog.SetWarning("Server is already up to date with your local copy.");

        await dialog.ShowDialog(window);

        if (dialog.Confirmed)
            _ctx.PushProjectFiltered();
    }

    private async void OnShareSettings(object? sender, RoutedEventArgs e)
    {
        if (_ctx.CurrentProject == null || !_ctx.CurrentProject.IsOriginalOwner) return;

        var window = ParentWindow;
        if (window == null) return;

        var dialog = new Finn.Dialogs.xShareSettingsDia();
        _ctx.ConfigureWindow(dialog, window);
        dialog.SetCurrentMode(_ctx.CurrentProject.OneWayShare);
        await dialog.ShowDialog(window);

        if (!dialog.Confirmed) return;

        _ctx.SetSharingMode(dialog.OneWayShare);

        string modeLabel = dialog.OneWayShare ? "one-way" : "collaborative";
        _ctx.PreviewVM.StatusMessage = $"Sharing mode set to {modeLabel}. Push to apply for other users.";
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

    private void OnTreeEditColor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string colorName }) return;

        string nodeTag = _lastRightClickedNode?.Tag ?? string.Empty;

        if (nodeTag == "All Types" && _contextMenuProject != null)
        {
            _contextMenuProject.ColorTag = colorName;
            _ctx.MarkDirty();
            _ctx.UpdateTreeview();
        }
        else if (nodeTag is "Group" or "Subgroup")
        {
            string? groupName = _lastRightClickedNode?.GroupName;
            var group = _ctx.Storage.ProjectGroups.FirstOrDefault(g => g.Name == groupName);
            if (group != null)
            {
                group.ColorTag = colorName;
                _ctx.MarkDirty();
                _ctx.UpdateTreeview();
            }
        }
    }
}
