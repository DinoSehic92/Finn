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
        public bool TrayDiff { get; set; }
        public bool TrayVersions { get; set; }
        public bool TrayOtherFiles { get; set; }
        public bool TrayAnnotations { get; set; }
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
        public int TreeViewWidth { get; set; }
        public int TrayWidth { get; set; }
        // Note: PreviewEmbeddedOpen intentionally omitted (kept transient in MainViewModel)
    }
}
