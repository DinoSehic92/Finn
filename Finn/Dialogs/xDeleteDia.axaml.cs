using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;
using Finn.Views;

namespace Finn.Dialog;

public partial class xDeleteDia : TemplateWindow
{
    public xDeleteDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;
        Loaded += OnLoaded;

    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;
        ctx.Confirmed = false;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;
        ctx.Confirmed = true;

        this.Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {

        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}