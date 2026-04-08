using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialogs;

public partial class xSharedPushDia : Window
{
    public bool Confirmed { get; private set; }

    public bool PushFiles { get; private set; } = true;
    public bool PushVersions { get; private set; } = true;
    public bool PushFolders { get; private set; } = true;
    public bool PushTodo { get; private set; } = true;
    public bool PushAnnotations { get; private set; } = true;
    public bool PushOtherFiles { get; private set; } = true;
    public bool PushSettings { get; private set; } = true;

    public xSharedPushDia()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public void SetWarning(string warning)
    {
        WarningText.Text = warning;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        PushFiles = IncludeFiles.IsChecked == true;
        PushVersions = IncludeVersions.IsChecked == true;
        PushFolders = IncludeFolders.IsChecked == true;
        PushTodo = IncludeTodo.IsChecked == true;
        PushAnnotations = IncludeAnnotations.IsChecked == true;
        PushOtherFiles = IncludeOtherFiles.IsChecked == true;
        PushSettings = IncludeSettings.IsChecked == true;
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
