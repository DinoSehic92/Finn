using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialogs;

public partial class xShareSettingsDia : Window
{
    public bool Confirmed { get; private set; }
    public bool OneWayShare { get; private set; }

    public xShareSettingsDia()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    /// <summary>
    /// Seeds the radio buttons from the project current sharing mode.
    /// </summary>
    public void SetCurrentMode(bool oneWay)
    {
        OneWayRadio.IsChecked = oneWay;
        CollabRadio.IsChecked = !oneWay;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        OneWayShare = OneWayRadio.IsChecked == true;
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
