using System.ComponentModel;
using Avalonia.Media;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Finn.ViewModels
{
    public class UISettingsViewModel : INotifyPropertyChanged
    {
        // Localized defaults moved here from Finn.Services.UIDefaults
        public static class Defaults
        {
            public static readonly Avalonia.Media.Color DefaultColor1 = Avalonia.Media.Color.Parse("#1F2933");
            public static readonly Avalonia.Media.Color DefaultColor2 = Avalonia.Media.Color.Parse("#0A84FF");
            public static readonly Avalonia.Media.Color DefaultColor3 = Avalonia.Media.Color.Parse("#E9EEF5");
            public static readonly Avalonia.Media.Color DefaultColor4 = Avalonia.Media.Color.Parse("#0066C0");

            public const string DefaultFontName = "Roboto";
            public const int DefaultFontSize = 15;

            public const double DefaultCornerRadius = 10.0;
            public const bool DefaultCornerRadiusVal = true;
            public const bool DefaultShadowVal = false;
            public const bool DefaultShowBorders = false;
            public const bool DefaultDarkMode = true;
            public const int DefaultTreeViewWidth = 300;
        }

        public UISettingsViewModel()
        {
            // default values from localized defaults
            Color1 = Defaults.DefaultColor1;
            Color2 = Defaults.DefaultColor2;
            Color3 = Defaults.DefaultColor3;
            Color4 = Defaults.DefaultColor4;

            CornerRadius = new CornerRadius(Defaults.DefaultCornerRadius);
            CornerRadiusVal = Defaults.DefaultCornerRadiusVal;

            Shadow = BoxShadows.Parse("0 2 6 0 #22000000, 0 8 24 0 #11000000");
            ShadowVal = Defaults.DefaultShadowVal;
            ShowBorders = Defaults.DefaultShowBorders;

            DarkMode = Defaults.DefaultDarkMode;

            Font = Defaults.DefaultFontName;
            FontSize = Defaults.DefaultFontSize;

            ShowIcons = true;
            TrayNote = true;
            TrayCollections = true;
            TrayBookmarks = true;
            TrayRecent = true;
            TrayVersions = false;
            ColorTagDot = false;

            TreeViewWidth = Defaults.DefaultTreeViewWidth;

            // View visibility flags persisted in UI settings
            TreeViewOpen = true;
            CalendarOpen = false;
            TimeSheetOpen = false;
            ShowFolders = false;
            ShowThumbnails = false;
            TrayViewOpen = false;

        }

        private bool treeViewOpen;
        public bool TreeViewOpen { get => treeViewOpen; set { treeViewOpen = value; RaisePropertyChanged(nameof(TreeViewOpen)); } }

        private int treeViewWidth;
        public int TreeViewWidth { get => treeViewWidth; set { treeViewWidth = value; RaisePropertyChanged(nameof(TreeViewWidth)); } }

        private bool calendarOpen;
        public bool CalendarOpen { get => calendarOpen; set { calendarOpen = value; RaisePropertyChanged(nameof(CalendarOpen)); } }

        private bool timeSheetOpen;
        public bool TimeSheetOpen { get => timeSheetOpen; set { timeSheetOpen = value; RaisePropertyChanged(nameof(TimeSheetOpen)); } }

        private bool showFolders;
        public bool ShowFolders { get => showFolders; set { showFolders = value; RaisePropertyChanged(nameof(ShowFolders)); } }

        private bool showThumbnails;
        public bool ShowThumbnails { get => showThumbnails; set { showThumbnails = value; RaisePropertyChanged(nameof(ShowThumbnails)); } }

        private Color color1;
        public Color Color1 { get => color1; set { color1 = value; RaisePropertyChanged(nameof(Color1)); } }

        private Color color2;
        public Color Color2 { get => color2; set { color2 = value; RaisePropertyChanged(nameof(Color2)); } }

        private Color color3;
        public Color Color3 { get => color3; set { color3 = value; RaisePropertyChanged(nameof(Color3)); } }

        private Color color4;
        public Color Color4 { get => color4; set { color4 = value; RaisePropertyChanged(nameof(Color4)); } }

        private bool cornerRadiusVal;
        public bool CornerRadiusVal { get => cornerRadiusVal; set { cornerRadiusVal = value; RaisePropertyChanged(nameof(CornerRadiusVal)); SetCornerRadius(); } }

        private CornerRadius cornerRadius;
        public CornerRadius CornerRadius { get => cornerRadius; set { cornerRadius = value; RaisePropertyChanged(nameof(CornerRadius)); } }

        private BoxShadows shadow;
        public BoxShadows Shadow { get => shadow; set { shadow = value; RaisePropertyChanged(nameof(Shadow)); } }

        private bool shadowVal;
        public bool ShadowVal { get => shadowVal; set { shadowVal = value; RaisePropertyChanged(nameof(ShadowVal)); SetShadow(); } }

        private bool darkMode;
        public bool DarkMode { get => darkMode; set { darkMode = value; RaisePropertyChanged(nameof(DarkMode)); RaisePropertyChanged(nameof(Theme)); } }

        public ThemeVariant Theme => DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;

        private string font = string.Empty;
        public string Font { get => font; set { font = value; RaisePropertyChanged(nameof(Font)); } }

        private int fontSize;
        public int FontSize { get => fontSize; set { fontSize = value; RaisePropertyChanged(nameof(FontSize)); } }

        private bool showIcons;
        public bool ShowIcons { get => showIcons; set { showIcons = value; RaisePropertyChanged(nameof(ShowIcons)); } }

        private bool colorTagDot;
        /// <summary>
        /// When true, color tags are shown as a small colored dot instead of tinting the full row.
        /// </summary>
        public bool ColorTagDot { get => colorTagDot; set { colorTagDot = value; RaisePropertyChanged(nameof(ColorTagDot)); RaisePropertyChanged(nameof(ColorTagRow)); } }

        /// <summary>Inverse of <see cref="ColorTagDot"/> for convenience bindings.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool ColorTagRow => !colorTagDot;

        private bool trayNote;
        public bool TrayNote { get => trayNote; set { trayNote = value; RaisePropertyChanged(nameof(TrayNote)); } }

        private bool trayCollections;
        public bool TrayCollections { get => trayCollections; set { trayCollections = value; RaisePropertyChanged(nameof(TrayCollections)); } }

        private bool trayBookmarks;
        public bool TrayBookmarks { get => trayBookmarks; set { trayBookmarks = value; RaisePropertyChanged(nameof(TrayBookmarks)); } }

        private bool trayRecent;
        public bool TrayRecent { get => trayRecent; set { trayRecent = value; RaisePropertyChanged(nameof(TrayRecent)); } }

        private bool trayDiff;
        public bool TrayDiff { get => trayDiff; set { trayDiff = value; RaisePropertyChanged(nameof(TrayDiff)); } }

        private bool trayVersions;
        public bool TrayVersions { get => trayVersions; set { trayVersions = value; RaisePropertyChanged(nameof(TrayVersions)); } }

        private bool trayViewOpen;
        public bool TrayViewOpen { get => trayViewOpen; set { trayViewOpen = value; RaisePropertyChanged(nameof(TrayViewOpen)); } }

        private bool previewEmbeddedOpen;
        public bool PreviewEmbeddedOpen { get => previewEmbeddedOpen; set { previewEmbeddedOpen = value; RaisePropertyChanged(nameof(PreviewEmbeddedOpen)); } }

        private bool showBorders;
        public bool ShowBorders { get => showBorders; set { showBorders = value; RaisePropertyChanged(nameof(ShowBorders)); SetBorderThickness(); } }

        private Thickness borderThickness;
        public Thickness BorderThickness { get => borderThickness; set { borderThickness = value; RaisePropertyChanged(nameof(BorderThickness)); } }

        private void SetBorderThickness()
        {
            BorderThickness = ShowBorders ? new Thickness(1) : new Thickness(0);
        }

        private void SetCornerRadius()
        {
            if (CornerRadiusVal)
                CornerRadius = new CornerRadius(10);
            else
                CornerRadius = new CornerRadius(0);
        }

        private void SetShadow()
        {
            if (ShadowVal)
                Shadow = BoxShadows.Parse("0 2 6 0 #22000000, 0 8 24 0 #11000000");
            else
                Shadow = new BoxShadows();
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // Convert the runtime UI viewmodel into the lightweight storage DTO
        public Finn.Storage.UIStorage ToStorage()
        {
            return new Finn.Storage.UIStorage
            {
                Color1 = this.Color1.ToString(),
                Color2 = this.Color2.ToString(),
                Color3 = this.Color3.ToString(),
                Color4 = this.Color4.ToString(),
                CornerRadiusVal = this.CornerRadiusVal,
                CornerRadius = this.CornerRadius.TopLeft,
                ShadowVal = this.ShadowVal,
                ShowBorders = this.ShowBorders,
                DarkMode = this.DarkMode,
                Font = this.Font,
                FontSize = this.FontSize,
                TrayNote = this.TrayNote,
                TrayCollections = this.TrayCollections,
                TrayBookmarks = this.TrayBookmarks,
                TrayRecent = this.TrayRecent,
                TrayDiff = this.TrayDiff,
                TrayVersions = this.TrayVersions,
                ShowIcons = this.ShowIcons,
                ColorTagDot = this.ColorTagDot,
                TreeViewOpen = this.TreeViewOpen,
                CalendarOpen = this.CalendarOpen,
                TimeSheetOpen = this.TimeSheetOpen,
                ShowFolders = this.ShowFolders,
                ShowThumbnails = this.ShowThumbnails,
                TrayViewOpen = this.TrayViewOpen,
                TreeViewWidth = this.TreeViewWidth
            };
        }

        // Populate the runtime UI viewmodel from a storage DTO
        public void FromStorage(Finn.Storage.UIStorage ui)
        {
            if (ui == null) return;

            try { if (!string.IsNullOrWhiteSpace(ui.Color1)) this.Color1 = Avalonia.Media.Color.Parse(ui.Color1); } catch { }
            try { if (!string.IsNullOrWhiteSpace(ui.Color2)) this.Color2 = Avalonia.Media.Color.Parse(ui.Color2); } catch { }
            try { if (!string.IsNullOrWhiteSpace(ui.Color3)) this.Color3 = Avalonia.Media.Color.Parse(ui.Color3); } catch { }
            try { if (!string.IsNullOrWhiteSpace(ui.Color4)) this.Color4 = Avalonia.Media.Color.Parse(ui.Color4); } catch { }

            this.CornerRadiusVal = ui.CornerRadiusVal;
            this.CornerRadius = new Avalonia.CornerRadius(ui.CornerRadius);
            this.ShadowVal = ui.ShadowVal;
            this.ShowBorders = ui.ShowBorders;
            this.DarkMode = ui.DarkMode;

            if (!string.IsNullOrWhiteSpace(ui.Font)) this.Font = ui.Font;
            if (ui.FontSize > 0) this.FontSize = ui.FontSize;

            try
            {
                this.TrayNote = ui.TrayNote;
                this.TrayCollections = ui.TrayCollections;
                this.TrayBookmarks = ui.TrayBookmarks;
                this.TrayRecent = ui.TrayRecent;
                this.TrayDiff = ui.TrayDiff;
                this.TrayVersions = ui.TrayVersions;
                this.ShowIcons = ui.ShowIcons;
                this.ColorTagDot = ui.ColorTagDot;

                this.TreeViewOpen = ui.TreeViewOpen;
                this.CalendarOpen = ui.CalendarOpen;
                this.TimeSheetOpen = ui.TimeSheetOpen;
                this.ShowFolders = ui.ShowFolders;
                this.ShowThumbnails = ui.ShowThumbnails;
                this.TrayViewOpen = ui.TrayViewOpen;
                if (ui.TreeViewWidth >= 250 && ui.TreeViewWidth <= 350)
                    this.TreeViewWidth = ui.TreeViewWidth;
            }
            catch
            {
                // ignore
            }
        }

        // Apply theme resources based on current runtime UI values
        public void ApplyTheme()
        {
            var theme = new FluentTheme()
            {
                Palettes =
                {
                    [ThemeVariant.Dark] = new ColorPaletteResources() { RegionColor = this.Color1, Accent = this.Color2 },
                    [ThemeVariant.Light] = new ColorPaletteResources() { RegionColor = this.Color3, Accent = this.Color4 }
                }
            };

            App.Current.Resources = theme.Resources;
            App.Current.RequestedThemeVariant = DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
