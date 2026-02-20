using System;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace Finn.Model
{
    /// <summary>
    /// Represents a calendar entry with notes, reminders, and timesheet data.
    /// </summary>
    public class CalendarData : INotifyPropertyChanged
    {

        public CalendarData()
        {
            // Ensure we notify when the timesheets collection changes
            timeSheets.CollectionChanged += TimeSheets_CollectionChanged;
        }

        private DateOnly date;
        /// <summary>
        /// Gets or sets the date for this calendar entry.
        /// </summary>
        public DateOnly Date
        {
            get => date;
            set
            {
                date = value;
                RaisePropertyChanged(nameof(Date));
                RaisePropertyChanged(nameof(DateString));
            }
        }

        /// <summary>
        /// Gets the week of the month for this date.
        /// </summary>
        [JsonIgnore]
        public int WeekOfMonth
        {
            get
            {
                int firstWeek = ISOWeek.GetWeekOfYear(new DateTime(Date.Year, Date.Month, 1));
                int currentWeek = ISOWeek.GetWeekOfYear(new DateTime(Date.Year, Date.Month, Date.Day));
                return currentWeek - firstWeek;
            }
        }

        /// <summary>
        /// Gets a string representation of the date, or " - " for weekends.
        /// </summary>
        [JsonIgnore]
        public string DateString
        {
            get
            {
                // Show month-day in MM-DD format
                string text = date.ToString("MM-dd");
                if (Date.DayOfWeek == DayOfWeek.Saturday || Date.DayOfWeek == DayOfWeek.Sunday)
                {
                    text = " - ";
                }
                return text;
            }
        }


        private string note1 = string.Empty;
        /// <summary>
        /// Gets or sets the first note.
        /// </summary>
        public string Note1
        {
            get => note1;
            set
            {
                note1 = value;
                RaisePropertyChanged(nameof(Note1));
                RaisePropertyChanged(nameof(DateString));
                RaisePropertyChanged(nameof(HasNote));
            }
        }

        private string note2 = string.Empty;
        /// <summary>
        /// Gets or sets the second note.
        /// </summary>
        public string Note2
        {
            get => note2;
            set
            {
                note2 = value;
                RaisePropertyChanged(nameof(Note2));
                RaisePropertyChanged(nameof(DateString));
                RaisePropertyChanged(nameof(HasNote));
            }
        }

        private string reminder = string.Empty;
        /// <summary>
        /// Gets or sets the reminder.
        /// </summary>
        public string Reminder
        {
            get => reminder;
            set
            {
                reminder = value;
                RaisePropertyChanged(nameof(Reminder));
                RaisePropertyChanged(nameof(DateString));
            }
        }

        /// <summary>
        /// Gets the total time from all timesheets, or null if none.
        /// </summary>
        [JsonIgnore]
        public int? TotalTime
        {
            get
            {
                int sum = TimeSheets.Select(x => x.Hours).Sum();
                return sum != 0 ? sum : null;
            }
        }

        private ObservableCollection<TimeSheetData> timeSheets = new();
        /// <summary>
        /// Gets or sets the collection of timesheets.
        /// </summary>
        public ObservableCollection<TimeSheetData> TimeSheets
        {
            get => timeSheets;
            set
            {
                if (ReferenceEquals(timeSheets, value))
                    return;

                if (timeSheets != null)
                    timeSheets.CollectionChanged -= TimeSheets_CollectionChanged;

                timeSheets = value ?? new ObservableCollection<TimeSheetData>();
                timeSheets.CollectionChanged += TimeSheets_CollectionChanged;

                RaisePropertyChanged(nameof(TimeSheets));
                RaisePropertyChanged(nameof(HasTime));
                RaisePropertyChanged(nameof(TotalTime));
                RaisePropertyChanged(nameof(DateString));
            }
        }

        private void TimeSheets_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(HasTime));
            RaisePropertyChanged(nameof(TotalTime));
            RaisePropertyChanged(nameof(DateString));
        }

        private string currentTimeSheetProjectDiary = string.Empty;
        /// <summary>
        /// Gets or sets the diary for the current timesheet project.
        /// </summary>
        [JsonIgnore]
        public string CurrentTimeSheetProjectDiary
        {
            get => currentTimeSheetProjectDiary;
            set { currentTimeSheetProjectDiary = value; RaisePropertyChanged(nameof(CurrentTimeSheetProjectDiary)); }
        }

        private int? currentTimeSheetProjectTime = null;
        /// <summary>
        /// Gets or sets the time for the current timesheet project.
        /// </summary>
        [JsonIgnore]
        public int? CurrentTimeSheetProjectTime
        {
            get => currentTimeSheetProjectTime;
            set { currentTimeSheetProjectTime = value; RaisePropertyChanged(nameof(CurrentTimeSheetProjectTime)); }
        }

        /// <summary>
        /// Triggers property change notifications for date string properties.
        /// </summary>
        public void TriggerDateStringUpdate()
        {
            RaisePropertyChanged(nameof(DateString));
        }

        /// <summary>
        /// Gets whether this entry has a note.
        /// </summary>
        [JsonIgnore]
        public bool HasNote => Note1.Length > 0 || Note2.Length > 0;

        /// <summary>
        /// Gets whether this entry has time data.
        /// </summary>
        [JsonIgnore]
        public bool HasTime => TimeSheets.Count > 0;


        /// <summary>
        /// Sets the diary and time for the current timesheet project.
        /// </summary>
        public void SetCurrentTimeSheetProjectDiary(string text)
        {
            if (TimeSheets.Any(x => x.Project == text))
            {
                string diary = string.Empty;
                int time = 0;
                foreach (TimeSheetData timeSheet in TimeSheets.Where(x => x.Project == text))
                {
                    diary += timeSheet.Diary;
                    time += timeSheet.Hours;
                }
                CurrentTimeSheetProjectDiary = diary;
                CurrentTimeSheetProjectTime = time;
            }
            else
            {
                CurrentTimeSheetProjectDiary = string.Empty;
                CurrentTimeSheetProjectTime = null;
            }
        }


        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
