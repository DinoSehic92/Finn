using Finn.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Finn.Storage
{
    public class CalendarStorage : INotifyPropertyChanged
    {
        private ObservableCollection<CalendarData>? calendarList = new ObservableCollection<CalendarData>();
        public ObservableCollection<CalendarData> CalendarList
        {
            get { return calendarList; }
            set { calendarList = value; RaisePropertyChanged(nameof(CalendarList)); }
        }

        private ObservableCollection<TimeSheetProjectData>? timeProjects = new ObservableCollection<TimeSheetProjectData>();
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
