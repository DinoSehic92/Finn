using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Finn.Dialogs;

public partial class xSyncRemoveDia : Window
{
    public bool Confirmed { get; private set; }

    private readonly ObservableCollection<RemoveEntry> _entries = [];

    public xSyncRemoveDia()
    {
        InitializeComponent();
        FileListGrid.ItemsSource = _entries;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public void SetFiles(IReadOnlyList<(string Name, string Path)> files, string folderName, string? subtitle = null)
    {
        foreach (var (name, path) in files)
        {
            _entries.Add(new RemoveEntry
            {
                Name = name,
                Source = folderName
            });
        }

        SubHeaderText.Text = subtitle
            ?? (files.Count == 1
                ? "1 file no longer exists on disk"
                : $"{files.Count} files no longer exist on disk");

        StatusText.Text = $"{files.Count} file(s) will be removed from the project on Accept";
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

    public record RemoveEntry
    {
        public string Name { get; init; } = "";
        public string Source { get; init; } = "";
    }
}
