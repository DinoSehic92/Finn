using System;

namespace Finn.Services
{
    // Simple DTO for UI-specific settings saved separately from the main projects file.
    public class UISettings
    {
        public string Color1 { get; set; } = string.Empty;
        public string Color2 { get; set; } = string.Empty;
        public string Color3 { get; set; } = string.Empty;
        public string Color4 { get; set; } = string.Empty;

        public bool CornerRadiusVal { get; set; }
        public double CornerRadius { get; set; }

        public bool ShadowVal { get; set; }

        public bool DarkMode { get; set; }

        public string Font { get; set; } = string.Empty;
        public int FontSize { get; set; }

        // Persistent UI-related flags that used to live in Storage.General
        public bool TrayNote { get; set; }
        public bool TrayCollections { get; set; }
        public bool TrayBookmarks { get; set; }
        public bool TrayRecent { get; set; }
        public bool ShowIcons { get; set; }
        
        // Persisted view visibility flags
        public bool TreeViewOpen { get; set; }
        public bool CalendarOpen { get; set; }
        public bool TimeSheetOpen { get; set; }
        public bool ShowFolders { get; set; }
        public bool ShowThumbnails { get; set; }
        public bool TrayViewOpen { get; set; }
        public bool PreviewEmbeddedOpen { get; set; }
    }
}
