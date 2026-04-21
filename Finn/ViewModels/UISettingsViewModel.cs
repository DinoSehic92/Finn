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
    Color? PanelBackground = null,
    Color? TextColor = null,
    Color? BorderColor = null,
    bool   Rounded  = true,
    bool   Shadows  = false,
    bool   Borders  = false);

public class UISettingsViewModel : ObservableObject
{
    // ── dark presets ─────────────────────────────────────────────────────────
    // Each belongs to a different colour family and carries tuned style flags.
    public static readonly IReadOnlyList<ThemePreset> DarkPresets = new[]
    {
        //                            Background   Accent       PanelBg      Text         Border       R      S      B

        // ── High-Contrast / Developer Favorites (Monochromatic bases, stark vibrant accents)
        new ThemePreset("Graphite",  Color.Parse("#161618"), Color.Parse("#FFB340"), Color.Parse("#202022"), Color.Parse("#EBEBF5"), Color.Parse("#38383A"), true,  true,  false),
        new ThemePreset("Midnight",  Color.Parse("#0D1117"), Color.Parse("#58A6FF"), Color.Parse("#161B22"), Color.Parse("#C9D1D9"), Color.Parse("#30363D"), true,  false, true), // GitHub Dark inspired
        new ThemePreset("Nord",      Color.Parse("#2E3440"), Color.Parse("#88C0D0"), Color.Parse("#3B4252"), Color.Parse("#D8DEE9"), Color.Parse("#4C566A"), true,  false, false), // Classic Nord
        new ThemePreset("Dracula",   Color.Parse("#282A36"), Color.Parse("#FF79C6"), Color.Parse("#383A59"), Color.Parse("#F8F8F2"), Color.Parse("#44475A"), true,  true,  false), // Dracula theme
        new ThemePreset("Tokyo",     Color.Parse("#1A1B26"), Color.Parse("#7AA2F7"), Color.Parse("#24283B"), Color.Parse("#C0CAF5"), Color.Parse("#414868"), true,  true,  true),  // Tokyo Night

        // ── Creative / Complementary Palettes (Mixing hues across color wheel)
        // Eclipse: Deep dark blue background, vibrant coral/orange accent, off-white text
        new ThemePreset("Eclipse",   Color.Parse("#0B132B"), Color.Parse("#FF7A59"), Color.Parse("#152238"), Color.Parse("#E0E6ED"), Color.Parse("#283B59"), true,  true,  false),

        // Cyberpunk: Almost pure black shell, deep purple panel, electric teal accent, high-contrast cyan/white text
        new ThemePreset("Cyber",     Color.Parse("#09050C"), Color.Parse("#00FFCC"), Color.Parse("#1B1226"), Color.Parse("#E0F2FE"), Color.Parse("#392A4D"), false, false, true),

        // Emerald City: Very dark charcoal / yellow-green tint, gold/amber accent, bright cream text
        new ThemePreset("Emerald",   Color.Parse("#111A16"), Color.Parse("#E5B567"), Color.Parse("#1A2620"), Color.Parse("#F2F0E6"), Color.Parse("#31473A"), true,  true,  false),

        // Neo-Brutalism: Very dark grey, bright pink accent, slightly lighter grey panel, stark outlines
        new ThemePreset("Neo",       Color.Parse("#121212"), Color.Parse("#F92672"), Color.Parse("#1E1E1E"), Color.Parse("#F8F8F2"), Color.Parse("#333333"), false, false, true),

        // Outrun: Dark navy/purple shell, magenta accent, soft pinkish-white text
        new ThemePreset("Outrun",    Color.Parse("#140D26"), Color.Parse("#FF2A6D"), Color.Parse("#21183B"), Color.Parse("#FDE4EC"), Color.Parse("#463366"), true,  true,  false),
    };

    // ── light presets ────────────────────────────────────────────────────────
    public static readonly IReadOnlyList<ThemePreset> LightPresets = new[]
    {
        //                           Background   Accent       PanelBg      Text         Border       R      S      B
        new ThemePreset("Cloud",    Color.Parse("#E8EFF8"), Color.Parse("#1A6FD4"), Color.Parse("#FFFFFF"), Color.Parse("#17283C"), Color.Parse("#A8BDD4"), true,  true,  false),
        new ThemePreset("Linen",    Color.Parse("#F5EEE4"), Color.Parse("#BF5322"), Color.Parse("#FFFFFF"), Color.Parse("#2A1A0A"), Color.Parse("#CEC0AE"), true,  true,  false),
        new ThemePreset("Meadow",   Color.Parse("#E8F3EA"), Color.Parse("#1F7A45"), Color.Parse("#FFFFFF"), Color.Parse("#122618"), Color.Parse("#A8CCB4"), true,  false, true),
        new ThemePreset("Pearl",    Color.Parse("#F2F4F7"), Color.Parse("#2E5FA3"), Color.Parse("#FFFFFF"), Color.Parse("#1C2330"), Color.Parse("#C4CAD6"), true,  true,  false),
        new ThemePreset("Rosewood", Color.Parse("#F5EAEC"), Color.Parse("#A03050"), Color.Parse("#FFFFFF"), Color.Parse("#2E121A"), Color.Parse("#D4B4BC"), true,  true,  false),
        new ThemePreset("Birch",    Color.Parse("#EDE8DC"), Color.Parse("#6B7A2A"), Color.Parse("#FFFFFF"), Color.Parse("#28220E"), Color.Parse("#C8C0A8"), true,  false, true),
        new ThemePreset("Lavender", Color.Parse("#EFEBF5"), Color.Parse("#6B21A8"), Color.Parse("#FFFFFF"), Color.Parse("#251A33"), Color.Parse("#C2B4D6"), true,  true,  false),
        new ThemePreset("Dusk",     Color.Parse("#EBECEE"), Color.Parse("#4A5568"), Color.Parse("#FFFFFF"), Color.Parse("#2D3748"), Color.Parse("#CBD5E0"), true,  false, true),
        new ThemePreset("Peach",    Color.Parse("#F8EFEF"), Color.Parse("#B5415C"), Color.Parse("#FFFFFF"), Color.Parse("#331C21"), Color.Parse("#DFC1C8"), true,  true,  false),
        new ThemePreset("Frost",    Color.Parse("#EFF4F5"), Color.Parse("#00838F"), Color.Parse("#FFFFFF"), Color.Parse("#183336"), Color.Parse("#BCE2E6"), true,  true,  false),
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

            // Extended overrides default to disabled (empty = Fluent default)
            DarkTextColor    = Color.Parse("#FFFFFF");
            DarkPanelColor   = Color.Parse("#2C2C2E");
            DarkBorderColor  = Color.Parse("#48484A");
            LightTextColor   = Color.Parse("#000000");
            LightPanelColor  = Color.Parse("#FFFFFF");
            LightBorderColor = Color.Parse("#C0C8D0");

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

        // ── extended per-theme overrides (empty = use Fluent default) ─────────────────────────────
        private Color darkTextColor;
        public Color DarkTextColor   { get => darkTextColor;   set { darkTextColor   = value; OnPropertyChanged(nameof(DarkTextColor));   } }
        private bool darkTextColorEnabled;
        public bool DarkTextColorEnabled { get => darkTextColorEnabled; set { darkTextColorEnabled = value; OnPropertyChanged(nameof(DarkTextColorEnabled)); } }

        private Color darkPanelColor;
        public Color DarkPanelColor  { get => darkPanelColor;  set { darkPanelColor  = value; OnPropertyChanged(nameof(DarkPanelColor));  } }
        private bool darkPanelColorEnabled;
        public bool DarkPanelColorEnabled { get => darkPanelColorEnabled; set { darkPanelColorEnabled = value; OnPropertyChanged(nameof(DarkPanelColorEnabled)); } }

        private Color darkBorderColor;
        public Color DarkBorderColor { get => darkBorderColor; set { darkBorderColor = value; OnPropertyChanged(nameof(DarkBorderColor)); } }
        private bool darkBorderColorEnabled;
        public bool DarkBorderColorEnabled { get => darkBorderColorEnabled; set { darkBorderColorEnabled = value; OnPropertyChanged(nameof(DarkBorderColorEnabled)); } }

        private Color lightTextColor;
        public Color LightTextColor   { get => lightTextColor;   set { lightTextColor   = value; OnPropertyChanged(nameof(LightTextColor));   } }
        private bool lightTextColorEnabled;
        public bool LightTextColorEnabled { get => lightTextColorEnabled; set { lightTextColorEnabled = value; OnPropertyChanged(nameof(LightTextColorEnabled)); } }

        private Color lightPanelColor;
        public Color LightPanelColor  { get => lightPanelColor;  set { lightPanelColor  = value; OnPropertyChanged(nameof(LightPanelColor));  } }
        private bool lightPanelColorEnabled;
        public bool LightPanelColorEnabled { get => lightPanelColorEnabled; set { lightPanelColorEnabled = value; OnPropertyChanged(nameof(LightPanelColorEnabled)); } }

        private Color lightBorderColor;
        public Color LightBorderColor { get => lightBorderColor; set { lightBorderColor = value; OnPropertyChanged(nameof(LightBorderColor)); } }
        private bool lightBorderColorEnabled;
        public bool LightBorderColorEnabled { get => lightBorderColorEnabled; set { lightBorderColorEnabled = value; OnPropertyChanged(nameof(LightBorderColorEnabled)); } }



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
                DarkTextColor   = this.DarkTextColorEnabled   ? this.DarkTextColor.ToString()   : string.Empty,
                DarkPanelColor  = this.DarkPanelColorEnabled  ? this.DarkPanelColor.ToString()  : string.Empty,
                DarkBorderColor = this.DarkBorderColorEnabled ? this.DarkBorderColor.ToString() : string.Empty,
                LightTextColor   = this.LightTextColorEnabled   ? this.LightTextColor.ToString()   : string.Empty,
                LightPanelColor  = this.LightPanelColorEnabled  ? this.LightPanelColor.ToString()  : string.Empty,
                LightBorderColor = this.LightBorderColorEnabled ? this.LightBorderColor.ToString() : string.Empty,
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

                if (!string.IsNullOrWhiteSpace(ui.DarkTextColor))   { try { this.DarkTextColor   = Color.Parse(ui.DarkTextColor);   this.DarkTextColorEnabled   = true; } catch { } }
                if (!string.IsNullOrWhiteSpace(ui.DarkPanelColor))  { try { this.DarkPanelColor  = Color.Parse(ui.DarkPanelColor);  this.DarkPanelColorEnabled  = true; } catch { } }
                if (!string.IsNullOrWhiteSpace(ui.DarkBorderColor)) { try { this.DarkBorderColor = Color.Parse(ui.DarkBorderColor); this.DarkBorderColorEnabled = true; } catch { } }
                if (!string.IsNullOrWhiteSpace(ui.LightTextColor))   { try { this.LightTextColor   = Color.Parse(ui.LightTextColor);   this.LightTextColorEnabled   = true; } catch { } }
                if (!string.IsNullOrWhiteSpace(ui.LightPanelColor))  { try { this.LightPanelColor  = Color.Parse(ui.LightPanelColor);  this.LightPanelColorEnabled  = true; } catch { } }
                if (!string.IsNullOrWhiteSpace(ui.LightBorderColor)) { try { this.LightBorderColor = Color.Parse(ui.LightBorderColor); this.LightBorderColorEnabled = true; } catch { } }
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

            // Re-apply per-theme custom overrides (survive every palette swap)
            if (DarkMode)
            {
                if (DarkTextColorEnabled)
                {
                    var b = new SolidColorBrush(DarkTextColor);
                    App.Current.Resources["SystemControlForegroundBaseHighBrush"] = b;
                    App.Current.Resources["ButtonForeground"]      = b;
                    App.Current.Resources["TextControlForeground"] = b;
                }
                if (DarkPanelColorEnabled)
                    App.Current.Resources["TextControlBackground"] = new SolidColorBrush(DarkPanelColor);
                if (DarkBorderColorEnabled)
                {
                    var b = new SolidColorBrush(DarkBorderColor);
                    App.Current.Resources["DataGridGridLinesBrush"]    = b;
                    App.Current.Resources["SystemBaseMediumLowColor"]  = DarkBorderColor;
                }
            }
            else
            {
                if (LightTextColorEnabled)
                {
                    var b = new SolidColorBrush(LightTextColor);
                    App.Current.Resources["SystemControlForegroundBaseHighBrush"] = b;
                    App.Current.Resources["ButtonForeground"]      = b;
                    App.Current.Resources["TextControlForeground"] = b;
                }
                if (LightPanelColorEnabled)
                    App.Current.Resources["TextControlBackground"] = new SolidColorBrush(LightPanelColor);
                if (LightBorderColorEnabled)
                {
                    var b = new SolidColorBrush(LightBorderColor);
                    App.Current.Resources["DataGridGridLinesBrush"]    = b;
                    App.Current.Resources["SystemBaseMediumLowColor"]  = LightBorderColor;
                }
            }

            App.Current.RequestedThemeVariant = DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
