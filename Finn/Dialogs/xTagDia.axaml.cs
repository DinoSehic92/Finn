using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialog;

public partial class xTagDia : Window
{
    public xTagDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

    }

    private void OnSetTag(object sender, RoutedEventArgs e)
    {
        if (TagMenuInput.Text != null)
        {
            MainViewModel ctx = (MainViewModel)this.DataContext;
            ctx.AddTag(TagMenuInput.Text.ToString());
        }

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