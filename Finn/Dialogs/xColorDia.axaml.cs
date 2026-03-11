using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Finn.Views;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using Newtonsoft.Json;
using Finn.Storage;
using Finn.ViewModels;

namespace Finn.Dialogs;

public partial class xColorDia : Window
{
    public xColorDia()
    {
        InitializeComponent();

        //FontCombo.ItemsSource = FontManager.Current.SystemFonts.Select(x => x.Name).ToList();

        FontCombo.ItemsSource = new List<string>() {"Barlow","Fira Sans", "IBM Plex Sans", "Jost", "Lato", "Lexend Deca", "Montserrat", "Nunito", "Open Sans", "Quicksand", "Raleway", "Recursive", "Roboto", "Rosario", "Share Tech", "Source Code Pro", "Ubuntu", "Urbanist", "Work Sans"};

        FontSizeCombo.ItemsSource = new List<int>() { 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 };

        KeyDown += CloseKey;
    }

    public void OnClose(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    public async void OnSave(object sender, RoutedEventArgs e)
    {
        // DataContext is MainViewModel (x:DataType specified in xaml)
        if (this.DataContext is not MainViewModel vm) return;

        try
        {
            var savePath = MainViewModel.SavePath;
            if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

            var ui = vm.UI.ToStorage();
            string json = JsonConvert.SerializeObject(ui, Formatting.Indented);
            string file = Path.Combine(savePath, "UISettings.json");
            await File.WriteAllTextAsync(file, json);
        }
        catch
        {
            // ignore save errors
        }

        this.Close();
    }

    public void ResetDark(object sender, RoutedEventArgs e)
    {
        BackgroundColorPickerDark.Color = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultColor1;
        AccentColorPickerDark.Color = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultColor2;
    }

    public void ResetLight(object sender, RoutedEventArgs e)
    {
        BackgroundColorPickerLight.Color = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultColor3;
        AccentColorPickerLight.Color = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultColor4;
    }

    public void ResetFonts(object sender, RoutedEventArgs e)
    {
        FontCombo.SelectedItem = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultFontName;
        FontSizeCombo.SelectedValue = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultFontSize;
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }
}