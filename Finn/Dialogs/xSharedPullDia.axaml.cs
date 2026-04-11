using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Finn.Dialogs;

public partial class xSharedPullDia : Window
{
    public bool Confirmed { get; private set; }

    private ObservableCollection<SharedDiffEntry> _entries = [];

    /// <summary>
    /// Lookup: (FileName, Category) pairs where KeepLocal was checked.
    /// Merge will preserve local values for these entries instead of
    /// applying the server value.
    /// </summary>
    public HashSet<(string File, string Category)> KeepLocalEntries => new(
        _entries.Where(e => e.KeepLocal && e.FileName != null)
                .Select(e => (e.FileName!, e.Category)));

    public xSharedPullDia()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public void SetDiff(List<SharedDiffEntry> entries, string summary)
    {
        _entries = new ObservableCollection<SharedDiffEntry>(entries);
        DiffGrid.ItemsSource = _entries;
        SummaryText.Text = summary;

        if (entries.Count == 0)
        {
            SubHeaderText.Text = "The server copy is identical to your local project.";
            this.FindControl<Button>("MergeButton")!.Content = "Merge Anyway";
        }
    }

    /// <summary>
    /// Hides the "Keep Local" column for viewers since their local
    /// changes are overwritten by server values on every pull.
    /// </summary>
    public void SetViewerMode()
    {
        DiffGrid.Columns[0].IsVisible = false;
    }

    private void OnMerge(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
