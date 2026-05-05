using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Finn.ViewModels;
using Finn.Views;

namespace Finn.Dialogs;

public partial class xRenameDia : Window
{
    public xRenameDia()
    {
        InitializeComponent();

        KeyDown += CloseKey;

    }

    public void SetCurrentName(string name)
    {
        NewNameInput.Text = name;
    }

    private async void AcceptRename(object sender, RoutedEventArgs e)
    {
        MainViewModel ctx = (MainViewModel)this.DataContext;
        var result = ctx.RenameOriginal(NewNameInput.Text?.ToString() ?? string.Empty);
        if (!result.Success)
        {
            await ctx.OpenMessageDia(this, result.Message);
            return;
        }

        try
        {
            await ctx.SaveFileAuto();
        }
        catch (Exception ex)
        {
            Utils.ErrorLogger.Log(ex, nameof(AcceptRename));
            await ctx.OpenMessageDia(this, $"Could not save changes after rename. {ex.Message}");
            return;
        }

        this.Close();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            this.Close();
        else if (e.Key == Key.Enter)
            AcceptRename(sender, e);
    }

}