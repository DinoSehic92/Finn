using System;
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

        private DateOnly date;
        /// <summary>
        /// Gets or sets the date for this calendar entry.
        /// </summary>
        public DateOnly Date
        {
            get => date;
            set { date = value; RaisePropertyChanged(nameof(Date)); }
        }

        /// <summary>
        /// Gets the week of the month for this date.
        /// </summary>
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
        public string DateString
        {
            get
            {
                string text = date.ToString();
                if (Date.DayOfWeek == DayOfWeek.Saturday || Date.DayOfWeek == DayOfWeek.Sunday)
                {
                    text = " - ";
                }
                return text;
            }
        }

        /// <summary>
        /// Gets a string with icons for notes and time, or " - " for weekends.
        /// </summary>
        public string DateStringIcon
        {
            get
            {
                string text = date.ToString() + "⠀";
                if (HasNote)
                    text += "📝 ";
                if (HasTime)
                    text += "🕑 ";
                if (Date.DayOfWeek == DayOfWeek.Saturday || Date.DayOfWeek == DayOfWeek.Sunday)
                    text = " - ";
                else
                    text += " " + TotalTime;
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
            set { note1 = value; RaisePropertyChanged(nameof(Note1)); RaisePropertyChanged(nameof(DateString)); RaisePropertyChanged(nameof(DateStringIcon)); }
        }

        private string note2 = string.Empty;
        /// <summary>
        /// Gets or sets the second note.
        /// </summary>
        public string Note2
        {
            get => note2;
            set { note2 = value; RaisePropertyChanged(nameof(Note2)); RaisePropertyChanged(nameof(DateString)); RaisePropertyChanged(nameof(DateStringIcon)); }
        }

        private string reminder = string.Empty;
        /// <summary>
        /// Gets or sets the reminder.
        /// </summary>
        public string Reminder
        {
            get => reminder;
            set { reminder = value; RaisePropertyChanged(nameof(Reminder)); RaisePropertyChanged(nameof(DateString)); RaisePropertyChanged(nameof(DateStringIcon)); }
        }

        /// <summary>
        /// Gets the total time from all timesheets, or null if none.
        /// </summary>
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
            set { timeSheets = value; RaisePropertyChanged(nameof(TimeSheets)); }
        }

        private string currentTimeSheetProjectDiary = string.Empty;
        /// <summary>
        /// Gets or sets the diary for the current timesheet project.
        /// </summary>
        public string CurrentTimeSheetProjectDiary
        {
            get => currentTimeSheetProjectDiary;
            set { currentTimeSheetProjectDiary = value; RaisePropertyChanged(nameof(CurrentTimeSheetProjectDiary)); }
        }

        private int? currentTimeSheetProjectTime = null;
        /// <summary>
        /// Gets or sets the time for the current timesheet project.
        /// </summary>
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
            RaisePropertyChanged(nameof(DateStringIcon));
        }

        /// <summary>
        /// Gets whether this entry has a note.
        /// </summary>
        public bool HasNote => Note1.Length > 0 || Note2.Length > 0;

        /// <summary>
        /// Gets whether this entry has time data.
        /// </summary>
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
