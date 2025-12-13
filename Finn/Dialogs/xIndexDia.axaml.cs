using Finn.Model;
using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using System.ComponentModel;

namespace Finn.Dialog;

public partial class xIndexDia : Window
{
    public xIndexDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}