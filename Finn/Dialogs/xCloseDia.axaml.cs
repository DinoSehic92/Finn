using Finn.ViewModels;
using Finn.Views;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace Finn.Dialog;

public partial class xCloseDia : Window
{
    private MainWindow mainWindow;

    public xCloseDia()
    {
        InitializeComponent();
    }

    public void SetMainWindow(MainWindow mainW)
    {
        mainWindow = mainW;
    }

    public async void SaveBeforeClose(object sender, RoutedEventArgs args)
    {

        MainViewModel ctx = (MainViewModel)this.DataContext;

        await ctx.SaveFileAuto();

        OnLeave(null, null);

    }

    public void OnLeave(object sender, RoutedEventArgs args)
    {
        mainWindow.confirmLeave = false;
        mainWindow.Close();
    }


    public void OnCancel(object sender, RoutedEventArgs args)
    {
        Close();
    }


}