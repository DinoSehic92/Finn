using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using Finn.ViewModels;
using Finn.Views;
using System.ComponentModel;

namespace Finn.Dialogs;

public partial class xContentDia : Window
{
    public xContentDia()
    {
        InitializeComponent();

        KeyDown += CloseKey!;

    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }

}