using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Finn.ViewModels
{
    public class UISettingsViewModel : ObservableObject
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
            public const int DefaultTrayWidth = 300;
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
            TrayOtherFiles = true;
            ColorTagDot = false;
            TrayTodo = true;

            TreeViewWidth = Defaults.DefaultTreeViewWidth;
            TrayWidth = Defaults.DefaultTrayWidth;

            // View visibility flags persisted in UI settings
            ShowActionBar = false;
            PreviewDarkMode = false;
            PreviewDarkModeTint = "None";
            PreviewTintIntensity = 15;
            PreviewLightPaper = "White";
            TreeViewOpen = true;
            CalendarOpen = false;
            TimeSheetOpen = false;
            ShowFolders = false;
            ShowThumbnails = false;
            TrayViewOpen = false;
            FolderWatchEnabled = false;

        }

        private bool treeViewOpen;
        public bool TreeViewOpen { get => treeViewOpen; set { treeViewOpen = value; OnPropertyChanged(nameof(TreeViewOpen)); } }

        private int treeViewWidth;
        public int TreeViewWidth { get => treeViewWidth; set { treeViewWidth = value; OnPropertyChanged(nameof(TreeViewWidth)); } }

        private int trayWidth;
        public int TrayWidth { get => trayWidth; set { trayWidth = value; OnPropertyChanged(nameof(TrayWidth)); } }

        private bool calendarOpen;
        public bool CalendarOpen { get => calendarOpen; set { calendarOpen = value; OnPropertyChanged(nameof(CalendarOpen)); } }

        private bool timeSheetOpen;
        public bool TimeSheetOpen { get => timeSheetOpen; set { timeSheetOpen = value; OnPropertyChanged(nameof(TimeSheetOpen)); } }

        private bool showFolders;
        public bool ShowFolders { get => showFolders; set { showFolders = value; OnPropertyChanged(nameof(ShowFolders)); } }

        private bool showActionBar;
        public bool ShowActionBar { get => showActionBar; set { showActionBar = value; OnPropertyChanged(nameof(ShowActionBar)); } }

        private bool showThumbnails;
        public bool ShowThumbnails { get => showThumbnails; set { showThumbnails = value; OnPropertyChanged(nameof(ShowThumbnails)); } }

        private bool autoCacheNetworkFiles;
        /// <summary>
        /// When true, network files are automatically cached locally before
        /// previewing. Avoids locking server files and speeds up re-opens.
        /// </summary>
        public bool AutoCacheNetworkFiles { get => autoCacheNetworkFiles; set { autoCacheNetworkFiles = value; OnPropertyChanged(nameof(AutoCacheNetworkFiles)); } }

        private bool readBytesMode;
        /// <summary>
        /// When true, PDF files are read entirely into memory before passing
        /// to MuPDF. This avoids holding a file lock on the server but uses
        /// more memory. When false, MuPDF opens the file by path (default).
        /// </summary>
        public bool ReadBytesMode { get => readBytesMode; set { readBytesMode = value; OnPropertyChanged(nameof(ReadBytesMode)); } }

        private bool previewDarkMode;
        public bool PreviewDarkMode { get => previewDarkMode; set { previewDarkMode = value; OnPropertyChanged(nameof(PreviewDarkMode)); } }

        private string previewDarkModeTint = "None";
        public string PreviewDarkModeTint { get => previewDarkModeTint; set { previewDarkModeTint = value ?? "None"; OnPropertyChanged(nameof(PreviewDarkModeTint)); } }

        private int previewTintIntensity = 15;
        public int PreviewTintIntensity { get => previewTintIntensity; set { previewTintIntensity = Math.Clamp(value, 0, 50); OnPropertyChanged(nameof(PreviewTintIntensity)); } }

        private string previewLightPaper = "White";
        public string PreviewLightPaper { get => previewLightPaper; set { previewLightPaper = value ?? "White"; OnPropertyChanged(nameof(PreviewLightPaper)); } }

        private Color color1;
        public Color Color1 { get => color1; set { color1 = value; OnPropertyChanged(nameof(Color1)); } }

        private Color color2;
        public Color Color2 { get => color2; set { color2 = value; OnPropertyChanged(nameof(Color2)); } }

        private Color color3;
        public Color Color3 { get => color3; set { color3 = value; OnPropertyChanged(nameof(Color3)); } }

        private Color color4;
        public Color Color4 { get => color4; set { color4 = value; OnPropertyChanged(nameof(Color4)); } }

        private bool cornerRadiusVal;
        public bool CornerRadiusVal { get => cornerRadiusVal; set { cornerRadiusVal = value; OnPropertyChanged(nameof(CornerRadiusVal)); SetCornerRadius(); } }

        private CornerRadius cornerRadius;
        public CornerRadius CornerRadius { get => cornerRadius; set { cornerRadius = value; OnPropertyChanged(nameof(CornerRadius)); } }

        private BoxShadows shadow;
        public BoxShadows Shadow { get => shadow; set { shadow = value; OnPropertyChanged(nameof(Shadow)); } }

        private bool shadowVal;
        public bool ShadowVal { get => shadowVal; set { shadowVal = value; OnPropertyChanged(nameof(ShadowVal)); SetShadow(); } }

        private bool darkMode;
        public bool DarkMode { get => darkMode; set { darkMode = value; OnPropertyChanged(nameof(DarkMode)); OnPropertyChanged(nameof(Theme)); } }

        public ThemeVariant Theme => DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;

        private string font = string.Empty;
        public string Font { get => font; set { font = value; OnPropertyChanged(nameof(Font)); } }

        private int fontSize;
        public int FontSize { get => fontSize; set { fontSize = value; OnPropertyChanged(nameof(FontSize)); OnPropertyChanged(nameof(FontSizeCompact)); } }

        /// <summary>FontSize - 2, for compact controls like calendar grids and timesheet entries.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int FontSizeCompact => Math.Max(fontSize - 2, 11);

        private bool showIcons;
        public bool ShowIcons { get => showIcons; set { showIcons = value; OnPropertyChanged(nameof(ShowIcons)); } }

        private bool colorTagDot;
        /// <summary>
        /// When true, color tags are shown as a small colored dot instead of tinting the full row.
        /// </summary>
        public bool ColorTagDot { get => colorTagDot; set { colorTagDot = value; OnPropertyChanged(nameof(ColorTagDot)); OnPropertyChanged(nameof(ColorTagRow)); } }

        /// <summary>Inverse of <see cref="ColorTagDot"/> for convenience bindings.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ColorTagRow => !colorTagDot;

        private bool trayNote;
        public bool TrayNote { get => trayNote; set { trayNote = value; OnPropertyChanged(nameof(TrayNote)); } }

        private bool trayCollections;
        public bool TrayCollections { get => trayCollections; set { trayCollections = value; OnPropertyChanged(nameof(TrayCollections)); } }

        private bool trayBookmarks;
        public bool TrayBookmarks { get => trayBookmarks; set { trayBookmarks = value; OnPropertyChanged(nameof(TrayBookmarks)); } }

        private bool trayRecent;
        public bool TrayRecent { get => trayRecent; set { trayRecent = value; OnPropertyChanged(nameof(TrayRecent)); } }

        private bool trayVersions;
        public bool TrayVersions { get => trayVersions; set { trayVersions = value; OnPropertyChanged(nameof(TrayVersions)); } }

        private bool trayOtherFiles;
        public bool TrayOtherFiles { get => trayOtherFiles; set { trayOtherFiles = value; OnPropertyChanged(nameof(TrayOtherFiles)); } }

        private bool trayAnnotations;
        public bool TrayAnnotations { get => trayAnnotations; set { trayAnnotations = value; OnPropertyChanged(nameof(TrayAnnotations)); } }

        private bool trayTodo;
        public bool TrayTodo { get => trayTodo; set { trayTodo = value; OnPropertyChanged(nameof(TrayTodo)); } }

        private bool showClock;
        /// <summary>Auto-toggled based on window height. Not persisted.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool ShowClock { get => showClock; set { showClock = value; OnPropertyChanged(nameof(ShowClock)); } }

        private bool trayViewOpen;
        public bool TrayViewOpen { get => trayViewOpen; set { trayViewOpen = value; OnPropertyChanged(nameof(TrayViewOpen)); } }

        private bool folderWatchEnabled;
        /// <summary>
        /// When true, folder watchers monitor sync folders for changes and
        /// a startup check detects files that changed while the app was closed.
        /// </summary>
        public bool FolderWatchEnabled { get => folderWatchEnabled; set { folderWatchEnabled = value; OnPropertyChanged(nameof(FolderWatchEnabled)); } }

        private bool previewEmbeddedOpen;
        public bool PreviewEmbeddedOpen { get => previewEmbeddedOpen; set { previewEmbeddedOpen = value; OnPropertyChanged(nameof(PreviewEmbeddedOpen)); } }

        private bool showBorders;
        public bool ShowBorders { get => showBorders; set { showBorders = value; OnPropertyChanged(nameof(ShowBorders)); SetBorderThickness(); } }

        private Thickness borderThickness;
        public Thickness BorderThickness { get => borderThickness; set { borderThickness = value; OnPropertyChanged(nameof(BorderThickness)); } }

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
                TrayVersions = this.TrayVersions,
                TrayOtherFiles = this.TrayOtherFiles,
                TrayAnnotations = this.TrayAnnotations,
                    TrayTodo = this.TrayTodo,
                    ShowIcons = this.ShowIcons,
                ColorTagDot = this.ColorTagDot,
                TreeViewOpen = this.TreeViewOpen,
                CalendarOpen = this.CalendarOpen,
                TimeSheetOpen = this.TimeSheetOpen,
                ShowFolders = this.ShowFolders,
                ShowThumbnails = this.ShowThumbnails,
                TrayViewOpen = this.TrayViewOpen,
                ShowActionBar = this.ShowActionBar,
                PreviewDarkMode = this.PreviewDarkMode,
                PreviewDarkModeTint = this.PreviewDarkModeTint,
                PreviewTintIntensity = this.PreviewTintIntensity,
                PreviewLightPaper = this.PreviewLightPaper,
                TreeViewWidth = this.TreeViewWidth,
                TrayWidth = this.TrayWidth,
                ReadBytesMode = this.ReadBytesMode,
                FolderWatchEnabled = this.FolderWatchEnabled,
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
                this.TrayVersions = ui.TrayVersions;
                this.TrayOtherFiles = ui.TrayOtherFiles;
                this.TrayAnnotations = ui.TrayAnnotations;
                this.TrayTodo = ui.TrayTodo;
                this.ShowIcons = ui.ShowIcons;
                this.ColorTagDot = ui.ColorTagDot;

                this.TreeViewOpen = ui.TreeViewOpen;
                this.CalendarOpen = ui.CalendarOpen;
                this.TimeSheetOpen = ui.TimeSheetOpen;
                this.ShowFolders = ui.ShowFolders;
                this.ShowThumbnails = ui.ShowThumbnails;
                this.TrayViewOpen = ui.TrayViewOpen;
                this.ShowActionBar = ui.ShowActionBar;
                this.PreviewDarkMode = ui.PreviewDarkMode;
                if (!string.IsNullOrWhiteSpace(ui.PreviewDarkModeTint))
                    this.PreviewDarkModeTint = ui.PreviewDarkModeTint;
                this.PreviewTintIntensity = ui.PreviewTintIntensity;
                if (!string.IsNullOrWhiteSpace(ui.PreviewLightPaper))
                    this.PreviewLightPaper = ui.PreviewLightPaper;
                if (ui.TreeViewWidth >= 250 && ui.TreeViewWidth <= 350)
                    this.TreeViewWidth = ui.TreeViewWidth;
                if (ui.TrayWidth >= 250 && ui.TrayWidth <= 400)
                    this.TrayWidth = ui.TrayWidth;
                this.ReadBytesMode = ui.ReadBytesMode;
                this.FolderWatchEnabled = ui.FolderWatchEnabled;
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
