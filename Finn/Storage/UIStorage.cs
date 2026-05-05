using System;

namespace Finn.Storage
{
    // Persisted UI settings storage (separate from runtime UI viewmodel)
    public class UIStorage
    {
        public string Color1 { get; set; } = string.Empty;
        public string Color2 { get; set; } = string.Empty;
        public string Color3 { get; set; } = string.Empty;
        public string Color4 { get; set; } = string.Empty;

        // Extended per-theme overrides: Dark = 5-7, Light = 8-10
        // Empty string means "use Fluent default" for that slot.
        public string DarkTextColor   { get; set; } = string.Empty;  // Color5
        public string DarkPanelColor  { get; set; } = string.Empty;  // Color6
        public string DarkBorderColor { get; set; } = string.Empty;  // Color7
        public string LightTextColor  { get; set; } = string.Empty;  // Color8
        public string LightPanelColor { get; set; } = string.Empty;  // Color9
        public string LightBorderColor{ get; set; } = string.Empty;  // Color10

        public bool CornerRadiusVal { get; set; }
        public double CornerRadius { get; set; }

        public bool ShadowVal { get; set; }
        public bool ShowBorders { get; set; }

        public bool DarkMode { get; set; }

        public string Font { get; set; } = string.Empty;
        public int FontSize { get; set; }

        // Persistent UI-related flags that used to live in Storage.General
        public bool TrayNote { get; set; }
        public bool TrayCollections { get; set; }
        public bool TrayBookmarks { get; set; }
        public bool TrayRecent { get; set; }
        public bool TrayVersions { get; set; }
        public bool TrayOtherFiles { get; set; }
        public bool TrayAnnotations { get; set; }
        public bool TrayTodo { get; set; }
        public bool ShowIcons { get; set; }
        public bool ColorTagDot { get; set; }

        // Persisted view visibility flags
        public bool TreeViewOpen { get; set; }
        public bool CalendarOpen { get; set; }
        public bool TimeSheetOpen { get; set; }
        public bool ShowFolders { get; set; }
        public bool ShowThumbnails { get; set; }
        public bool TrayViewOpen { get; set; }
        public bool ShowActionBar { get; set; }
        public bool PreviewDarkMode { get; set; }
        public string PreviewDarkModeTint { get; set; } = "None";
        public int PreviewTintIntensity { get; set; } = 25;
        public string PreviewLightPaper { get; set; } = "White";
        public int TreeViewWidth { get; set; }
        public int TrayWidth { get; set; }
        public bool ReadBytesMode { get; set; }
        public bool FolderWatchEnabled { get; set; }
        public bool SharedSyncCheckOnStartup { get; set; } = true;
        public bool AlternatingRowShading { get; set; }
        public bool SuperuserMode { get; set; }
        // Note: PreviewEmbeddedOpen intentionally omitted (kept transient in MainViewModel)
    }
}
