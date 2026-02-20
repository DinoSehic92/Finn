using System.ComponentModel;
using Finn.Services;
using Avalonia.Media;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Finn.ViewModels
{
    public class UISettingsViewModel : INotifyPropertyChanged
    {
        public UISettingsViewModel()
        {
            // default values from centralized UI defaults
            Color1 = Finn.Services.UIDefaults.DefaultColor1;
            Color2 = Finn.Services.UIDefaults.DefaultColor2;
            Color3 = Finn.Services.UIDefaults.DefaultColor3;
            Color4 = Finn.Services.UIDefaults.DefaultColor4;

            CornerRadius = new CornerRadius(UIDefaults.DefaultCornerRadius);
            CornerRadiusVal = UIDefaults.DefaultCornerRadiusVal;

            Shadow = BoxShadows.Parse("0 2 6 0 #22000000, 0 8 24 0 #11000000");
            ShadowVal = UIDefaults.DefaultShadowVal;

            DarkMode = UIDefaults.DefaultDarkMode;

            Font = UIDefaults.DefaultFontName;
            FontSize = UIDefaults.DefaultFontSize;

            ShowIcons = true;
            TrayNote = true;
            TrayCollections = true;
            TrayBookmarks = true;
            TrayRecent = true;

            // View visibility flags persisted in UI settings
            TreeViewOpen = true;
            CalendarOpen = false;
            TimeSheetOpen = false;
            ShowFolders = false;
            ShowThumbnails = false;
        }

        private bool treeViewOpen;
        public bool TreeViewOpen { get => treeViewOpen; set { treeViewOpen = value; RaisePropertyChanged(nameof(TreeViewOpen)); } }

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

        private bool trayNote;
        public bool TrayNote { get => trayNote; set { trayNote = value; RaisePropertyChanged(nameof(TrayNote)); } }

        private bool trayCollections;
        public bool TrayCollections { get => trayCollections; set { trayCollections = value; RaisePropertyChanged(nameof(TrayCollections)); } }

        private bool trayBookmarks;
        public bool TrayBookmarks { get => trayBookmarks; set { trayBookmarks = value; RaisePropertyChanged(nameof(TrayBookmarks)); } }

        private bool trayRecent;
        public bool TrayRecent { get => trayRecent; set { trayRecent = value; RaisePropertyChanged(nameof(TrayRecent)); } }

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
    }
}
