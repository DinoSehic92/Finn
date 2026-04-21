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

        var fontCombo = this.FindControl<ComboBox>("FontCombo");
        var fontSizeCombo = this.FindControl<ComboBox>("FontSizeCombo");

        if (fontCombo != null) fontCombo.ItemsSource = new List<string>() {"Barlow","Fira Sans", "IBM Plex Sans", "Jost", "Lato", "Lexend Deca", "Montserrat", "Nunito", "Open Sans", "Quicksand", "Raleway", "Recursive", "Roboto", "Rosario", "Share Tech", "Source Code Pro", "Ubuntu", "Urbanist", "Work Sans"};
        if (fontSizeCombo != null) fontSizeCombo.ItemsSource = new List<int>() { 14, 15, 16 };

        var darkCombo = this.FindControl<ComboBox>("DarkPresetCombo");
        if (darkCombo != null)
        {
            darkCombo.ItemsSource  = UISettingsViewModel.DarkPresets.Select(p => p.Name).ToList();
            darkCombo.SelectionChanged  += OnDarkPresetSelected;
        }

        var lightCombo = this.FindControl<ComboBox>("LightPresetCombo");
        if (lightCombo != null)
        {
            lightCombo.ItemsSource = UISettingsViewModel.LightPresets.Select(p => p.Name).ToList();
            lightCombo.SelectionChanged += OnLightPresetSelected;
        }

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
        var darkCombo = this.FindControl<ComboBox>("DarkPresetCombo");
        if (darkCombo == null) return;

        int idx = darkCombo.SelectedIndex;
        if (idx < 0 || idx >= UISettingsViewModel.DarkPresets.Count) return;
        if (this.DataContext is not MainViewModel vm) return;

        var preset = UISettingsViewModel.DarkPresets[idx];

        var bgPicker     = this.FindControl<ColorPicker>("BackgroundColorPickerDark");
        var accentPicker = this.FindControl<ColorPicker>("AccentColorPickerDark");

        if (bgPicker != null)     bgPicker.Color     = preset.Background;
        if (accentPicker != null) accentPicker.Color = preset.Accent;

        vm.UI.CornerRadiusVal = preset.Rounded;
        vm.UI.ShadowVal       = preset.Shadows;
        vm.UI.ShowBorders     = preset.Borders;

        vm.UI.DarkTextColorEnabled   = preset.TextColor.HasValue;
        if (preset.TextColor.HasValue)   vm.UI.DarkTextColor   = preset.TextColor.Value;
        vm.UI.DarkPanelColorEnabled  = preset.PanelBackground.HasValue;
        if (preset.PanelBackground.HasValue) vm.UI.DarkPanelColor  = preset.PanelBackground.Value;
        vm.UI.DarkBorderColorEnabled = preset.BorderColor.HasValue;
        if (preset.BorderColor.HasValue) vm.UI.DarkBorderColor = preset.BorderColor.Value;

        vm.UI.ApplyTheme();
    }

    public void OnLightPresetSelected(object? sender, SelectionChangedEventArgs e)
    {
        var lightCombo = this.FindControl<ComboBox>("LightPresetCombo");
        if (lightCombo == null) return;

        int idx = lightCombo.SelectedIndex;
        if (idx < 0 || idx >= UISettingsViewModel.LightPresets.Count) return;
        if (this.DataContext is not MainViewModel vm) return;

        var preset = UISettingsViewModel.LightPresets[idx];

        var bgPicker     = this.FindControl<ColorPicker>("BackgroundColorPickerLight");
        var accentPicker = this.FindControl<ColorPicker>("AccentColorPickerLight");

        if (bgPicker != null)     bgPicker.Color     = preset.Background;
        if (accentPicker != null) accentPicker.Color = preset.Accent;

        vm.UI.CornerRadiusVal = preset.Rounded;
        vm.UI.ShadowVal       = preset.Shadows;
        vm.UI.ShowBorders     = preset.Borders;

        vm.UI.LightTextColorEnabled   = preset.TextColor.HasValue;
        if (preset.TextColor.HasValue)   vm.UI.LightTextColor   = preset.TextColor.Value;
        vm.UI.LightPanelColorEnabled  = preset.PanelBackground.HasValue;
        if (preset.PanelBackground.HasValue) vm.UI.LightPanelColor  = preset.PanelBackground.Value;
        vm.UI.LightBorderColorEnabled = preset.BorderColor.HasValue;
        if (preset.BorderColor.HasValue) vm.UI.LightBorderColor = preset.BorderColor.Value;

        vm.UI.ApplyTheme();
    }

    public void ResetFonts(object sender, RoutedEventArgs e)
    {
        var fontCombo = this.FindControl<ComboBox>("FontCombo");
        if (fontCombo != null) fontCombo.SelectedItem = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultFontName;

        var fontSizeCombo = this.FindControl<ComboBox>("FontSizeCombo");
        if (fontSizeCombo != null) fontSizeCombo.SelectedValue = Finn.ViewModels.UISettingsViewModel.Defaults.DefaultFontSize;
    }

    private void CloseKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            this.Close();
        }
    }
}