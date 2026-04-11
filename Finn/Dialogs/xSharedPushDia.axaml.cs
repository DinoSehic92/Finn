using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialogs;

public partial class xSharedPushDia : Window
{
    public bool Confirmed { get; private set; }

    public bool OneWayShare { get; private set; }

    public xSharedPushDia()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    /// <summary>
    /// Initializes the one-way sharing checkbox from the current project state.
    /// Call after construction so the checkbox reflects the persisted value.
    /// </summary>
    public void SetOneWayShare(bool currentValue)
    {
        OneWayShareCheckBox.IsChecked = currentValue;
    }

    public void SetWarning(string warning)
    {
        WarningText.Text = warning;
    }

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        OneWayShare = OneWayShareCheckBox.IsChecked == true;
        Confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
