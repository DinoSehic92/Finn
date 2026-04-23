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
    public static readonly IReadOnlyList<ThemePreset> DarkPresets = new[]
    {
        //                            Background   Accent       PanelBg      Text         Border       R      S      B

        // Default — matches the original hardcoded dark styling baseline for the app
        new ThemePreset("Default",   Color.Parse("#1F2933"), Color.Parse("#0A84FF"), Color.Parse("#2C3640"), Color.Parse("#F4F7FB"), Color.Parse("#44515E"), true,  false, false),

        // Graphite — neutral charcoal with a lifted panel so areas separate cleanly without borders
        new ThemePreset("Graphite",  Color.Parse("#24272C"), Color.Parse("#8EA4B5"), Color.Parse("#31363D"), Color.Parse("#E8EBEF"), Color.Parse("#56616C"), true,  true,  false),

        // Midnight — deep navy anchor with a clearly brighter work surface
        new ThemePreset("Midnight",  Color.Parse("#1B2530"), Color.Parse("#6EA5DA"), Color.Parse("#2C3B49"), Color.Parse("#DAE3EC"), Color.Parse("#4B5D71"), true,  true,  true),

        // Nord — soft slate shell with a gently lifted panel and icy accent
        new ThemePreset("Nord",      Color.Parse("#39424D"), Color.Parse("#8FB7CB"), Color.Parse("#444F5B"), Color.Parse("#E0E7EE"), Color.Parse("#657483"), true,  false, true),

        // Harbor — marine blue-grey with a gently raised panel and calmer teal-blue accent
        new ThemePreset("Harbor",    Color.Parse("#2F3942"), Color.Parse("#71A4B0"), Color.Parse("#3B4751"), Color.Parse("#DEE7EC"), Color.Parse("#60717D"), true,  true,  false),

        // Slate — dependable blue-grey workhorse with a slightly brighter panel and restrained blue accent
        new ThemePreset("Slate",     Color.Parse("#303942"), Color.Parse("#80ABCA"), Color.Parse("#3A4650"), Color.Parse("#DCE3E9"), Color.Parse("#5D6B78"), true,  false, true),

        // Granite — brighter neutral shell with a modestly lifted mineral panel
        new ThemePreset("Granite",   Color.Parse("#434947"), Color.Parse("#94A29D"), Color.Parse("#4A504E"), Color.Parse("#E6EAE7"), Color.Parse("#66716C"), true,  false, true),

        // Steel — industrial grey-blue, cleaner and more technical than Slate
        new ThemePreset("Steel",     Color.Parse("#3B434C"), Color.Parse("#90A5BA"), Color.Parse("#47515B"), Color.Parse("#E2E7EC"), Color.Parse("#61707E"), true,  false, true),

        // Mist — olive-grey shell with a darker, but not dramatically darker, panel and no border emphasis
        new ThemePreset("Mist",      Color.Parse("#525E54"), Color.Parse("#94AA9D"), Color.Parse("#3F4842"), Color.Parse("#E6EBE7"), Color.Parse("#667168"), true,  false, false),

        // Tide — airy blue-green shell with a deeper blue-grey panel for clearer structure
        new ThemePreset("Tide",      Color.Parse("#536267"), Color.Parse("#8EB3B8"), Color.Parse("#344044"), Color.Parse("#E4EAEB"), Color.Parse("#73868B"), true,  false, true),
    };

    // ── light presets ────────────────────────────────────────────────────────
    public static readonly IReadOnlyList<ThemePreset> LightPresets = new[]
    {
        //                           Background   Accent       PanelBg      Text         Border       R      S      B

        // Cloud — cool blue-grey wash with crisp blue accent; clear and versatile
        new ThemePreset("Cloud",    Color.Parse("#E2EBF5"), Color.Parse("#226BCB"), Color.Parse("#FFFFFF"), Color.Parse("#182438"), Color.Parse("#A5B7D1"), true,  true,  false),

        // Pearl — neutral cool white with navy accent; safest all-purpose light preset
        new ThemePreset("Pearl",    Color.Parse("#F3F5F7"), Color.Parse("#365F9D"), Color.Parse("#FFFFFF"), Color.Parse("#1F2635"), Color.Parse("#C0C8D4"), true,  true,  false),

        // Frost — icy teal-white with a controlled teal accent
        new ThemePreset("Frost",    Color.Parse("#ECF4F5"), Color.Parse("#1A7882"), Color.Parse("#FFFFFF"), Color.Parse("#173236"), Color.Parse("#ABCFD4"), true,  true,  false),

        // Linen — warm paper with restrained terracotta; soft without drifting muddy
        new ThemePreset("Linen",    Color.Parse("#F4ECE3"), Color.Parse("#B66332"), Color.Parse("#FCFAF7"), Color.Parse("#2D1D12"), Color.Parse("#CBB9A2"), true,  true,  false),

        // Porcelain — crisp blue-white with cooler structure; sharper than Cloud
        new ThemePreset("Porcelain",Color.Parse("#E8EEF6"), Color.Parse("#4475B5"), Color.Parse("#FDFEFF"), Color.Parse("#1B2536"), Color.Parse("#B9C7DA"), true,  true,  false),

        // Sandstone — warm mineral neutral with muted rust accent
        new ThemePreset("Sandstone",Color.Parse("#EFE6DC"), Color.Parse("#A96A3D"), Color.Parse("#FBF8F4"), Color.Parse("#2C2218"), Color.Parse("#CBBBA9"), true,  false, true),

        // Willow — pale green-grey with restrained green accent; natural and low-noise
        new ThemePreset("Willow",   Color.Parse("#E9EFE7"), Color.Parse("#66856D"), Color.Parse("#FBFCFA"), Color.Parse("#1F2A22"), Color.Parse("#BECABF"), true,  false, false),

        // Iris — soft periwinkle-lilac; cooler and more refined than sugary lavender
        new ThemePreset("Iris",     Color.Parse("#EDEBF6"), Color.Parse("#616CB4"), Color.Parse("#FCFCFF"), Color.Parse("#25243A"), Color.Parse("#BDBAD8"), true,  true,  false),

        // Dusk — cool neutral grey with slate accent; minimal and highly usable
        new ThemePreset("Dusk",     Color.Parse("#E9EBEE"), Color.Parse("#556274"), Color.Parse("#F9FAFB"), Color.Parse("#2D3544"), Color.Parse("#C2CAD4"), true,  false, true),

        // Rose Paper — subtle rose-tinted paper with restrained berry accent; warm but still grown-up
        new ThemePreset("Rose Paper", Color.Parse("#F4E9EB"), Color.Parse("#A8576D"), Color.Parse("#FEFBFC"), Color.Parse("#322026"), Color.Parse("#D8BDC5"), true,  true,  false),
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

        // When true, property-change notifications for colour/style properties will not
        // trigger ApplyTheme() from the external listener in App.axaml.cs. We call it
        // once ourselves at the end of each batch update instead.
        internal bool SuppressThemeUpdates { get; private set; }

        public void ApplyDarkPreset(ThemePreset preset)
        {
            SuppressThemeUpdates = true;
            try
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
            }
            finally
            {
                SuppressThemeUpdates = false;
            }

            ApplyTheme();
        }

        public void ApplyLightPreset(ThemePreset preset)
        {
            SuppressThemeUpdates = true;
            try
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
            }
            finally
            {
                SuppressThemeUpdates = false;
            }

            ApplyTheme();
        }

        public void AutoGenerateDarkThemeFromBackground(Color background)
        {
            SuppressThemeUpdates = true;
            try
            {
                color1 = background;

                var generatedText = GetReadableForeground(background);
                var generatedPanel = GeneratePanelColor(background, preferBrighterPanel: ShouldPreferBrighterPanel(background));
                var generatedBorder = GenerateBorderColor(background, generatedPanel, generatedText);

                darkTextColor = generatedText;
                darkTextColorEnabled = true;
                darkPanelColor = generatedPanel;
                darkPanelColorEnabled = true;
                darkBorderColor = generatedBorder;
                darkBorderColorEnabled = true;

                OnPropertyChanged(nameof(Color1));
                OnPropertyChanged(nameof(DarkTextColor));
                OnPropertyChanged(nameof(DarkTextColorEnabled));
                OnPropertyChanged(nameof(DarkPanelColor));
                OnPropertyChanged(nameof(DarkPanelColorEnabled));
                OnPropertyChanged(nameof(DarkBorderColor));
                OnPropertyChanged(nameof(DarkBorderColorEnabled));
            }
            finally
            {
                SuppressThemeUpdates = false;
            }

            ApplyTheme();
        }

        public void AutoGenerateLightThemeFromBackground(Color background)
        {
            SuppressThemeUpdates = true;
            try
            {
                color3 = background;

                var generatedText = GetReadableForeground(background);
                var generatedPanel = GeneratePanelColor(background, preferBrighterPanel: true);
                var generatedBorder = GenerateBorderColor(background, generatedPanel, generatedText);

                lightTextColor = generatedText;
                lightTextColorEnabled = true;
                lightPanelColor = generatedPanel;
                lightPanelColorEnabled = true;
                lightBorderColor = generatedBorder;
                lightBorderColorEnabled = true;

                OnPropertyChanged(nameof(Color3));
                OnPropertyChanged(nameof(LightTextColor));
                OnPropertyChanged(nameof(LightTextColorEnabled));
                OnPropertyChanged(nameof(LightPanelColor));
                OnPropertyChanged(nameof(LightPanelColorEnabled));
                OnPropertyChanged(nameof(LightBorderColor));
                OnPropertyChanged(nameof(LightBorderColorEnabled));
            }
            finally
            {
                SuppressThemeUpdates = false;
            }

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

        private static bool ShouldPreferBrighterPanel(Color background)
        {
            var luminance = GetLuminance(background);
            return luminance < 0.17;
        }

        private static Color GeneratePanelColor(Color background, bool preferBrighterPanel)
        {
            var isDark = IsDark(background);

            if (isDark)
            {
                return preferBrighterPanel
                    ? Mix(background, Colors.White, 0.11)
                    : Mix(background, Colors.Black, 0.22);
            }

            return Mix(background, Colors.White, 0.58);
        }

        private static Color GenerateBorderColor(Color background, Color panel, Color text)
        {
            var isDark = IsDark(background);
            var baseBorder = Mix(panel, text, isDark ? 0.20 : 0.16);
            return Mix(baseBorder, background, isDark ? 0.18 : 0.08);
        }

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
                ? Mix(panel, background, 0.45)
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
            var headerText = Mix(text, background, isDark ? 0.16 : 0.22);

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
            var rowHoverSurface = Mix(panelSurface, accent, isDark ? 0.16 : 0.11);
            var rowSelectionSurface = Mix(panelSurface, accent, isDark ? 0.28 : 0.20);
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
            var headerForegroundBrush = new SolidColorBrush(headerText);
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
            App.Current.Resources["AppHeaderSurfaceBrush"] = chromeSurfaceBrush;
            App.Current.Resources["AppHeaderForegroundBrush"] = headerForegroundBrush;
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
