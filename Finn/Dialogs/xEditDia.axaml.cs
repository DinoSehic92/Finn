using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Finn.Model;
using Finn.ViewModels;
using Finn.Views;
using iText.Kernel.XMP.Impl.XPath;

namespace Finn.Dialogs;

public partial class xEditDia : Window
{
    public xEditDia()
    {
        InitializeComponent();

        ProjectCategory.AddHandler(ComboBox.LoadedEvent, SetupCategory);

        KeyDown += CloseKey;

    }

    private void SetupCategory(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;

        string cat = ctx.CurrentProject.Category;

        ComboBoxItem comboBoxItem = null;

        foreach( ComboBoxItem item in ProjectCategory.Items)
        {
            if(item.Content.ToString() == cat)
            {
                comboBoxItem = item;
            }
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

            // Live-update when the watcher detects server changes
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
            SharedSyncState.InSync => "In sync ✓",
            SharedSyncState.ServerAhead => "Server has updates ↓",
            SharedSyncState.LocalAhead => "Local changes pending ↑",
            SharedSyncState.Conflicted => "Both sides changed ⇅",
            SharedSyncState.ServerMissing => "Server file missing ✕",
            _ => "Checking…"
        };
        syncText.Text = project.LastPushedUtc is { } pushed
            ? $"Last pushed: {pushed.ToLocalTime():g}  •  {status}"
            : $"Never pushed  •  {status}";
    }

    private void OnEditProject(object sender, RoutedEventArgs e)
    {
        if (ProjectName.Text != null)
        {
            MainViewModel ctx = (MainViewModel)this.DataContext;

            // Only allow rename on non-shared projects
            if (!ctx.CurrentProject.IsShared)
                ctx.RenameProject(ProjectName.Text.ToString());

            string group = null;

            if (ProjectGroup.Text != null)
            {
                group = ProjectGroup.Text.ToString();
            }

            ctx.SetGroup(group);

            if (ProjectCategory.SelectedItem is ComboBoxItem selectedCombo)
                ctx.SetCategory(selectedCombo.Content?.ToString() ?? "Project");

            ctx.CurrentProject.ReviewFolder = ReviewFolder.Text?.Trim() ?? string.Empty;

            ctx.UpdateTreeview();
        }

        this.Close();
    }

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