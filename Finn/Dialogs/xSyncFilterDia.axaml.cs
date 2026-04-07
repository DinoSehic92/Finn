using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Finn.Dialogs;

public partial class xSyncFilterDia : Window
{
    public bool Confirmed { get; private set; }

    private ObservableCollection<SyncFilterEntry> _entries = [];

    public xSyncFilterDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;
    }

    /// <summary>
    /// Populates the dialog with all files on disk for the given folder.
    /// Files in <see cref="FolderData.ExcludedFiles"/> start unchecked.
    /// </summary>
    public void SetFolder(FolderData folder, string pattern = "*.pdf", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        HeaderText.Text = $"Synced Files \u2014 {folder.Name}";

        var excluded = new HashSet<string>(folder.ExcludedFiles, StringComparer.OrdinalIgnoreCase);
        _entries = [];

        if (folder.ExistsOnDisk())
        {
            foreach (string path in Directory.EnumerateFiles(folder.Path, pattern, searchOption)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string fileName = Path.GetFileNameWithoutExtension(path);
                _entries.Add(new SyncFilterEntry
                {
                    FileName = fileName,
                    FilePath = path,
                    IsIncluded = !excluded.Contains(fileName)
                });
            }
        }

        FilterGrid.ItemsSource = _entries;
    }

    /// <summary>
    /// Returns file names that the user unchecked (excluded from sync).
    /// </summary>
    public List<string> GetExcludedFileNames() =>
        _entries.Where(e => !e.IsIncluded).Select(e => e.FileName).ToList();

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
