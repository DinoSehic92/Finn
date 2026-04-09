using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.IO;
using System.Linq;

namespace Finn.Dialogs;

public partial class xReplaceDia : Window
{
    public bool Accepted { get; private set; }
    public string NewPath => PathInput.Text?.Trim() ?? string.Empty;

    public xReplaceDia()
    {
        InitializeComponent();

        DropZone.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        DropZone.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        DropZone.AddHandler(DragDrop.DropEvent, OnDrop);

        KeyDown += OnKeyDown;
    }

    public void SetCurrentPath(string path)
    {
        PathInput.Text = path;
        PathInput.CaretIndex = path.Length;
    }

    public bool FileExists { get; private set; }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        string path = PathInput.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(path)
            && path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            Accepted = true;
            FileExists = File.Exists(path);
            Close();
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e)
    {
        Accepted = false;
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            OnClose(sender, e);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.TryGetFiles();
        if (items != null && items.Any(i => i is IStorageFile &&
            Path.GetExtension(i.Path.LocalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            DropZone.Classes.Add("DragOver");
        }
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

        var pdf = items.FirstOrDefault(i => i is IStorageFile &&
            Path.GetExtension(i.Path.LocalPath).Equals(".pdf", StringComparison.OrdinalIgnoreCase));

        if (pdf != null)
        {
            PathInput.Text = pdf.Path.LocalPath;
        }
    }
}
