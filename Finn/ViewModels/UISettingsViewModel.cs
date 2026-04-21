using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Finn.ViewModels
{
/// <summary>A complete style preset: colours, corner rounding, shadow depth and border visibility.</summary>
public record ThemePreset(
    string Name,
    Color  Background,
    Color  Accent,
    bool   Rounded  = true,
    bool   Shadows  = false,
    bool   Borders  = false);

public class UISettingsViewModel : ObservableObject
{
    // ── 10 dark presets ─────────────────────────────────────────────────────────
    // Each belongs to a different colour family and carries tuned style flags.
    public static readonly IReadOnlyList<ThemePreset> DarkPresets = new[]
    {
        //                              Background   Accent       R      S      B
        new ThemePreset("Default",     Color.Parse("#1F2933"), Color.Parse("#0A84FF"), true,  false, false), // blue-charcoal  + bright blue
        new ThemePreset("Nord",        Color.Parse("#2E3440"), Color.Parse("#88C0D0"), true,  false, false), // cool slate     + frost teal
        new ThemePreset("Midnight",    Color.Parse("#0D1117"), Color.Parse("#58A6FF"), false, false, false), // near-black     + sky blue   (flat/minimal)
        new ThemePreset("Tokyo Night", Color.Parse("#1A1B2E"), Color.Parse("#7AA2F7"), true,  true,  false), // deep navy      + soft blue  (layered depth)
        new ThemePreset("Dracula",     Color.Parse("#282A36"), Color.Parse("#BD93F9"), true,  false, false), // gray-purple    + violet
        new ThemePreset("Forest",      Color.Parse("#343434"), Color.Parse("#498205"), false, false, false), // neutral grey   + earthy green (flat/grounded)
        new ThemePreset("Coffee",      Color.Parse("#2B1D0E"), Color.Parse("#D4A843"), true,  true,  false), // dark espresso  + warm gold
        new ThemePreset("Ember",       Color.Parse("#1C1410"), Color.Parse("#E07B39"), true,  true,  false), // charred wood   + orange flame
        new ThemePreset("Terminal",    Color.Parse("#0C0C0C"), Color.Parse("#00FF88"), false, false, true),  // true black     + mint green (borders fit terminal grid)
        new ThemePreset("Rosé",        Color.Parse("#201A1E"), Color.Parse("#F28FAD"), true,  true,  false), // muted dark     + soft pink
    };

    // ── 10 light presets ────────────────────────────────────────────────────────
    // Shadows/borders are calibrated to the amount of tint — paler backgrounds
    // get more visual structure so buttons remain clearly defined.
    public static readonly IReadOnlyList<ThemePreset> LightPresets = new[]
    {
        //                             Background   Accent       R      S      B
        new ThemePreset("Default",   Color.Parse("#E9EEF5"), Color.Parse("#0066C0"), true,  true,  false), // cool blue-gray  — tinted enough; shadows add depth
        new ThemePreset("Sage",      Color.Parse("#E4EDE4"), Color.Parse("#2D6A4F"), true,  false, false), // soft green      — tint carries the visibility
        new ThemePreset("Lavender",  Color.Parse("#EDE8F5"), Color.Parse("#6B21A8"), true,  true,  false), // purple tint     — shadows bring focus
        new ThemePreset("Blossom",   Color.Parse("#FCE8EE"), Color.Parse("#B5174B"), true,  true,  false), // warm rose       — shadows needed (pale bg)
        new ThemePreset("Ocean",     Color.Parse("#E0F4F4"), Color.Parse("#00838F"), true,  false, false), // light teal      — tint is clear enough
        new ThemePreset("Nordic",    Color.Parse("#DCE4EE"), Color.Parse("#1A237E"), true,  true,  false), // strong cool blue — darker tint, shadows add polish
        new ThemePreset("Dusk",      Color.Parse("#F0E6D6"), Color.Parse("#7B3F00"), true,  true,  false), // warm peach      — pale; shadows lift elements
        new ThemePreset("Ivory",     Color.Parse("#FEFCF0"), Color.Parse("#C2410C"), true,  true,  false), // near-white warm — shadows essential on pale bg
        new ThemePreset("Paper",     Color.Parse("#F5EFE4"), Color.Parse("#8B4513"), false, false, true),  // parchment       — flat + borders = classic document
        new ThemePreset("Mineral",   Color.Parse("#E3E9E6"), Color.Parse("#37474F"), false, false, true),  // muted gray-green — flat + borders = structured/corporate
    };

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

            ShowIcons = false;
            TrayNote = true;
            TrayCollections = true;
            TrayBookmarks = false;
            TrayRecent = true;
            TrayVersions = false;
            TrayOtherFiles = false;
            TrayAnnotations = false;
            ColorTagDot = true;
            TrayTodo = true;

            TreeViewWidth = Defaults.DefaultTreeViewWidth;
            TrayWidth = Defaults.DefaultTrayWidth;

            // View visibility flags persisted in UI settings
            ShowActionBar = true;
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
            AlternatingRowShading = false;
            SuperuserMode = false;

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

        private bool alternatingRowShading;
        /// <summary>
        /// When true, DataGrid rows use alternating background shading for readability.
        /// </summary>
        public bool AlternatingRowShading { get => alternatingRowShading; set { alternatingRowShading = value; OnPropertyChanged(nameof(AlternatingRowShading)); } }

        private bool superuserMode;
        /// <summary>
        /// When true, advanced features (shared projects, versions, folders,
        /// calendar, todo, metadata workers, etc.) are visible in the UI.
        /// When false, only core file-management and preview features are shown.
        /// </summary>
        public bool SuperuserMode { get => superuserMode; set { superuserMode = value; OnPropertyChanged(nameof(SuperuserMode)); } }

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
                AlternatingRowShading = this.AlternatingRowShading,
                SuperuserMode = this.SuperuserMode,
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
                this.AlternatingRowShading = ui.AlternatingRowShading;
                this.SuperuserMode = ui.SuperuserMode;
            }
            catch
            {
                // ignore
            }
        }

        public void ApplyTheme()
        {
            if (App.Current is null) return;

            var theme = new FluentTheme
            {
                Palettes =
                {
                    [ThemeVariant.Dark] = new ColorPaletteResources { RegionColor = this.Color1, Accent = this.Color2 },
                    [ThemeVariant.Light] = new ColorPaletteResources { RegionColor = this.Color3, Accent = this.Color4 }
                }
            };

            // Swap the resource dictionary (fast single-reference assignment).
            // Templates live in App.Styles, not App.Resources, so no re-templating.
            App.Current.Resources = theme.Resources;

            // Re-apply custom keys that the swap cleared
            App.Current.Resources["AppCornerRadius"] = this.CornerRadius;
            App.Current.Resources["AppShadow"] = this.Shadow;
            App.Current.Resources["AppBorderThickness"] = this.BorderThickness;

            App.Current.RequestedThemeVariant = DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
