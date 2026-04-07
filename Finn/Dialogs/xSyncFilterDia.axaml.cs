using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Finn.Dialogs;

public partial class xSyncFilterDia : Window
{
    public bool Confirmed { get; private set; }

    private ObservableCollection<SyncFilterEntry> _entries = [];
    private bool _suppressToggle;

    public xSyncFilterDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;
    }

    /// <summary>
    /// Populates the dialog with files on disk for the given folder.
    /// Runs the directory scan on a background thread to avoid blocking the UI.
    /// Files in <see cref="FolderData.ExcludedFiles"/> start unchecked.
    /// For version folders, exclusions are stored as full paths; for other
    /// folders they are stored as file names without extension.
    /// When <paramref name="projectFileNames"/> is provided, only files
    /// whose name (without extension) matches a project file are shown.
    /// </summary>
    public async Task SetFolderAsync(FolderData folder, string pattern = "*.pdf",
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        IReadOnlySet<string>? projectFileNames = null)
    {
        HeaderText.Text = $"Synced Files \u2014 {folder.Name}";

        bool isVersionFolder = folder.Mode == SyncFolderMode.VersionDelivery;
        var excluded = new HashSet<string>(folder.ExcludedFiles, StringComparer.OrdinalIgnoreCase);
        string folderPath = folder.Path;

        // Run the directory scan off the UI thread
        var entries = await Task.Run(() =>
        {
            var result = new List<SyncFilterEntry>();
            if (!Directory.Exists(folderPath)) return result;

            foreach (string path in Directory.EnumerateFiles(folderPath, pattern, searchOption)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);

                if (projectFileNames != null && !projectFileNames.Contains(fileName))
                    continue;

                // Version folders exclude by full path; others by name
                bool isExcluded = isVersionFolder
                    ? excluded.Contains(path)
                    : excluded.Contains(fileName);

                result.Add(new SyncFilterEntry
                {
                    FileName = fileName,
                    FilePath = path,
                    IsIncluded = !isExcluded
                });
            }
            return result;
        });

        _entries = new ObservableCollection<SyncFilterEntry>(entries);
        FilterGrid.ItemsSource = _entries;
    }

    /// <summary>
    /// Returns file names that the user unchecked (excluded from sync).
    /// Used for non-version folders.
    /// </summary>
    public List<string> GetExcludedFileNames() =>
        _entries.Where(e => !e.IsIncluded).Select(e => e.FileName).ToList();

    /// <summary>
    /// Returns the full file paths of entries that the user unchecked.
    /// Used for version folders where name-only matching is ambiguous.
    /// </summary>
    public List<string> GetExcludedFilePaths() =>
        _entries.Where(e => !e.IsIncluded).Select(e => e.FilePath).ToList();

    private void OnCheckToggled(object? sender, RoutedEventArgs e)
    {
        if (_suppressToggle) return;
        if (sender is not CheckBox cb || cb.DataContext is not SyncFilterEntry toggled) return;

        var selected = FilterGrid.SelectedItems
            .OfType<SyncFilterEntry>()
            .Where(entry => entry != toggled)
            .ToList();

        if (selected.Count == 0) return;

        bool state = toggled.IsIncluded;
        _suppressToggle = true;
        foreach (var entry in selected)
            entry.IsIncluded = state;
        _suppressToggle = false;
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
}
