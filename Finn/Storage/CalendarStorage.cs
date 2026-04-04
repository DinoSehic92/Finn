using CommunityToolkit.Mvvm.ComponentModel;
using Finn.Model;
using System.Collections.ObjectModel;

namespace Finn.Storage
{
    public class CalendarStorage : ObservableObject
    {
        private ObservableCollection<CalendarData> calendarList = new ObservableCollection<CalendarData>();
        public ObservableCollection<CalendarData> CalendarList
        {
            get { return calendarList; }
            set { calendarList = value ?? new ObservableCollection<CalendarData>(); OnPropertyChanged(nameof(CalendarList)); }
        }

        private ObservableCollection<TimeSheetProjectData> timeProjects = new ObservableCollection<TimeSheetProjectData>();
        public ObservableCollection<TimeSheetProjectData> TimeProjects
        {
            get { return timeProjects; }
            set { timeProjects = value ?? new ObservableCollection<TimeSheetProjectData>(); OnPropertyChanged(nameof(TimeProjects)); }
        }
    }
}
