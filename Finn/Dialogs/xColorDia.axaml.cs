using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Finn.Views;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Finn.Storage;
using Finn.Utils;
using Finn.ViewModels;

namespace Finn.Dialogs;

public partial class xColorDia : Window
{
    public xColorDia()
    {
        InitializeComponent();

        //FontCombo.ItemsSource = FontManager.Current.SystemFonts.Select(x => x.Name).ToList();

        FontCombo.ItemsSource = new List<string>() {"Barlow","Fira Sans", "IBM Plex Sans", "Jost", "Lato", "Lexend Deca", "Montserrat", "Nunito", "Open Sans", "Quicksand", "Raleway", "Recursive", "Roboto", "Rosario", "Share Tech", "Source Code Pro", "Ubuntu", "Urbanist", "Work Sans"};

        FontSizeCombo.ItemsSource = new List<int>() { 14, 15, 16 };

        DarkPresetCombo.ItemsSource  = UISettingsViewModel.DarkPresets.Select(p => p.Name).ToList();
        LightPresetCombo.ItemsSource = UISettingsViewModel.LightPresets.Select(p => p.Name).ToList();

        DarkPresetCombo.SelectionChanged  += OnDarkPresetSelected;
        LightPresetCombo.SelectionChanged += OnLightPresetSelected;

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
            string json = JsonHelper.Serialize(ui);
            string file = Path.Combine(savePath, "UISettings.json");
            await File.WriteAllTextAsync(file, json);
        }
        catch
        {
            // ignore save errors
        }

        this.Close();
    }

    public void OnDarkPresetSelected(object? sender, SelectionChangedEventArgs e)
    {
        int idx = DarkPresetCombo.SelectedIndex;
        if (idx < 0 || idx >= UISettingsViewModel.DarkPresets.Count) return;
        if (this.DataContext is not MainViewModel vm) return;

        var preset = UISettingsViewModel.DarkPresets[idx];
        BackgroundColorPickerDark.Color = preset.Background;
        AccentColorPickerDark.Color     = preset.Accent;
        vm.UI.CornerRadiusVal           = preset.Rounded;
        vm.UI.ShadowVal                 = preset.Shadows;
        vm.UI.ShowBorders               = preset.Borders;
    }

    public void OnLightPresetSelected(object? sender, SelectionChangedEventArgs e)
    {
        int idx = LightPresetCombo.SelectedIndex;
        if (idx < 0 || idx >= UISettingsViewModel.LightPresets.Count) return;
        if (this.DataContext is not MainViewModel vm) return;

        var preset = UISettingsViewModel.LightPresets[idx];
        BackgroundColorPickerLight.Color = preset.Background;
        AccentColorPickerLight.Color     = preset.Accent;
        vm.UI.CornerRadiusVal            = preset.Rounded;
        vm.UI.ShadowVal                  = preset.Shadows;
        vm.UI.ShowBorders                = preset.Borders;
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