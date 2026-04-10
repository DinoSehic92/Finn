using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System.Linq;

namespace Finn.Dialogs;

public partial class xChangeFolderDia : Window
{
    public bool Confirmed { get; private set; }
    public string SelectedPath { get; private set; } = string.Empty;

    private string _currentPath = string.Empty;

    public xChangeFolderDia()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the dialog with the folder's current directory path.
    /// </summary>
    public void SetCurrentPath(string folderName, string currentPath)
    {
        _currentPath = currentPath;
        PathBox.Text = currentPath;
        HeaderText.Text = $"Change Directory \u2014 {folderName}";
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var storage = StorageProvider;
        var startFolder = !string.IsNullOrEmpty(_currentPath)
            ? await storage.TryGetFolderFromPathAsync(_currentPath)
            : null;

        var result = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Folder",
            AllowMultiple = false,
            SuggestedStartLocation = startFolder
        });

        if (result.Count > 0)
        {
            SelectedPath = result[0].TryGetLocalPath() ?? string.Empty;
            PathBox.Text = SelectedPath;
        }
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(PathBox.Text))
            SelectedPath = PathBox.Text;

        Confirmed = !string.IsNullOrWhiteSpace(SelectedPath);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
