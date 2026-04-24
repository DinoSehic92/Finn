using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
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

        KeyDown += CloseKey;
    }

    private ComboBox? _projectGroupCombo;

    private void SetupGroupCombo(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx) return;
        _projectGroupCombo = sender as ComboBox ?? this.FindControl<ComboBox>("ProjectGroup");
        if (_projectGroupCombo == null) return;

        _groupItems = ctx.GetProjectGroupPickerItems();
        _projectGroupCombo.ItemsSource = _groupItems.Select(i => i.Label).ToList();

        string? currentParent = ctx.CurrentProject.Parent;
        int idx = _groupItems.ToList().FindIndex(i => i.GroupName == currentParent);
        _projectGroupCombo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void SetupCategory(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;

        string cat = ctx.CurrentProject.Category;

        ComboBoxItem comboBoxItem = null;

        foreach (ComboBoxItem item in ProjectCategory.Items)
        {
            if (item.Content.ToString() == cat)
                comboBoxItem = item;
        }

        ProjectCategory.SelectedItem = comboBoxItem;

        // Shared projects cannot be renamed — disable the name field
        if (ctx.CurrentProject.IsShared)
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
                            () => UpdateSyncText(syncText, ctx.CurrentProject));
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

    private void OnEditProject(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel ctx) return;
        if (ProjectName.Text == null) return;

        if (!ctx.CurrentProject.IsShared)
            ctx.RenameProject(ProjectName.Text.ToString());

        // Read selected group from the picker
        int groupIdx = _projectGroupCombo?.SelectedIndex ?? -1;
        if (groupIdx >= 0 && groupIdx < _groupItems.Count)
            ctx.CurrentProject.Parent = _groupItems[groupIdx].GroupName; // null = no group

        // Category is always set from the Category ComboBox
        if (ProjectCategory.SelectedItem is ComboBoxItem selectedCombo)
            ctx.SetCategory(selectedCombo.Content?.ToString() ?? "Project");

        ctx.CurrentProject.ReviewFolder = ReviewFolder.Text?.Trim() ?? string.Empty;
        ctx.MarkDirty();
        ctx.UpdateTreeview();

        this.Close();
    }

    private async void OnRunIntegrityCheck(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel ctx)
            await ctx.ShowIntegrityReportAsync(this);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => this.Close();

    private void ResetColor(object sender, RoutedEventArgs e)
    {
        ColorPickerForeground.Color = Color.Parse("#FFFFFFFF");
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            this.Close();
        else if (e.Key == Key.Enter)
            OnEditProject(sender, e);
    }
}