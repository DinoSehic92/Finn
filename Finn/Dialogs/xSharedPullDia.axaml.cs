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
    public bool IsMerge { get; private set; }

    private ObservableCollection<SharedDiffEntry> _entries = [];

    /// <summary>
    /// File names whose "Accept Incoming" checkbox was ticked for at least one category.
    /// </summary>
    public HashSet<string> AcceptIncomingFiles => new(
        _entries.Where(e => e.AcceptIncoming && e.FileName != null).Select(e => e.FileName!),
        System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lookup: (FileName, Category) pairs where AcceptIncoming was checked.
    /// Used by Merge: these entries get full server overwrite instead of additive merge.
    /// </summary>
    public HashSet<(string File, string Category)> AcceptIncomingEntries => new(
        _entries.Where(e => e.AcceptIncoming && e.FileName != null)
                .Select(e => (e.FileName!, e.Category)));

    /// <summary>
    /// Lookup: (FileName, Category) pairs where the checkbox was checked.
    /// Used by Replace: these entries keep local values instead of being overwritten.
    /// </summary>
    public HashSet<(string File, string Category)> KeepLocalEntries => AcceptIncomingEntries;

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
            AcceptButton.Content = "Replace Anyway";
        }
    }

    private void OnMerge(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        IsMerge = true;
        Close();
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        IsMerge = false;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
