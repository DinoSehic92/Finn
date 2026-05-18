using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using Finn.ViewModels;
using Finn.Views;
using System.Linq;

namespace Finn.Dialogs;

public partial class xEditDia : Window
{
    // Mirrors GetGroupParentPickerItems entries so we can read back the selection
    private System.Collections.Generic.IReadOnlyList<(string Label, string? GroupName, string Category)> _groupItems
        = System.Array.Empty<(string, string?, string)>();

    public xEditDia()
    {
        InitializeComponent();

        ProjectCategory.AddHandler(ComboBox.LoadedEvent, SetupCategory);
        this.FindControl<ComboBox>("ProjectGroup")?.AddHandler(ComboBox.LoadedEvent, SetupGroupCombo);

        // Wire the category→group refresh only after the window is fully open,
        // so the initial SetupCategory selection does not reset the group picker.
        Opened += (_, _) =>
        {
            if (DataContext is not MainViewModel ctx) return;
            ProjectCategory.SelectionChanged += (_, _) =>
            {
                string cat = (ProjectCategory.SelectedItem as ComboBoxItem)?.Content?.ToString()
                             ?? ctx.CurrentProject?.Category ?? string.Empty;
                RefreshGroupPicker(cat, null);
            };

            // Inject live candidate counts into Reapply/Reset button tooltips so
            // the user knows what will be affected before clicking.
            UpdateVersionGroupCounts(ctx);
        };

        KeyDown += CloseKey!;
    }

    private ComboBox? _projectGroupCombo;

    private void SetupGroupCombo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx) return;
        _projectGroupCombo = sender as ComboBox ?? this.FindControl<ComboBox>("ProjectGroup");
        if (_projectGroupCombo == null) return;

        RefreshGroupPicker(ctx.CurrentProject?.Category ?? string.Empty, ctx.CurrentProject?.Parent);
    }

    private void RefreshGroupPicker(string category, string? selectedGroup)
    {
        if (_projectGroupCombo == null) return;
        if (DataContext is not MainViewModel ctx) return;

        _groupItems = ctx.GetProjectGroupPickerItems(category);
        _projectGroupCombo.ItemsSource = _groupItems.Select(i => i.Label).ToList();

        int idx = selectedGroup != null
            ? _groupItems.ToList().FindIndex(i => i.GroupName == selectedGroup)
            : -1;
        _projectGroupCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void SetupCategory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx) return;

        string cat = ctx.CurrentProject?.Category ?? string.Empty;

        ComboBoxItem? comboBoxItem = null;

        foreach (ComboBoxItem? item in ProjectCategory.Items)
        {
            if (item?.Content?.ToString() == cat)
                comboBoxItem = item;
        }

        ProjectCategory.SelectedItem = comboBoxItem;

        // Shared projects cannot be renamed — disable the name field
        if (ctx.CurrentProject?.IsShared == true)
        {
            ProjectName.IsEnabled = false;
            ProjectName.PlaceholderText = "Rename disabled (shared)";

            var panel = this.FindControl<StackPanel>("SharedInfoPanel");
            var pathText = this.FindControl<TextBlock>("SharedPathText");
            var syncText = this.FindControl<TextBlock>("SharedSyncText");
            if (panel != null) panel.IsVisible = true;
            if (pathText != null) pathText.Text = $"Server: {ctx.CurrentProject.SharedPath}";
            UpdateSyncText(syncText, ctx.CurrentProject);

            if (syncText != null)
            {
                ctx.CurrentProject.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ProjectData.SharedSyncStatus))
                        Avalonia.Threading.Dispatcher.UIThread.Post(
                            () => UpdateSyncText(syncText, ctx.CurrentProject!));
                };
            }
        }
    }

    private static void UpdateSyncText(TextBlock? syncText, ProjectData project)
    {
        if (syncText == null) return;
        string status = project.SharedSyncStatus switch
        {
            SharedSyncState.InSync      => "In sync ✓",
            SharedSyncState.ServerAhead => "Server has updates ↓",
            SharedSyncState.LocalAhead  => "Local changes pending ↑",
            SharedSyncState.Conflicted  => "Both sides changed ⇅",
            SharedSyncState.ServerMissing => "Server file missing ✕",
            _ => "Checking…"
        };
        syncText.Text = project.LastPushedUtc is { } pushed
            ? $"Last pushed: {pushed.ToLocalTime():g}  •  {status}"
            : $"Never pushed  •  {status}";
    }

    private void UpdateVersionGroupCounts(MainViewModel ctx)
    {
        if (ctx.CurrentProject == null) return;

        var reapplyBtn = this.FindControl<Button>("ReapplyVersionsButton");
        var resetBtn   = this.FindControl<Button>("ResetVersionsButton");

        int candidates = MainViewModel.CountAutoGroupCandidates(ctx.CurrentProject);
        int autoGroups = MainViewModel.CountAutoGroupReset(ctx.CurrentProject);

        string reapplyTip = candidates == 0
            ? "Re-apply auto-grouping project-wide — no ungrouped candidates found with the current suffix."
            : $"Re-apply auto-grouping project-wide using the current suffix. {candidates} ungrouped file{(candidates == 1 ? "" : "s")} are eligible. Already-versioned files are not affected.";

        string resetTip = autoGroups == 0
            ? "Dissolve auto-grouped version sets — no auto-grouped sets found. Manually added versions are always preserved."
            : $"Dissolve {autoGroups} auto-grouped set{(autoGroups == 1 ? "" : "s")} — each version file is restored as a standalone entry. Manually added versions are preserved.";

        if (reapplyBtn != null) ToolTip.SetTip(reapplyBtn, reapplyTip);
        if (resetBtn   != null) ToolTip.SetTip(resetBtn,   resetTip);
    }

    private void OnEditProject(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx) return;
        if (ProjectName.Text == null) return;

        if (ctx.CurrentProject?.IsShared == false)
            ctx.RenameProject(ProjectName.Text!.ToString());

        // Category is always set from the Category ComboBox
        if (ProjectCategory.SelectedItem is ComboBoxItem selectedCombo)
            ctx.SetCategory(selectedCombo.Content?.ToString() ?? "Project");

        // Read selected group from the picker (must be after SetCategory)
        int groupIdx = _projectGroupCombo?.SelectedIndex ?? -1;
        if (groupIdx >= 0 && groupIdx < _groupItems.Count)
            ctx.CurrentProject!.Parent = _groupItems[groupIdx].GroupName;

        ctx.CurrentProject!.ReviewFolder = ReviewFolder.Text?.Trim() ?? string.Empty;

        var versionSuffixBox = this.FindControl<TextBox>("VersionSuffixBox");
        if (versionSuffixBox != null)
            ctx.CurrentProject!.VersionSuffix = versionSuffixBox.Text?.Trim() ?? "v";

        ctx.MarkDirty();
        ctx.UpdateTreeview();

        this.Close();
    }

    private async void OnRunIntegrityCheck(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel ctx)
            await ctx.ShowIntegrityReportAsync(this);
    }

    private void OnReapplyVersionGrouping(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx || ctx.CurrentProject == null) return;

        // Save the current suffix value first so grouping uses whatever is in the box
        var versionSuffixBox = this.FindControl<TextBox>("VersionSuffixBox");
        if (versionSuffixBox != null)
            ctx.CurrentProject.VersionSuffix = versionSuffixBox.Text?.Trim() ?? "v";

        ctx.ReapplyVersionGrouping(ctx.CurrentProject);
        UpdateVersionGroupCounts(ctx);
    }

    private void OnResetVersionGrouping(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx || ctx.CurrentProject == null) return;
        ctx.ResetVersionGrouping(ctx.CurrentProject);
        UpdateVersionGroupCounts(ctx);
    }

    private void OnToggleVersionInfo(object? sender, RoutedEventArgs e)
    {
        var panel = this.FindControl<Border>("VersionInfoPanel");
        if (panel != null) panel.IsVisible = !panel.IsVisible;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => this.Close();

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            this.Close();
        else if (e.Key == Key.Enter)
            OnEditProject(sender, e);
    }
}