using Finn.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Finn.Storage
{
    public class CalendarStorage : INotifyPropertyChanged
    {

        private string savePath = "C:\\FIlePathManager";

        public string SavePath
        {
            get => savePath;
            set { savePath = value; RaisePropertyChanged(nameof(SavePath)); }
        }

        public ObservableCollection<CalendarData>? calendarList = new ObservableCollection<CalendarData>();
        public ObservableCollection<CalendarData> CalendarList
        {
            get { return calendarList; }
            set { calendarList = value; RaisePropertyChanged(nameof(CalendarList)); }
        }

        public ObservableCollection<TimeSheetProjectData>? timeProjects = new ObservableCollection<TimeSheetProjectData>();
        public ObservableCollection<TimeSheetProjectData> TimeProjects
        {
            get { return timeProjects; }
            set { timeProjects = value; RaisePropertyChanged(nameof(TimeProjects)); }
        }

        private void RaisePropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
