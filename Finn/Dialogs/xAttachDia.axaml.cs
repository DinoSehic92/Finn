using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Finn.Dialogs;

public partial class xAttachDia : Window
{
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Only the newly added file paths (not existing ones).
    /// </summary>
    public IReadOnlyList<string> AcceptedFiles =>
        _entries.Where(e => e.IsNew).Select(e => e.Path).ToList();

    /// <summary>
    /// The newly added folder paths (for synced folder creation).
    /// </summary>
    public IReadOnlyList<string> AcceptedFolders => _newFolders.ToList();

    private readonly ObservableCollection<AttachEntry> _entries = [];
    private readonly List<string> _newFolders = [];
    private int _existingCount;

    public xAttachDia()
    {
        InitializeComponent();
        FileListGrid.ItemsSource = _entries;
        KeyDown += CloseKey;

        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>
    /// Sets the parent file name and populates existing attached files.
    /// </summary>
    public void SetParentFile(string name, IEnumerable<FileData> existingFiles)
    {
        HeaderText.Text = $"Attach to {name}";

        foreach (var file in existingFiles)
        {
            _entries.Add(new AttachEntry
            {
                Name = file.Namn,
                Source = "Existing",
                Path = file.Sökväg,
                IsNew = false
            });
        }

        _existingCount = _entries.Count;
        UpdateState();
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Add("DragOver");
        DropZone.Classes.Remove("Collapsed");
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("DragOver");
        if (_entries.Count > 0)
            DropZone.Classes.Add("Collapsed");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("DragOver");

        var items = e.Data.GetFiles();
        if (items == null) return;

        foreach (var item in items)
        {
            string path = item.Path.LocalPath;

            if (item is IStorageFolder)
            {
                if (_newFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    continue;

                _newFolders.Add(path);
                try
                {
                    string folderName = Path.GetFileName(path) ?? path;
                    var pdfs = Directory.EnumerateFiles(path, "*.pdf", SearchOption.TopDirectoryOnly);
                    foreach (var pdf in pdfs)
                        AddNewEntry(pdf, folderName);
                }
                catch { /* inaccessible folder */ }
            }
            else if (item is IStorageFile
                     && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                AddNewEntry(path, "Dropped");
            }
        }

        UpdateState();
    }

    private void AddNewEntry(string path, string source)
    {
        if (_entries.Any(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        _entries.Add(new AttachEntry
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Source = source,
            Path = path,
            IsNew = true
        });
    }

    private void UpdateState()
    {
        int newCount = _entries.Count(e => e.IsNew);
        bool hasNew = newCount > 0;

        // Drop zone: show prominently when empty, fade when grid has content
        if (_entries.Count > 0)
            DropZone.Classes.Add("Collapsed");
        else
            DropZone.Classes.Remove("Collapsed");

        AcceptButton.IsEnabled = hasNew;

        SubHeaderText.Text = _existingCount > 0 && !hasNew
            ? $"{_existingCount} attached · drop files to add more"
            : hasNew
                ? $"{_existingCount} attached · {newCount} new to add"
                : "Drop PDF files or folders below";

        StatusText.Text = hasNew
            ? $"{newCount} file(s) will be attached on Accept"
            : "";
    }

    private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is AttachEntry entry)
        {
            e.Row.Classes.Remove("Existing");
            if (!entry.IsNew)
                e.Row.Classes.Add("Existing");
        }
    }

    private void OnRemoveSelected(object? sender, RoutedEventArgs e)
    {
        var selected = FileListGrid.SelectedItems
            .Cast<AttachEntry>()
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

    public record AttachEntry
    {
        public string Name { get; init; } = "";
        public string Source { get; init; } = "";
        public string Path { get; init; } = "";
        public bool IsNew { get; init; } = true;
    }
}
