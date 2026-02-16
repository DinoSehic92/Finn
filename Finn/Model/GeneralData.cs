using Avalonia;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Styling;
using System.Collections.ObjectModel;

namespace Finn.Model
{
    /// <summary>
    /// Represents general application data and color settings.
    /// </summary>
    /// <summary>
    /// Represents general application data and color settings.
    /// </summary>
    public class GeneralData : INotifyPropertyChanged
    {
        // Central default values — update here to change defaults across the app
        // Dark mode defaults (background, accent)
        public static readonly Avalonia.Media.Color DefaultColor1 = Avalonia.Media.Color.Parse("#1F2933");
        public static readonly Avalonia.Media.Color DefaultColor2 = Avalonia.Media.Color.Parse("#0A84FF");
        // Light mode defaults (background, accent) — slightly darker for better contrast
        public static readonly Avalonia.Media.Color DefaultColor3 = Avalonia.Media.Color.Parse("#E9EEF5");
        public static readonly Avalonia.Media.Color DefaultColor4 = Avalonia.Media.Color.Parse("#0066C0");

        public static readonly string DefaultFontName = "Roboto";
        public static readonly int DefaultFontSize = 15;

        private string savePath = "C:\\FIlePathManager";
        /// <summary>
        /// Gets or sets the save path.
        /// </summary>
        public string SavePath
        {
            get => savePath;
            set { savePath = value; RaisePropertyChanged(nameof(SavePath)); }
        }

        private Color color1 = DefaultColor1;
        /// <summary>
        /// Gets or sets the first color.
        /// </summary>
        public Color Color1
        {
            get => color1;
            set { color1 = value; RaisePropertyChanged(nameof(Color1)); }
        }

        private Color color2 = DefaultColor2;
        /// <summary>
        /// Gets or sets the second color.
        /// </summary>
        public Color Color2
        {
            get => color2;
            set { color2 = value; RaisePropertyChanged(nameof(Color2)); }
        }

        private Color color3 = DefaultColor3;
        /// <summary>
        /// Gets or sets the third color.
        /// </summary>
        public Color Color3
        {
            get => color3;
            set { color3 = value; RaisePropertyChanged(nameof(Color3)); }
        }

        private Color color4 = DefaultColor4;
        /// <summary>
        /// Gets or sets the fourth color.
        /// </summary>
        public Color Color4
        {
            get => color4;
            set { color4 = value; RaisePropertyChanged(nameof(Color4)); }
        }


        private bool cornerRadiusVal = true;
        public bool CornerRadiusVal
        {
            get { return cornerRadiusVal; }
            set { cornerRadiusVal = value; RaisePropertyChanged("CornerRadiusVal"); SetCornerRadius(); }
        }

        private CornerRadius cornerRadius = new CornerRadius(10);
        public CornerRadius CornerRadius
        {
            get { return cornerRadius; }
            set { cornerRadius = value; RaisePropertyChanged("CornerRadius"); }
        }

        private BoxShadows shadow = BoxShadows.Parse("1 1 4 1 Black");

        public BoxShadows Shadow
        {
            get { return shadow; }
            set { shadow = value; RaisePropertyChanged("Shadow"); }
        }

        private bool darkMode = true;
        public bool DarkMode
        {
            get { return darkMode; }
            set { darkMode = value; RaisePropertyChanged("DarkMode"); RaisePropertyChanged("Theme"); }
        }

        public ThemeVariant Theme
        {
            get 
            { 
                if (DarkMode)
                {
                    return ThemeVariant.Dark;
                }
                else
                {
                    return ThemeVariant.Light;
                }
            }
        }


        private bool shadowVal = false;
        public bool ShadowVal
        {
            get { return shadowVal; }
            set { shadowVal = value; RaisePropertyChanged("ShadowVal"); SetShadow(); }
        }

        private int fontSize = DefaultFontSize;
        public int FontSize
        {
            get { return fontSize; }
            set { fontSize = value; RaisePropertyChanged("FontSize"); RaisePropertyChanged("RowHeight");}
        }

        private string font = DefaultFontName;
        public string Font
        {
            get { return font; }
            set { font = value; RaisePropertyChanged("Font"); }
        }

        private bool trayNote = true;
        public bool TrayNote
        {
            get { return trayNote; }
            set { trayNote = value; RaisePropertyChanged("TrayNote"); }
        }

        private bool trayCollections = true;
        public bool TrayCollections
        {
            get { return trayCollections; }
            set { trayCollections = value; RaisePropertyChanged("TrayCollections"); }
        }

        private bool trayBookmarks = true;
        public bool TrayBookmarks
        {
            get { return trayBookmarks; }
            set { trayBookmarks = value; RaisePropertyChanged("TrayBookmarks"); }
        }

        private bool trayRecent = true;
        public bool TrayRecent
        {
            get { return trayRecent; }
            set { trayRecent = value; RaisePropertyChanged("TrayRecent"); }
        }

        private bool showIcons = true;
        public bool ShowIcons
        {
            get { return showIcons; }
            set { showIcons = value; RaisePropertyChanged("ShowIcons"); }
        }

        private ObservableCollection<string> collections = new ObservableCollection<string>();
        public ObservableCollection<string> Collections
        {
            get { return collections; }
            set { collections = value; RaisePropertyChanged("Collections"); }
        }

        private ObservableCollection<CalendarData> calendarList = new ObservableCollection<CalendarData>();

        public ObservableCollection<CalendarData> CalendarList
        {
            get { return calendarList; }
            set { calendarList = value; RaisePropertyChanged("CalendarList"); }
        }


        private ObservableCollection<TimeSheetProjectData> timeProjects = new ObservableCollection<TimeSheetProjectData>();

        public ObservableCollection<TimeSheetProjectData> TimeProjects
        {
            get { return timeProjects; }
            set { timeProjects = value; RaisePropertyChanged("TimeProjects"); }
        }

        private void SetCornerRadius()
        {
            if (CornerRadiusVal)
            {
                CornerRadius = new CornerRadius(10);
            }
            else
            {
                CornerRadius = new CornerRadius(0);
            }
        }

        private void SetShadow()
        {
            if (ShadowVal)
            {
                Shadow = BoxShadows.Parse("1 1 4 1 Black");
            }
            else
            {
                Shadow = new BoxShadows();
            }
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
