using Finn.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Finn.Storage
{
    public class CalendarStorage : INotifyPropertyChanged
    {
        private ObservableCollection<CalendarData> calendarList = new ObservableCollection<CalendarData>();
        public ObservableCollection<CalendarData> CalendarList
        {
            get { return calendarList; }
            set { calendarList = value ?? new ObservableCollection<CalendarData>(); RaisePropertyChanged(nameof(CalendarList)); }
        }

        private ObservableCollection<TimeSheetProjectData> timeProjects = new ObservableCollection<TimeSheetProjectData>();
        public ObservableCollection<TimeSheetProjectData> TimeProjects
        {
            get { return timeProjects; }
            set { timeProjects = value ?? new ObservableCollection<TimeSheetProjectData>(); RaisePropertyChanged(nameof(TimeProjects)); }
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
