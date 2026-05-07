using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;
using Finn.Views;

namespace Finn.Dialogs;

public partial class xTagDia : Window
{
    public xTagDia()
    {
        InitializeComponent();

        KeyDown += CloseKey!;

    }

    private void OnSetTag(object? sender, RoutedEventArgs e)
    {
        if (TagMenuInput.Text != null)
        {
            if (DataContext is not MainViewModel ctx) return;
            ctx.AddTag(TagMenuInput.Text.ToString());
        }

        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            this.Close();
        else if (e.Key == Key.Enter)
            OnSetTag(sender, e);
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

}