using System;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
            // Ensure initial timesheet collection is hooked up via the property setter
            TimeSheets = timeSheets;
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
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Gets the week of the month for this date (0-based).
        /// Uses day-of-month arithmetic to avoid ISO week wraparound issues
        /// (e.g. January 1st in ISO week 52 of the previous year).
        /// </summary>
        [JsonIgnore]
        public int WeekOfMonth
        {
            get
            {
                // Week 0 = days 1–7, week 1 = days 8–14, etc.
                return (Date.Day - 1) / 7;
            }
        }

        private string note1 = string.Empty;
        /// <summary>
        /// Gets or sets the note.
        /// </summary>
        public string Note1
        {
            get => note1;
            set
            {
                note1 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasNote));
            }
        }

        private string note2 = string.Empty;
        /// <summary>
        /// Gets or sets the second note (legacy, kept for deserialization).
        /// </summary>
        public string Note2
        {
            get => note2;
            set
            {
                note2 = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasNote));
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
                OnPropertyChanged();
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
                int sum = TimeSheets.Sum(x => x.Hours);
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
                if (timeSheets != null)
                    timeSheets.CollectionChanged -= TimeSheets_CollectionChanged;

                timeSheets = value ?? new ObservableCollection<TimeSheetData>();
                timeSheets.CollectionChanged += TimeSheets_CollectionChanged;

                OnPropertyChanged(nameof(TimeSheets));
                OnPropertyChanged(nameof(HasTime));
                OnPropertyChanged(nameof(TotalTime));
            }
        }

        private void TimeSheets_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasTime));
            OnPropertyChanged(nameof(TotalTime));
        }

        /// <summary>
        /// Gets whether this entry has a note.
        /// </summary>
        [JsonIgnore]
        public bool HasNote => (note1 != null && note1.Length > 0) || (note2 != null && note2.Length > 0);

        /// <summary>
        /// Gets whether this entry has time data.
        /// </summary>
        [JsonIgnore]
        public bool HasTime => TimeSheets.Count > 0;


        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
