using Avalonia.Controls;
using Avalonia.Input;
using Finn.ViewModels;
using System.Diagnostics;

namespace Finn.Views;

public partial class PreWindow : TemplateWindow
{
    public PreWindow()
    {
        InitializeComponent();

        KeyDown += CloseKey;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {

        e.Cancel = true;


        MainViewModel ctx = (MainViewModel)this.DataContext;

        if (ctx.PreviewWindowOpen)
        {
            ctx.PreviewWindowOpen = false;
        }

        e.Cancel = false;
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}