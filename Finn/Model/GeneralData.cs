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

        private string savePath = "C:\\FIlePathManager";
        /// <summary>
        /// Gets or sets the save path.
        /// </summary>
        public string SavePath
        {
            get => savePath;
            set { savePath = value; RaisePropertyChanged(nameof(SavePath)); }
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

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
