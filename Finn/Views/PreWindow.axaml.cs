using Avalonia.Controls;
using Avalonia.Input;
using Finn.ViewModels;
using System.Diagnostics;

namespace Finn.Views;

public partial class PreWindow : Window
{
    public PreWindow()
    {
        InitializeComponent();

        KeyDown += OnWindowKeyDown;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;

        if (this.DataContext is MainViewModel ctx && ctx.PreviewWindowOpen)
        {
            ctx.PreviewWindowOpen = false;
        }

        e.Cancel = false;
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
            return;
        }

        if (this.DataContext is not MainViewModel ctx) return;

        // Forward Left/Right arrow navigation at window level so page switching
        // works regardless of which child element currently holds focus.
        if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.None)
        {
            if (ctx.PreviewVM.PrevPageCommand.CanExecute(false))
                ctx.PreviewVM.PrevPageCommand.Execute(false);
            e.Handled = true;
        }
        else if (e.Key == Key.Right && e.KeyModifiers == KeyModifiers.None)
        {
            if (ctx.PreviewVM.NextPageCommand.CanExecute(false))
                ctx.PreviewVM.NextPageCommand.Execute(false);
            e.Handled = true;
        }
    }

}