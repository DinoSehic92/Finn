using Finn.ViewModels;
using Avalonia.Controls;
using Finn.Dialog;
using System.ComponentModel;
using System.Diagnostics;


namespace Finn.Views;

public partial class MainWindow : TemplateWindow, INotifyPropertyChanged
{

    public bool confirmLeave = true;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (confirmLeave)
        {
            e.Cancel = true;
            OpenClosingDia();
        }
        else
        {
            MainViewModel ctx = (MainViewModel)this.DataContext;

            if (ctx.PreviewWindowOpen)
            {
                ctx.PreviewWindowOpen = false;
            }

            e.Cancel = false;
        }
    }

    public void OpenClosingDia()
    {

        var window = new xCloseDia()
        {
            DataContext = (MainViewModel)this.DataContext
        };

        window.SetMainWindow(this);

        window.RequestedThemeVariant = this.ActualThemeVariant;
        window.ShowDialog(this);
    }


}