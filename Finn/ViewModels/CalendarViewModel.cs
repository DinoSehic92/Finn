using Finn.Model;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Finn.Storage;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;

namespace Finn.ViewModels
{
    /// <summary>
    /// Encapsulates calendar related state and logic extracted from MainViewModel.
    /// </summary>
    public class CalendarViewModel : ViewModelBase
    {
        private readonly Func<UISettingsViewModel> uiGetter;
        // Fast lookup by date to avoid scanning the full CalendarList
        private readonly Dictionary<DateOnly, CalendarData> _dateIndex = new();

        private const string TOTAL_PROJECT = "Total";

        public CalendarViewModel(Func<UISettingsViewModel> uiGetter)
            : base(logger: null)
        {
            this.uiGetter = uiGetter;
            CalendarStorage = new CalendarStorage();
        }

        private UISettingsViewModel UI => uiGetter();

        private CalendarStorage calendarStorage = new CalendarStorage();
        public CalendarStorage CalendarStorage
        {
            get => calendarStorage;
            set => SetProperty(ref calendarStorage, value);
        }

        // Expose collections for binding (delegates to CalendarStorage)
        public ObservableCollection<CalendarData> CalendarList
        {
            get => CalendarStorage.CalendarList ?? new ObservableCollection<CalendarData>();
            set
            {
                if (CalendarStorage.CalendarList != value)
                {
                    CalendarStorage.CalendarList = value;
                    OnPropertyChanged(nameof(CalendarList));
                    RebuildDateIndex();
                }
            }
        }

        public ObservableCollection<TimeSheetProjectData> TimeProjects
        {
            get => CalendarStorage.TimeProjects ?? new ObservableCollection<TimeSheetProjectData>();
            set
            {
                if (CalendarStorage.TimeProjects != value)
                {
                    CalendarStorage.TimeProjects = value;
                    OnPropertyChanged(nameof(TimeProjects));
                }
            }
        }

        /// <summary>
        /// Loads calendar storage from the provided save path (Calendar.json). If the file
        /// does not exist a new file will be created from the current in-memory storage.
        /// </summary>
        public void LoadOrCreateStorage(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath)) return;

            try
            {
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                string file = Path.Combine(savePath, "Calendar.json");

                if (File.Exists(file))
                {
                    string json = File.ReadAllText(file);
                    var cs = JsonConvert.DeserializeObject<CalendarStorage>(json);
                    if (cs != null)
                    {
                        CalendarStorage = cs;
                        // Ensure collections are concrete ObservableCollections
                        CalendarStorage.CalendarList = new ObservableCollection<CalendarData>(CalendarStorage.CalendarList ?? new ObservableCollection<CalendarData>());
                        CalendarStorage.TimeProjects = new ObservableCollection<TimeSheetProjectData>(CalendarStorage.TimeProjects ?? new ObservableCollection<TimeSheetProjectData>());
                        // Notify bindings
                        OnPropertyChanged(nameof(CalendarStorage));
                        OnPropertyChanged(nameof(CalendarList));
                        OnPropertyChanged(nameof(TimeProjects));

                        // Ensure monthly view and current item reflect loaded storage
                        try
                        {
                            UpdateMonthly();
                            SetCurrentCalendarData();
                        }
                        catch { }
                    }
                }
                else
                {
                    string json = JsonConvert.SerializeObject(CalendarStorage, Formatting.Indented);
                    File.WriteAllText(file, json);
                }
            }
            catch
            {
                // ignore IO/parse errors — do not crash UI thread
            }
        }

        /// <summary>
        /// Saves the current <see cref="CalendarStorage"/> to Calendar.json under <paramref name="savePath"/>.
        /// </summary>
        public void SaveStorage()
        {
            string savePath = CalendarStorage.SavePath;
            Debug.WriteLine("Saving Calendar");
            try
            {
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                string file = Path.Combine(savePath, "Calendar.json");
                string json = JsonConvert.SerializeObject(CalendarStorage, Formatting.Indented);
                File.WriteAllText(file, json);
            }
            catch
            {
                // ignore IO/parse errors — do not crash UI thread
            }
        }

        private IEnumerable<CalendarData> GetMonthEntries(int year, int month)
        {
            // Use index lookup to construct month entries efficiently by iterating days in month
            var dates = GetDates(year, month).Select(d => DateOnly.FromDateTime(d));
            foreach (var date in dates)
            {
                if (_dateIndex.TryGetValue(date, out var cd)) yield return cd;
            }
        }

        private int SumProjectHoursForWeek(IEnumerable<CalendarData> monthEntries, int weekOfMonth, string project)
        {
            return monthEntries
                   .Where(x => x.WeekOfMonth == weekOfMonth)
                   .SelectMany(x => x.TimeSheets)
                   .Where(ts => ts.Project == project)
                   .Sum(ts => ts.Hours);
        }

        // Rebuild the date index from the current CalendarList
        private void RebuildDateIndex()
        {
            _dateIndex.Clear();
            foreach (var cd in CalendarList)
            {
                try
                {
                    _dateIndex[cd.Date] = cd;
                }
                catch { }
            }
        }

        private DateTime selectedDateTime = new();
        public DateTime SelectedDateTime
        {
            get => selectedDateTime;
            set
            {
                var prevYear = selectedDateTime.Year;
                var prevMonth = selectedDateTime.Month;
                SetProperty(ref selectedDateTime, value, () =>
                {
                    // If month or year changed, ensure backing CalendarList contains all days for the new month
                    if (prevYear != SelectedDateTime.Year || prevMonth != SelectedDateTime.Month)
                    {
                        EnsureMonthEntries(SelectedDateTime.Year, SelectedDateTime.Month);
                    }

                    UpdateMonthly();
                    SetCurrentCalendarData();
                    OnPropertyChanged(nameof(SelectedYear));
                    OnPropertyChanged(nameof(SelectedMonth));
                });
            }
        }

        public int SelectedYear => SelectedDateTime.Year;
        public int SelectedMonth => SelectedDateTime.Month;

        private int selectedWeek = 0;
        public int SelectedWeek
        {
            get => selectedWeek;
            set => SetProperty(ref selectedWeek, value);
        }

        private ObservableCollection<CalendarData> monthlyNotes = new();
        public ObservableCollection<CalendarData> MonthlyNotes
        {
            get => monthlyNotes;
            set => SetProperty(ref monthlyNotes, value);
        }

        private CalendarData currentCalendarData = new();
        public CalendarData CurrentCalendarData
        {
            get => currentCalendarData;
            set => SetProperty(ref currentCalendarData, value, () => { SelectDateTime(); });
        }

        // Weak subscription to transient calendar entry
        private CalendarData? _subscribedCalendarData;

        private TimeSheetData currentTimeSheet = new();
        public TimeSheetData CurrentTimeSheet
        {
            get => currentTimeSheet;
            set => SetProperty(ref currentTimeSheet, value);
        }

        private ObservableCollection<int> hours = new() { 1, 2, 3, 4, 5, 6, 7, 8 };
        public ObservableCollection<int> Hours
        {
            get => hours;
            set => SetProperty(ref hours, value);
        }

        private TimeSheetProjectData currentTimeSheetProject = new();
        public TimeSheetProjectData CurrentTimeSheetProject
        {
            get => currentTimeSheetProject;
            set => SetProperty(ref currentTimeSheetProject, value);
        }

        public void NewTimeSheet()
        {
            // Ensure we have a current calendar entry before mutating it
            if (CurrentCalendarData == null)
            {
                SetCurrentCalendarData();
            }

            if (CurrentCalendarData == null)
                return;

            CurrentCalendarData.TimeSheets.Add(new TimeSheetData() { Hours = 1, Project = "New" });
            CurrentCalendarData.TriggerDateStringUpdate();
        }

        public void RemoveTimeSheet()
        {
            if (CurrentCalendarData == null || CurrentTimeSheet == null)
                return;

            CurrentCalendarData.TimeSheets.Remove(CurrentTimeSheet);
            CurrentCalendarData.TriggerDateStringUpdate();
        }

        public void UpdateTimeSheetSummary()
        {
            // Guard against null CurrentTimeSheetProject (can happen during edits)
            var projectName = CurrentTimeSheetProject?.Project ?? string.Empty;
            foreach (CalendarData calendarData in MonthlyNotes)
            {
                calendarData.SetCurrentTimeSheetProjectDiary(projectName);
            }
        }

        public void WeeklyTimeSummary()
        {
            // Guard against no timesheet UI
            if (!UI.TimeSheetOpen)
                return;

            // Ensure we have a valid current calendar entry. If CurrentCalendarData is missing
            // or has a default Date, try to resolve it from SelectedDateTime or CalendarList.
            if (CurrentCalendarData == null || CurrentCalendarData.Date == default)
            {
                SetCurrentCalendarData();
            }

            if (CurrentCalendarData == null || CurrentCalendarData.Date == default)
            {
                // Fallback: try to find entries for the selected date
                var selDate = DateOnly.FromDateTime(SelectedDateTime);
                var existing = CalendarList.FirstOrDefault(x => x.Date == selDate);
                if (existing != null)
                {
                    CurrentCalendarData = existing;
                }
                else
                {
                    return;
                }
            }

            // Cache month entries to avoid repeated LINQ scans
            int month = CurrentCalendarData.Date.Month;
            int year = CurrentCalendarData.Date.Year;
            var monthEntries = GetMonthEntries(year, month);

            foreach (TimeSheetProjectData project in TimeProjects.Where(x => (x?.Project ?? string.Empty) != TOTAL_PROJECT))
            {
                var projName = project?.Project ?? string.Empty;
                project.W1 = SumProjectHoursForWeek(monthEntries, 0, projName);
                project.W2 = SumProjectHoursForWeek(monthEntries, 1, projName);
                project.W3 = SumProjectHoursForWeek(monthEntries, 2, projName);
                project.W4 = SumProjectHoursForWeek(monthEntries, 3, projName);
                project.W5 = SumProjectHoursForWeek(monthEntries, 4, projName);
            }

            TimeSheetProjectData summarySheet = TimeProjects.FirstOrDefault(x => x.Project == TOTAL_PROJECT);
            if (summarySheet != null)
            {
                var nonTotal = TimeProjects.Where(x => x.Project != TOTAL_PROJECT).ToList();
                summarySheet.W1 = nonTotal.Sum(x => x.W1);
                summarySheet.W2 = nonTotal.Sum(x => x.W2);
                summarySheet.W3 = nonTotal.Sum(x => x.W3);
                summarySheet.W4 = nonTotal.Sum(x => x.W4);
                summarySheet.W5 = nonTotal.Sum(x => x.W5);
            }
        }

        private void UpdateMonthly()
        {
            // Refresh the monthly notes collection from the CalendarList
            SelectedWeek = ISOWeek.GetWeekOfYear(SelectedDateTime);
            MonthlyNotes = new ObservableCollection<CalendarData>(CalendarList.Where(x => x.Date.Month == SelectedDateTime.Month && x.Date.Year == SelectedDateTime.Year).OrderBy(x => x.Date));
        }

        private void SetCurrentCalendarData()
        {
            if (SelectedDateTime != null)
            {
                var date = DateOnly.FromDateTime(SelectedDateTime);
                var existing = CalendarList.FirstOrDefault(x => x.Date == date);
                if (existing != null)
                {
                    CurrentCalendarData = existing;
                    _subscribedCalendarData = existing;
                }
                else
                {
                    CalendarData transient = new() { Date = date };
                    CurrentCalendarData = transient;
                    _subscribedCalendarData = transient;

                    transient.PropertyChanged += TransientCalendar_PropertyChanged;
                }
            }
        }

        public void SetCalendarMonth()
        {
            foreach (DateTime datetime in GetDates(SelectedDateTime.Year, SelectedDateTime.Month))
            {
                DateOnly date = DateOnly.FromDateTime(datetime);
                if (!CalendarList.Any(x => x.Date == date)) CalendarList.Add(new CalendarData() { Date = date });
            }

            UpdateMonthly();
        }

        private void TransientCalendar_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is CalendarData cal && _subscribedCalendarData == cal)
            {
                if (e.PropertyName == nameof(CalendarData.Note1) || e.PropertyName == nameof(CalendarData.Note2) ||
                    e.PropertyName == nameof(CalendarData.Reminder) || e.PropertyName == nameof(CalendarData.TimeSheets))
                {
                    try { cal.PropertyChanged -= TransientCalendar_PropertyChanged; } catch { }

                    if (!CalendarList.Any(x => x.Date == cal.Date)) CalendarList.Add(cal);
                    _subscribedCalendarData = null;
                }
            }
        }

        public static List<DateTime> GetDates(int year, int month)
        {
            return Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                             .Select(day => new DateTime(year, month, day))
                             .ToList();
        }

        public void ResetDate()
        {
            SelectedDateTime = DateTime.Now;
        }

        // Ensure the calendar backing list has an entry for every day in the specified month/year
        private void EnsureMonthEntries(int year, int month)
        {
            if (CalendarStorage.CalendarList == null)
                CalendarStorage.CalendarList = new ObservableCollection<CalendarData>();

            var days = GetDates(year, month).Select(d => DateOnly.FromDateTime(d)).ToList();

            foreach (var day in days)
            {
                if (!CalendarList.Any(x => x.Date == day))
                {
                    // insert sorted to maintain order
                    int insertAt = 0;
                    while (insertAt < CalendarList.Count && CalendarList[insertAt].Date.CompareTo(day) < 0)
                        insertAt++;
                    CalendarList.Insert(insertAt, new CalendarData { Date = day });
                }
            }
        }

        private void SelectDateTime()
        {
            if (CurrentCalendarData != null)
            {
                if (CurrentCalendarData.Date != DateOnly.FromDateTime(SelectedDateTime.Date))
                {
                    SelectedDateTime = CurrentCalendarData.Date.ToDateTime(TimeOnly.Parse("10:00 PM"));
                }
            }
        }
    }
}
