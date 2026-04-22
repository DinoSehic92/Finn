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
    // Curated toward neutral bases, reliable contrast and restrained accents.
    public static readonly IReadOnlyList<ThemePreset> DarkPresets = new[]
    {
        //                            Background   Accent       PanelBg      Text         Border       R      S      B

        new ThemePreset("Graphite",  Color.Parse("#161618"), Color.Parse("#D8A44A"), Color.Parse("#202124"), Color.Parse("#ECECF2"), Color.Parse("#393B40"), true,  true,  false),
        new ThemePreset("Midnight",  Color.Parse("#0D1117"), Color.Parse("#58A6FF"), Color.Parse("#151C24"), Color.Parse("#C9D1D9"), Color.Parse("#2E3844"), true,  false, true),
        new ThemePreset("Nord",      Color.Parse("#2E3440"), Color.Parse("#88C0D0"), Color.Parse("#394150"), Color.Parse("#D8DEE9"), Color.Parse("#4C566A"), true,  false, false),
        new ThemePreset("Tokyo",     Color.Parse("#1A1B26"), Color.Parse("#7AA2F7"), Color.Parse("#232637"), Color.Parse("#C0CAF5"), Color.Parse("#3F4660"), true,  true,  true),
        new ThemePreset("Slate",     Color.Parse("#1C232B"), Color.Parse("#6EA8FE"), Color.Parse("#252E38"), Color.Parse("#D9E1EA"), Color.Parse("#3B4956"), true,  false, true),
        new ThemePreset("Harbor",    Color.Parse("#141D24"), Color.Parse("#4CB7C5"), Color.Parse("#1C2831"), Color.Parse("#DCE7EE"), Color.Parse("#31424E"), true,  true,  false),
        new ThemePreset("Moss",      Color.Parse("#171B18"), Color.Parse("#68B36B"), Color.Parse("#202622"), Color.Parse("#E2E9E2"), Color.Parse("#38443B"), true,  false, true),
        new ThemePreset("Ember",     Color.Parse("#1A1715"), Color.Parse("#D1905A"), Color.Parse("#24201D"), Color.Parse("#EEE5DD"), Color.Parse("#443931"), true,  true,  false),
        new ThemePreset("Alloy",     Color.Parse("#171A1F"), Color.Parse("#8E95F2"), Color.Parse("#20252C"), Color.Parse("#E6EAF0"), Color.Parse("#39414C"), true,  true,  false),
        new ThemePreset("Ash",       Color.Parse("#1B1D21"), Color.Parse("#9AA5B1"), Color.Parse("#24282D"), Color.Parse("#E5E7EB"), Color.Parse("#3E4650"), true,  false, true),
    };

    // ── light presets ────────────────────────────────────────────────────────
    public static readonly IReadOnlyList<ThemePreset> LightPresets = new[]
    {
        //                           Background   Accent       PanelBg      Text         Border       R      S      B
        new ThemePreset("Cloud",    Color.Parse("#E8EFF8"), Color.Parse("#1A6FD4"), Color.Parse("#FFFFFF"), Color.Parse("#17283C"), Color.Parse("#A8BDD4"), true,  true,  false),
        new ThemePreset("Pearl",    Color.Parse("#F2F4F7"), Color.Parse("#2E5FA3"), Color.Parse("#FFFFFF"), Color.Parse("#1C2330"), Color.Parse("#C4CAD6"), true,  true,  false),
        new ThemePreset("Frost",    Color.Parse("#EFF4F5"), Color.Parse("#0A7C86"), Color.Parse("#FFFFFF"), Color.Parse("#183336"), Color.Parse("#B7DDE1"), true,  true,  false),
        new ThemePreset("Linen",    Color.Parse("#F5EEE4"), Color.Parse("#BF5322"), Color.Parse("#FFFFFF"), Color.Parse("#2A1A0A"), Color.Parse("#CEC0AE"), true,  true,  false),
        new ThemePreset("Birch",    Color.Parse("#EDE8DC"), Color.Parse("#6B7A2A"), Color.Parse("#FFFFFF"), Color.Parse("#28220E"), Color.Parse("#C8C0A8"), true,  false, true),
        new ThemePreset("Meadow",   Color.Parse("#E8F3EA"), Color.Parse("#1F7A45"), Color.Parse("#FFFFFF"), Color.Parse("#122618"), Color.Parse("#A8CCB4"), true,  false, true),
        new ThemePreset("Sage",     Color.Parse("#EEF2EC"), Color.Parse("#5C7F62"), Color.Parse("#FFFFFF"), Color.Parse("#1F2A20"), Color.Parse("#C4D0C1"), true,  false, false),
        new ThemePreset("Lavender", Color.Parse("#EFEBF5"), Color.Parse("#6B21A8"), Color.Parse("#FFFFFF"), Color.Parse("#251A33"), Color.Parse("#C2B4D6"), true,  true,  false),
        new ThemePreset("Dusk",     Color.Parse("#EBECEE"), Color.Parse("#4A5568"), Color.Parse("#FFFFFF"), Color.Parse("#2D3748"), Color.Parse("#CBD5E0"), true,  false, true),
        new ThemePreset("Blush",    Color.Parse("#F6ECEF"), Color.Parse("#B24567"), Color.Parse("#FFFFFF"), Color.Parse("#331E24"), Color.Parse("#DEC2CB"), true,  true,  false),
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

        public void ApplyDarkPreset(ThemePreset preset)
        {
            color1 = preset.Background;
            color2 = preset.Accent;

            cornerRadiusVal = preset.Rounded;
            SetCornerRadius();

            shadowVal = preset.Shadows;
            SetShadow();

            showBorders = preset.Borders;
            SetBorderThickness();

            darkTextColorEnabled = preset.TextColor.HasValue;
            if (preset.TextColor.HasValue)
                darkTextColor = preset.TextColor.Value;

            darkPanelColorEnabled = preset.PanelBackground.HasValue;
            if (preset.PanelBackground.HasValue)
                darkPanelColor = preset.PanelBackground.Value;

            darkBorderColorEnabled = preset.BorderColor.HasValue;
            if (preset.BorderColor.HasValue)
                darkBorderColor = preset.BorderColor.Value;

            OnPropertyChanged(nameof(Color1));
            OnPropertyChanged(nameof(Color2));
            OnPropertyChanged(nameof(CornerRadiusVal));
            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(ShadowVal));
            OnPropertyChanged(nameof(Shadow));
            OnPropertyChanged(nameof(ShowBorders));
            OnPropertyChanged(nameof(BorderThickness));
            OnPropertyChanged(nameof(DarkTextColorEnabled));
            OnPropertyChanged(nameof(DarkTextColor));
            OnPropertyChanged(nameof(DarkPanelColorEnabled));
            OnPropertyChanged(nameof(DarkPanelColor));
            OnPropertyChanged(nameof(DarkBorderColorEnabled));
            OnPropertyChanged(nameof(DarkBorderColor));

            ApplyTheme();
        }

        public void ApplyLightPreset(ThemePreset preset)
        {
            color3 = preset.Background;
            color4 = preset.Accent;

            cornerRadiusVal = preset.Rounded;
            SetCornerRadius();

            shadowVal = preset.Shadows;
            SetShadow();

            showBorders = preset.Borders;
            SetBorderThickness();

            lightTextColorEnabled = preset.TextColor.HasValue;
            if (preset.TextColor.HasValue)
                lightTextColor = preset.TextColor.Value;

            lightPanelColorEnabled = preset.PanelBackground.HasValue;
            if (preset.PanelBackground.HasValue)
                lightPanelColor = preset.PanelBackground.Value;

            lightBorderColorEnabled = preset.BorderColor.HasValue;
            if (preset.BorderColor.HasValue)
                lightBorderColor = preset.BorderColor.Value;

            OnPropertyChanged(nameof(Color3));
            OnPropertyChanged(nameof(Color4));
            OnPropertyChanged(nameof(CornerRadiusVal));
            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(ShadowVal));
            OnPropertyChanged(nameof(Shadow));
            OnPropertyChanged(nameof(ShowBorders));
            OnPropertyChanged(nameof(BorderThickness));
            OnPropertyChanged(nameof(LightTextColorEnabled));
            OnPropertyChanged(nameof(LightTextColor));
            OnPropertyChanged(nameof(LightPanelColorEnabled));
            OnPropertyChanged(nameof(LightPanelColor));
            OnPropertyChanged(nameof(LightBorderColorEnabled));
            OnPropertyChanged(nameof(LightBorderColor));

            ApplyTheme();
        }

        private static Color Mix(Color baseColor, Color tintColor, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);

            static byte Blend(byte from, byte to, double ratio) =>
                (byte)Math.Round(from + ((to - from) * ratio));

            return Color.FromArgb(
                255,
                Blend(baseColor.R, tintColor.R, amount),
                Blend(baseColor.G, tintColor.G, amount),
                Blend(baseColor.B, tintColor.B, amount));
        }

        private static double GetLuminance(Color color)
        {
            static double Channel(byte value)
            {
                var normalized = value / 255.0;
                return normalized <= 0.03928
                    ? normalized / 12.92
                    : Math.Pow((normalized + 0.055) / 1.055, 2.4);
            }

            return (0.2126 * Channel(color.R))
                 + (0.7152 * Channel(color.G))
                 + (0.0722 * Channel(color.B));
        }

        private static bool IsDark(Color color) => GetLuminance(color) < 0.45;

        private static Color GetReadableForeground(Color background) =>
            IsDark(background)
                ? Color.Parse("#F4F7FB")
                : Color.Parse("#16202B");

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

            var background = DarkMode ? this.Color1 : this.Color3;
            var accent = DarkMode ? this.Color2 : this.Color4;
            var isDark = IsDark(background);

            var textEnabled = DarkMode ? this.DarkTextColorEnabled : this.LightTextColorEnabled;
            var panelEnabled = DarkMode ? this.DarkPanelColorEnabled : this.LightPanelColorEnabled;
            var borderEnabled = DarkMode ? this.DarkBorderColorEnabled : this.LightBorderColorEnabled;

            var text = textEnabled
                ? (DarkMode ? this.DarkTextColor : this.LightTextColor)
                : GetReadableForeground(background);

            var panel = panelEnabled
                ? (DarkMode ? this.DarkPanelColor : this.LightPanelColor)
                : Mix(background, Colors.White, isDark ? 0.07 : 0.55);

            var surfaceAlt = isDark
                ? Mix(panel, Colors.White, 0.045)
                : Mix(panel, background, 0.12);

            var chromeSurface = isDark
                ? Mix(background, Colors.White, 0.05)
                : Mix(background, panel, 0.35);

            var flyoutSurface = isDark
                ? Mix(chromeSurface, Colors.White, 0.035)
                : Mix(chromeSurface, panel, 0.08);

            var panelSurface = isDark
                ? Mix(panel, Colors.White, 0.045)
                : Mix(panel, background, 0.14);

            var controlSurface = isDark
                ? Mix(panelSurface, Colors.White, 0.085)
                : Mix(panelSurface, background, 0.10);

            var mutedText = Mix(text, background, isDark ? 0.38 : 0.50);
            var mediumText = Mix(text, background, isDark ? 0.22 : 0.30);

            var subtleBorder = borderEnabled
                ? (DarkMode ? this.DarkBorderColor : this.LightBorderColor)
                : Mix(panel, text, isDark ? 0.16 : 0.20);

            var divider = Mix(subtleBorder, panelSurface, isDark ? 0.24 : 0.12);
            var strongBorder = Mix(subtleBorder, text, isDark ? 0.30 : 0.24);
            var panelBorder = Mix(subtleBorder, panelSurface, isDark ? 0.08 : 0.04);
            var controlBorder = Mix(subtleBorder, controlSurface, isDark ? 0.05 : 0.03);
            var flyoutBorder = Mix(subtleBorder, flyoutSurface, isDark ? 0.30 : 0.22);
            var hoverSurface = Mix(controlSurface, accent, isDark ? 0.12 : 0.08);
            var selectedSurface = Mix(controlSurface, accent, isDark ? 0.22 : 0.16);
            var menuHoverSurface = Mix(chromeSurface, accent, isDark ? 0.08 : 0.06);
            var rowHoverSurface = Mix(panelSurface, accent, isDark ? 0.08 : 0.05);
            var rowSelectionSurface = Mix(panelSurface, accent, isDark ? 0.14 : 0.10);
            var accentForeground = GetReadableForeground(accent);

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
            App.Current.Resources["AppPopupBorderThickness"] = new Thickness(1);

            var foregroundBrush = new SolidColorBrush(text);
            var mutedForegroundBrush = new SolidColorBrush(mutedText);
            var mediumForegroundBrush = new SolidColorBrush(mediumText);
            var backgroundBrush = new SolidColorBrush(background);
            var surfaceBrush = new SolidColorBrush(panel);
            var surfaceAltBrush = new SolidColorBrush(surfaceAlt);
            var chromeSurfaceBrush = new SolidColorBrush(chromeSurface);
            var flyoutSurfaceBrush = new SolidColorBrush(flyoutSurface);
            var panelSurfaceBrush = new SolidColorBrush(panelSurface);
            var controlSurfaceBrush = new SolidColorBrush(controlSurface);
            var hoverSurfaceBrush = new SolidColorBrush(hoverSurface);
            var selectedSurfaceBrush = new SolidColorBrush(selectedSurface);
            var menuHoverSurfaceBrush = new SolidColorBrush(menuHoverSurface);
            var rowHoverSurfaceBrush = new SolidColorBrush(rowHoverSurface);
            var rowSelectionSurfaceBrush = new SolidColorBrush(rowSelectionSurface);
            var dividerBrush = new SolidColorBrush(divider);
            var panelBorderBrush = new SolidColorBrush(panelBorder);
            var controlBorderBrush = new SolidColorBrush(controlBorder);
            var flyoutBorderBrush = new SolidColorBrush(flyoutBorder);
            var subtleBorderBrush = new SolidColorBrush(subtleBorder);
            var strongBorderBrush = new SolidColorBrush(strongBorder);
            var accentForegroundBrush = new SolidColorBrush(accentForeground);

            App.Current.Resources["AppBackgroundBrush"] = backgroundBrush;
            App.Current.Resources["AppSurfaceBrush"] = surfaceBrush;
            App.Current.Resources["AppSurfaceAltBrush"] = surfaceAltBrush;
            App.Current.Resources["AppChromeSurfaceBrush"] = chromeSurfaceBrush;
            App.Current.Resources["AppFlyoutSurfaceBrush"] = flyoutSurfaceBrush;
            App.Current.Resources["AppPanelSurfaceBrush"] = panelSurfaceBrush;
            App.Current.Resources["AppControlSurfaceBrush"] = controlSurfaceBrush;
            App.Current.Resources["AppHoverSurfaceBrush"] = hoverSurfaceBrush;
            App.Current.Resources["AppSelectedSurfaceBrush"] = selectedSurfaceBrush;
            App.Current.Resources["AppForegroundBrush"] = foregroundBrush;
            App.Current.Resources["AppMutedForegroundBrush"] = mutedForegroundBrush;
            App.Current.Resources["AppMenuHoverBrush"] = menuHoverSurfaceBrush;
            App.Current.Resources["AppRowHoverBrush"] = rowHoverSurfaceBrush;
            App.Current.Resources["AppRowSelectionBrush"] = rowSelectionSurfaceBrush;
            App.Current.Resources["AppDividerBrush"] = dividerBrush;
            App.Current.Resources["AppPanelBorderBrush"] = panelBorderBrush;
            App.Current.Resources["AppControlBorderBrush"] = controlBorderBrush;
            App.Current.Resources["AppFlyoutBorderBrush"] = flyoutBorderBrush;
            App.Current.Resources["AppSubtleBorderBrush"] = subtleBorderBrush;
            App.Current.Resources["AppStrongBorderBrush"] = strongBorderBrush;
            App.Current.Resources["AppAccentForegroundBrush"] = accentForegroundBrush;

            App.Current.Resources["SystemControlForegroundBaseHighBrush"] = foregroundBrush;
            App.Current.Resources["SystemControlForegroundBaseMediumBrush"] = mediumForegroundBrush;
            App.Current.Resources["SystemControlForegroundBaseMediumHighBrush"] = mediumForegroundBrush;
            App.Current.Resources["SystemControlForegroundBaseMediumLowBrush"] = mutedForegroundBrush;
            App.Current.Resources["ButtonForeground"] = foregroundBrush;
            App.Current.Resources["TextControlForeground"] = foregroundBrush;
            App.Current.Resources["TextControlBackground"] = controlSurfaceBrush;
            App.Current.Resources["FlyoutPresenterBackground"] = flyoutSurfaceBrush;
            App.Current.Resources["FlyoutBorderThemeBrush"] = flyoutBorderBrush;
            App.Current.Resources["FlyoutBorderThemeThickness"] = new Thickness(1);
            App.Current.Resources["FlyoutContentThemePadding"] = new Thickness(8, 6);
            App.Current.Resources["MenuFlyoutPresenterBackground"] = flyoutSurfaceBrush;
            App.Current.Resources["MenuFlyoutPresenterBorderBrush"] = flyoutBorderBrush;
            App.Current.Resources["MenuFlyoutPresenterBorderThemeThickness"] = new Thickness(1);
            App.Current.Resources["MenuFlyoutPresenterThemePadding"] = new Thickness(2, 4);
            App.Current.Resources["DataGridColumnHeaderForegroundBrush"] = mutedForegroundBrush;
            App.Current.Resources["DataGridColumnHeaderBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
            App.Current.Resources["DataGridColumnHeaderHoveredBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
            App.Current.Resources["DataGridColumnHeaderPressedBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
            App.Current.Resources["DataGridColumnHeaderDraggedBackgroundBrush"] = new SolidColorBrush(Colors.Transparent);
            App.Current.Resources["DataGridGridLinesBrush"] = dividerBrush;
            App.Current.Resources["SystemBaseMediumLowColor"] = strongBorder;

            App.Current.RequestedThemeVariant = DarkMode ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }
}
