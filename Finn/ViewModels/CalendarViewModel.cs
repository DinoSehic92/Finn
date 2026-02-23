using Finn.Model;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;
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

        private const string TOTAL_PROJECT = "Total";

        public CalendarViewModel(Func<UISettingsViewModel> uiGetter)
            : base(logger: null)
        {
            this.uiGetter = uiGetter;
        }

        private IEnumerable<CalendarData> GetMonthEntries(int year, int month)
        {
            return CalendarList.Where(x => x.Date.Year == year && x.Date.Month == month);
        }

        private int SumProjectHoursForWeek(IEnumerable<CalendarData> monthEntries, int weekOfMonth, string project)
        {
            return monthEntries
                   .Where(x => x.WeekOfMonth == weekOfMonth)
                   .SelectMany(x => x.TimeSheets)
                   .Where(ts => ts.Project == project)
                   .Sum(ts => ts.Hours);
        }

        private ObservableCollection<CalendarData> calendarList = new ObservableCollection<CalendarData>();

        public ObservableCollection<CalendarData> CalendarList
        {
            get => calendarList;
            set => SetProperty(ref calendarList, value);
        }

        private ObservableCollection<TimeSheetProjectData> timeProjects = new ObservableCollection<TimeSheetProjectData>();

        public ObservableCollection<TimeSheetProjectData> TimeProjects
        {
            get => timeProjects;
            set => SetProperty(ref timeProjects, value);
        }

        private UISettingsViewModel UI => uiGetter();


        private DateTime selectedDateTime = new();
        public DateTime SelectedDateTime
        {
            get => selectedDateTime;
            set => SetProperty(ref selectedDateTime, value, () => { UpdateMonthly(); SetCurrentCalendarData(); });
        }

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
            CurrentCalendarData.TimeSheets.Add(new TimeSheetData() { Hours = 1, Project = "New" });
            CurrentCalendarData.TriggerDateStringUpdate();
        }

        public void RemoveTimeSheet()
        {
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
            if (!UI.TimeSheetOpen || CurrentCalendarData == null)
                return;

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

        public void RemoveCalendarNote()
        {
            CalendarList.Remove(CurrentCalendarData);
            CurrentCalendarData = CalendarList.FirstOrDefault() ?? new CalendarData();
            UpdateMonthly();
        }

        public void ResetDate()
        {
            SelectedDateTime = DateTime.Now;
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
