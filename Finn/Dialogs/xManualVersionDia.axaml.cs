using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Finn.Model;
using System;
using System.IO;
using System.Linq;

namespace Finn.Dialogs;

public partial class xManualVersionDia : Window
{
    public bool Confirmed { get; private set; }
    public string? SelectedFilePath { get; private set; }
    public string SelectedLabel { get; private set; } = "NEW";

    public xManualVersionDia()
    {
        InitializeComponent();

        DropZone.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);
        DropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver);

        LabelCombo.SelectedItem = "NEW";
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        bool hasFile = items?.OfType<IStorageFile>().Any() == true;
        if (hasFile)
            DropZone.Classes.Add("DragOver");
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("DragOver");
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Only signal Copy when there is at least one actual file being dragged.
        var items = e.DataTransfer.TryGetFiles();
        bool hasFile = items?.OfType<IStorageFile>().Any() == true;
        e.DragEffects = hasFile ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.Classes.Remove("DragOver");

        var items = e.DataTransfer.TryGetFiles();
        if (items == null) return;

        var first = items.OfType<IStorageFile>().FirstOrDefault();
        if (first == null) return;

        string path = first.Path.LocalPath;
        if (!File.Exists(path)) return;

        SetDroppedFile(path);
    }

    private void SetDroppedFile(string path)
    {
        SelectedFilePath = path;
        string fileName = Path.GetFileName(path);

        DropHint.IsVisible = false;
        FileInfoPanel.IsVisible = true;
        DroppedFileName.Text = fileName;
        DroppedFilePath.Text = path;

        string? detected = TryDetectLabel(fileName);
        if (detected != null)
        {
            LabelCombo.Text = detected;
            AutoDetectHint.Text = "Auto-detected from filename";
        }
        else
        {
            AutoDetectHint.Text = string.Empty;
        }

        AcceptButton.IsEnabled = true;
    }

    /// <summary>
    /// Extracts a version label from a filename using the convention
    /// basename_versionlabel.ext — everything after the last underscore
    /// is used as the label (e.g. "Drawing_RevideringA.pdf" -> "RevideringA").
    /// If the extracted suffix matches a known predefined label (case-insensitive)
    /// that canonical form is returned instead.
    /// Returns null when no underscore separator is found.
    /// </summary>
    private static string? TryDetectLabel(string fileName)
    {
        string nameNoExt = Path.GetFileNameWithoutExtension(fileName);
        int underscoreIdx = nameNoExt.LastIndexOf('_');

        // Must have an underscore that is not the last character.
        if (underscoreIdx < 0 || underscoreIdx == nameNoExt.Length - 1)
            return null;

        string suffix = nameNoExt[(underscoreIdx + 1)..].Trim();
        if (string.IsNullOrEmpty(suffix)) return null;

        // If the suffix matches a known predefined label, return its canonical casing.
        string? known = FileVersionData.VersionLabels
            .FirstOrDefault(l => string.Equals(l, suffix, StringComparison.OrdinalIgnoreCase));

        return known ?? suffix;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        if (SelectedFilePath == null) return;
        SelectedLabel = (LabelCombo.Text ?? LabelCombo.SelectedItem as string ?? "NEW").Trim();
        if (string.IsNullOrEmpty(SelectedLabel)) SelectedLabel = "NEW";
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
