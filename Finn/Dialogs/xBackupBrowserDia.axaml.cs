using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Finn.Dialogs;

public partial class xBackupBrowserDia : Window
{
    public bool Confirmed { get; private set; }
    public string? SelectedBackupPath { get; private set; }

    private readonly List<(string Path, string Display)> _backups = [];

    public xBackupBrowserDia()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        BackupList.SelectionChanged += (_, _) =>
        {
            RestoreButton.IsEnabled = BackupList.SelectedIndex >= 0;
        };
    }

    public void SetBackups(string backupDir, string projectName)
    {
        _backups.Clear();

        if (!Directory.Exists(backupDir))
        {
            SubHeaderText.Text = "No backups found.";
            return;
        }

        string sanitized = SanitizeFileName(projectName);
        var files = Directory.GetFiles(backupDir, $"{sanitized}-backup-*.json")
            .OrderByDescending(f => f)
            .ToList();

        if (files.Count == 0)
        {
            SubHeaderText.Text = "No backups found for this project.";
            return;
        }

        SubHeaderText.Text = $"{files.Count} backup(s) found for \"{projectName}\"";

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            string display = $"{info.LastWriteTime:g}  —  {info.Length / 1024} KB";
            _backups.Add((file, display));
        }

        BackupList.ItemsSource = _backups.Select(b => b.Display).ToList();
    }

    private void OnRestore(object? sender, RoutedEventArgs e)
    {
        if (BackupList.SelectedIndex >= 0 && BackupList.SelectedIndex < _backups.Count)
        {
            SelectedBackupPath = _backups[BackupList.SelectedIndex].Path;
            Confirmed = true;
        }
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
