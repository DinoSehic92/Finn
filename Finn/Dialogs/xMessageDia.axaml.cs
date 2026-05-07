using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Views;

namespace Finn.Dialogs;

public partial class xMessageDia : Window
{
    public xMessageDia()
    {
        InitializeComponent();

        KeyDown += CloseKey!;

    }

    public void SetMessage(string message)
    {
        MessageLabel.Text = message;
    }

    private void OnClose(object sender, RoutedEventArgs e)
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