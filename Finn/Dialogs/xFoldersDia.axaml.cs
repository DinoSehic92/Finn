using Finn.ViewModels;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Finn.Dialog;

public partial class xFoldersDia : Window
{
    public xFoldersDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

    }

    private void OnAddProject(object sender, RoutedEventArgs e)
    {

    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }

}