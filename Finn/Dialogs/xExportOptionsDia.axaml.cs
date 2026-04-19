using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;

namespace Finn.Dialogs;

public partial class xExportOptionsDia : Window
{
    public bool Confirmed { get; private set; }

    public MainViewModel.ExportProjectOptions Options { get; private set; } = new();

    public xExportOptionsDia()
    {
        InitializeComponent();
        KeyDown += CloseKey;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        Options = new MainViewModel.ExportProjectOptions(
            IncludeGroupsCheckBox.IsChecked == true,
            IncludeAttachedFilesCheckBox.IsChecked == true,
            IncludeOtherFilesCheckBox.IsChecked == true);
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void CloseKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        else if (e.Key == Key.Enter)
            OnAccept(sender, e);
    }
}
