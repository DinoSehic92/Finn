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
    /// Only the newly added file paths that were dropped individually (not from folders).
    /// Files from folders are handled by the sync folder created from AcceptedFolders.
    /// </summary>
    public IReadOnlyList<string> AcceptedFiles =>
        _entries.Where(e => e.IsNew && !e.IsFromFolder).Select(e => e.Path).ToList();

    /// <summary>
    /// The newly added folder paths (for synced folder creation).
    /// </summary>
    public IReadOnlyList<string> AcceptedFolders => _newFolders.ToList();

    private readonly ObservableCollection<AttachEntry> _entries = [];
    private readonly List<string> _newFolders = [];
    private HashSet<string> _allProjectPaths = new(StringComparer.OrdinalIgnoreCase);
    private int _existingCount;
    private int _skippedCount;

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
    /// Sets the parent file name, populates existing attached files, and
    /// registers all project file paths so duplicates can be rejected.
    /// </summary>
    public void SetParentFile(string name, IEnumerable<FileData> existingFiles, IEnumerable<string> allProjectPaths)
    {
        HeaderText.Text = $"Attach to {name}";
        _allProjectPaths = new HashSet<string>(allProjectPaths, StringComparer.OrdinalIgnoreCase);

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
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("DragOver");
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("DragOver");

        var items = e.DataTransfer.TryGetFiles();
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
                    string folderName = new DirectoryInfo(path).Name;
                    var pdfs = Directory.EnumerateFiles(path, "*.pdf", SearchOption.TopDirectoryOnly);
                    foreach (var pdf in pdfs)
                        AddNewEntry(pdf, folderName, isFromFolder: true);
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

    private void AddNewEntry(string path, string source, bool isFromFolder = false)
    {
        if (_entries.Any(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
            return;

        if (_allProjectPaths.Contains(path))
        {
            _skippedCount++;
            return;
        }

        _entries.Add(new AttachEntry
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Source = source,
            Path = path,
            IsNew = true,
            IsFromFolder = isFromFolder
        });
    }

    private void UpdateState()
    {
        int newCount = _entries.Count(e => e.IsNew);
        bool hasNew = newCount > 0;

        AcceptButton.IsEnabled = hasNew;

        SubHeaderText.Text = _existingCount > 0 && !hasNew
            ? $"{_existingCount} attached · drop files to add more"
            : hasNew
                ? $"{_existingCount} attached · {newCount} new to add"
                : "Drop PDF files or folders below";

        string skippedHint = _skippedCount > 0
            ? $" · {_skippedCount} skipped (already in project)"
            : "";

        StatusText.Text = hasNew
            ? $"{newCount} file(s) will be attached on Accept{skippedHint}"
            : _skippedCount > 0
                ? $"{_skippedCount} file(s) skipped — already in project"
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
        public bool IsFromFolder { get; init; }
    }
}
