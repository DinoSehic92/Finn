using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.Model;
using Finn.ViewModels;
using Finn.Views;

namespace Finn.Dialog;

public partial class xPlaceholderDia : TemplateWindow
{
    public xPlaceholderDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

    }

    private void OnAddPlaceholder(object sender, RoutedEventArgs e)
    {
        if (NewFileName.Text != null)
        {
            MainViewModel ctx = (MainViewModel)this.DataContext;
            ctx.AddPlaceholderFile(NewFileName.Text);
            
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