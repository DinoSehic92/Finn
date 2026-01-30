using Avalonia.Controls;
using Avalonia.Input;
using Finn.Model;
using Finn.ViewModels;
using Finn.Views;
using System.ComponentModel;

namespace Finn.Dialog;

public partial class xIndexDia : TemplateWindow
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