using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;
using Finn.Views;

namespace Finn.Dialogs;

public partial class xDeleteDia : Window
{
    public xDeleteDia()
    {
        InitializeComponent();

        KeyDown += CloseKey!;
        Loaded += OnLoaded;

    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is MainViewModel ctx1)
            ctx1.Confirmed = false;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (this.DataContext is MainViewModel ctx2)
        {
            ctx2.Confirmed = true;
            this.Close();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {

        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            this.Close();
        else if (e.Key == Key.Enter)
            OnConfirm(sender, e);
    }

}