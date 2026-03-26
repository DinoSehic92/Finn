using Finn.ViewModels;
using Finn.Views;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace Finn.Dialogs;

public partial class xCloseDia : Window
{
    private MainWindow? mainWindow;

    public xCloseDia()
    {
        InitializeComponent();
    }

    public void SetMainWindow(MainWindow mainW)
    {
        mainWindow = mainW;
    }

    public async void SaveBeforeClose(object? sender, RoutedEventArgs args)
    {
        if (this.DataContext is not MainViewModel ctx) return;

        await ctx.SaveFileAuto();

        OnLeave(null, null);
    }

    public void OnLeave(object? sender, RoutedEventArgs? args)
    {
        if (mainWindow == null) return;
        mainWindow.ConfirmLeave = false;
        mainWindow.Close();
    }

    public void OnCancel(object? sender, RoutedEventArgs? args)
    {
        Close();
    }
}