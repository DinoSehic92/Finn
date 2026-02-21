using System;
using System.ComponentModel;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Threading.Tasks;
using Finn.ViewModels;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Media;
using Avalonia;
using Newtonsoft.Json;

namespace Finn.Services
{
    public class UIService : IUIService
    {
        private MainViewModel? _vm;
        private CalendarService? _calendarService;

        public UIService()
        {

        }

        public void Initialize(object viewModel)
        {
            if (viewModel is not MainViewModel vm) return;

            _vm = vm;

            // Attempt to migrate/load UI settings from separate file. Fire-and-forget.
            _ = LoadOrCreateUISettingsAsync();

            // Apply current UI settings immediately
            ApplyThemeAndBorders();

            // Subscribe to future UI changes
            vm.UI.PropertyChanged += OnUIChanged;

            // Initialize calendar persistence service (give service the calendar VM and save path)
            _calendarService = new CalendarService();
            try
            {
                _calendarService.Initialize(vm.Calendar, vm.Storage.General.SavePath);
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "UIService.Initialize.CalendarService");
            }
        }

        private async Task LoadOrCreateUISettingsAsync()
        {
            if (_vm == null) return;

            try
            {
                var savePath = _vm.Storage.General.SavePath;
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

                string file = Path.Combine(savePath, "UISettings.json");

                if (File.Exists(file))
                {
                    // Load existing UI settings and apply to in-memory Storage.General
                    string json = await File.ReadAllTextAsync(file);
                    var ui = JsonConvert.DeserializeObject<UISettings>(json);
                    if (ui != null)
                    {
                        // Map settings into the runtime UI viewmodel
                        try { if (!string.IsNullOrWhiteSpace(ui.Color1)) _vm.UI.Color1 = Avalonia.Media.Color.Parse(ui.Color1); } catch { }
                        try { if (!string.IsNullOrWhiteSpace(ui.Color2)) _vm.UI.Color2 = Avalonia.Media.Color.Parse(ui.Color2); } catch { }
                        try { if (!string.IsNullOrWhiteSpace(ui.Color3)) _vm.UI.Color3 = Avalonia.Media.Color.Parse(ui.Color3); } catch { }
                        try { if (!string.IsNullOrWhiteSpace(ui.Color4)) _vm.UI.Color4 = Avalonia.Media.Color.Parse(ui.Color4); } catch { }

                        _vm.UI.CornerRadiusVal = ui.CornerRadiusVal;
                        _vm.UI.CornerRadius = new Avalonia.CornerRadius(ui.CornerRadius);
                        _vm.UI.ShadowVal = ui.ShadowVal;
                        _vm.UI.DarkMode = ui.DarkMode;
                        if (!string.IsNullOrWhiteSpace(ui.Font)) _vm.UI.Font = ui.Font;
                        if (ui.FontSize > 0) _vm.UI.FontSize = ui.FontSize;

                        // Populate the runtime UI viewmodel with the persisted flags.
                        // These flags are now part of the UI layer, not StoreData.
                        try
                        {
                            _vm.UI.TrayNote = ui.TrayNote;
                            _vm.UI.TrayCollections = ui.TrayCollections;
                            _vm.UI.TrayBookmarks = ui.TrayBookmarks;
                            _vm.UI.TrayRecent = ui.TrayRecent;
                            _vm.UI.ShowIcons = ui.ShowIcons;
                            // migrated visibility flags
                            _vm.UI.TreeViewOpen = ui.TreeViewOpen;
                            _vm.UI.CalendarOpen = ui.CalendarOpen;
                            _vm.UI.TimeSheetOpen = ui.TimeSheetOpen;
                            _vm.UI.ShowFolders = ui.ShowFolders;
                            _vm.UI.ShowThumbnails = ui.ShowThumbnails;
                            _vm.UI.PreviewEmbeddedOpen = ui.PreviewEmbeddedOpen;
                            _vm.UI.TrayViewOpen = ui.TrayViewOpen;

                        }
                        catch
                        {
                            // ignore
                        }

                        // Apply loaded settings to UI
                        ApplyThemeAndBorders();
                    }
                }
                else
                {
                    // No UI settings file yet — create one from current Storage.General
                    await SaveUISettingsAsync();
                }
            }
            catch
            {
                // Swallow IO/parse errors — do not crash initialization
            }
        }

        private void ApplyThemeAndBorders()
        {
            if (_vm == null) return;

            // Apply colors and border settings using existing viewmodel helpers
            SetWindowColors();
        }

        private async void OnUIChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_vm == null) return;

            // If color or theme related values changed, apply resources
            if (e.PropertyName == "Color1" || e.PropertyName == "Color2" || e.PropertyName == "Color3" || e.PropertyName == "Color4" || e.PropertyName == "DarkMode")
            {
                SetWindowColors();
            }

            // Persist UI-only settings to disk (fire-and-forget).
            // Do NOT call SaveFileAuto() here — saving the full Projects.json on UI changes
            // can overwrite the user's project data if it hasn't been loaded yet.
            try
            {
                await SaveUISettingsAsync();
            }
            catch
            {
                // ignore save errors here — do not crash UI thread
            }
        }

        // Storage.General no longer owns tray/show flags; persistence is driven from UI.PropertyChanged

        private async Task SaveUISettingsAsync()
        {
            if (_vm == null) return;

            try
            {
                var savePath = _vm.Storage.General.SavePath;
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                var ui = new UISettings
                {
                    Color1 = _vm.UI.Color1.ToString(),
                    Color2 = _vm.UI.Color2.ToString(),
                    Color3 = _vm.UI.Color3.ToString(),
                    Color4 = _vm.UI.Color4.ToString(),
                    CornerRadiusVal = _vm.UI.CornerRadiusVal,
                    CornerRadius = _vm.UI.CornerRadius.TopLeft,
                    ShadowVal = _vm.UI.ShadowVal,
                    DarkMode = _vm.UI.DarkMode,
                    Font = _vm.UI.Font,
                    FontSize = _vm.UI.FontSize
                };

                // Include the persistent tray/show flags from the runtime UI viewmodel
                try
                {
                    ui.TrayNote = _vm.UI.TrayNote;
                    ui.TrayCollections = _vm.UI.TrayCollections;
                    ui.TrayBookmarks = _vm.UI.TrayBookmarks;
                    ui.TrayRecent = _vm.UI.TrayRecent;
                    ui.ShowIcons = _vm.UI.ShowIcons;
                    // migrated visibility flags
                    ui.TreeViewOpen = _vm.UI.TreeViewOpen;
                    ui.CalendarOpen = _vm.UI.CalendarOpen;
                    ui.TimeSheetOpen = _vm.UI.TimeSheetOpen;
                    ui.ShowFolders = _vm.UI.ShowFolders;
                    ui.ShowThumbnails = _vm.UI.ShowThumbnails;
                    ui.TrayViewOpen = _vm.UI.TrayViewOpen;
                    ui.PreviewEmbeddedOpen = _vm.UI.PreviewEmbeddedOpen;
                }
                catch
                {
                    // ignore - UI may not be populated yet in some call paths
                }

                string json = JsonConvert.SerializeObject(ui, Formatting.Indented);
                string file = Path.Combine(savePath, "UISettings.json");
                await File.WriteAllTextAsync(file, json);
            }
            catch
            {
                // Swallow IO/serialization errors here — do not crash UI thread
            }
        }

        // Move theme application here — UIService owns application-wide theming
        public void SetWindowColors()
        {
            if (_vm == null) return;

            var theme = new FluentTheme()
            {
                Palettes =
                {
                    [ThemeVariant.Dark] = new ColorPaletteResources() { RegionColor = _vm.UI.Color1, Accent = _vm.UI.Color2 },
                    [ThemeVariant.Light] = new ColorPaletteResources() { RegionColor = _vm.UI.Color3, Accent = _vm.UI.Color4 }
                }
            };

            App.Current.Resources = theme.Resources;
        }

        public void SetWindowBorders()
        {
            // Borders and corner radius are applied via bindings to the UI viewmodel in XAML.
            // This method remains for compatibility and future runtime work.
        }
    }
}
