using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Finn.Dialogs;

public partial class xImportDia : Window
{
    public bool Confirmed { get; private set; }
    public string SelectedCategory => CategoryCombo.SelectedItem as string ?? "New";

    /// <summary>The file paths the user accepted for import.</summary>
    public IReadOnlyList<string> AcceptedPaths =>
        _entries.Where(e => e.IsNew).Select(e => e.Path).ToList();

    private readonly ObservableCollection<ImportEntry> _entries = [];
    private int _skippedCount;

    public xImportDia()
    {
        InitializeComponent();
        FileListGrid.ItemsSource = _entries;
        KeyDown += CloseKey;
    }

    /// <summary>
    /// Populates the dialog with the files to import, the categories available,
    /// and the currently selected category as default.
    /// </summary>
    public void SetFiles(
        IReadOnlyList<(string Path, string Source)> newFiles,
        int skippedCount,
        IEnumerable<string>? categories,
        string? defaultCategory)
    {
        _skippedCount = skippedCount;

        foreach (var (path, source) in newFiles)
        {
            _entries.Add(new ImportEntry
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Source = source,
                Path = path,
                IsNew = true
            });
        }

        if (categories != null)
        {
            var list = categories.ToList();
            CategoryCombo.ItemsSource = list;
            if (defaultCategory != null && list.Contains(defaultCategory))
                CategoryCombo.SelectedItem = defaultCategory;
            else if (list.Count > 0)
                CategoryCombo.SelectedIndex = 0;
        }

        UpdateState();
    }

    private void UpdateState()
    {
        int newCount = _entries.Count(e => e.IsNew);
        bool hasNew = newCount > 0;

        AcceptButton.IsEnabled = hasNew;

        SubHeaderText.Text = hasNew
            ? $"{newCount} file(s) to add"
            : "No new files to add";

        string skippedHint = _skippedCount > 0
            ? $" · {_skippedCount} skipped (already in project)"
            : "";

        StatusText.Text = hasNew
            ? $"{newCount} file(s) will be imported on Accept{skippedHint}"
            : _skippedCount > 0
                ? $"{_skippedCount} file(s) skipped — already in project"
                : "";
    }

    private void OnRemoveSelected(object? sender, RoutedEventArgs e)
    {
        var selected = FileListGrid.SelectedItems
            .Cast<ImportEntry>()
            .Where(entry => entry.IsNew)
            .ToList();

        foreach (var item in selected)
            _entries.Remove(item);

        UpdateState();
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void CloseKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    public record ImportEntry
    {
        public string Name { get; init; } = "";
        public string Source { get; init; } = "";
        public string Path { get; init; } = "";
        public bool IsNew { get; init; } = true;
    }
}
