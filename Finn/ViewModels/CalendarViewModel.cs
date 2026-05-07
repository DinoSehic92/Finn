using Finn.Model;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Finn.Storage;
using System.IO;
using Finn.Utils;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Finn.ViewModels
{
    /// <summary>
    /// Encapsulates calendar related state and logic extracted from MainViewModel.
    /// </summary>
    public class CalendarViewModel : ViewModelBase
    {
        private readonly Func<UISettingsViewModel> uiGetter;
        private readonly Dictionary<DateOnly, CalendarData> _dateIndex = new();

        private const string TOTAL_PROJECT = "Total";

        public CalendarViewModel(Func<UISettingsViewModel> uiGetter)
            : base(logger: null)
        {
            this.uiGetter = uiGetter;
            CalendarStorage = new CalendarStorage();
        }

        private UISettingsViewModel UI => uiGetter();

        private CalendarStorage calendarStorage = null!;
        public CalendarStorage CalendarStorage
        {
            get => calendarStorage;
            set => SetProperty(ref calendarStorage, value);
        }

        /// <summary>
        /// All calendar entries. Delegates to CalendarStorage but ensures a
        /// concrete collection is always returned (never creates throwaways).
        /// </summary>
        public ObservableCollection<CalendarData> CalendarList
        {
            get
            {
                CalendarStorage.CalendarList ??= [];
                return CalendarStorage.CalendarList;
            }
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
            get
            {
                CalendarStorage.TimeProjects ??= [];
                return CalendarStorage.TimeProjects;
            }
            set
            {
                if (CalendarStorage.TimeProjects != value)
                {
                    CalendarStorage.TimeProjects = value;
                    OnPropertyChanged(nameof(TimeProjects));
                }
            }
        }

        #region Persistence

        /// <summary>
        /// Loads calendar storage from Calendar.json. Creates a new file if none exists.
        /// </summary>
        public async Task LoadOrCreateStorageAsync(string savePath)
        {
            if (string.IsNullOrWhiteSpace(savePath)) return;

            try
            {
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                string file = Path.Combine(savePath, "Calendar.json");

                if (File.Exists(file))
                {
                    string json = await File.ReadAllTextAsync(file);
                    var cs = JsonHelper.Deserialize<CalendarStorage>(json);
                    if (cs != null)
                    {
                        CalendarStorage = cs;
                        CalendarStorage.CalendarList = new ObservableCollection<CalendarData>(CalendarStorage.CalendarList ?? []);
                        CalendarStorage.TimeProjects = new ObservableCollection<TimeSheetProjectData>(CalendarStorage.TimeProjects ?? []);
                        RebuildDateIndex();
                        OnPropertyChanged(nameof(CalendarStorage));
                        OnPropertyChanged(nameof(CalendarList));
                        OnPropertyChanged(nameof(TimeProjects));
                    }
                }
                else
                {
                    string json = JsonHelper.Serialize(CalendarStorage);
                    await File.WriteAllTextAsync(file, json);
                }

                SetCurrentCalendarData();
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarViewModel.LoadOrCreateStorageAsync");
            }

            EnsureTotalRow();
            OnPropertyChanged(nameof(SelectableProjects));
            OnPropertyChanged(nameof(SelectableProjectNames));
            var firstProject = SelectableProjects.FirstOrDefault();
            if (firstProject != null)
                NewEntryProject = firstProject.Project;
            RefreshProjectSummaries();
            RefreshProjectDiarySummary();
            DayIndicatorsChanged?.Invoke();
        }

        /// <summary>
        /// Saves the current CalendarStorage to Calendar.json using atomic write.
        /// </summary>
        public async Task SaveStorageAsync(string savePath)
        {
            try
            {
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                string file = Path.Combine(savePath, "Calendar.json");
                string tmpFile = file + ".tmp";
                string bakFile = file + ".bak";

                // Prune empty entries before saving to keep Calendar.json compact (D1)
                PruneEmptyEntries();

                string json = JsonHelper.Serialize(CalendarStorage);

                await File.WriteAllTextAsync(tmpFile, json);

                if (File.Exists(file))
                    File.Copy(file, bakFile, overwrite: true);

                File.Move(tmpFile, file, overwrite: true);
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarViewModel.SaveStorageAsync");
            }
        }

        #endregion

        #region Date selection

        private DateTime selectedDateTime = DateTime.Now;
        public DateTime SelectedDateTime
        {
            get => selectedDateTime;
            set
            {
                SetProperty(ref selectedDateTime, value, () =>
                {
                    SelectedWeek = ISOWeek.GetWeekOfYear(SelectedDateTime);
                    SetCurrentCalendarData();
                    OnPropertyChanged(nameof(SelectedYear));
                    OnPropertyChanged(nameof(SelectedMonth));
                    RefreshProjectSummaries();
                    RefreshProjectDiarySummary();
                    DayIndicatorsChanged?.Invoke();
                });
            }
        }

        public int SelectedYear => SelectedDateTime.Year;
        public int SelectedMonth => SelectedDateTime.Month;

        private int selectedWeek;
        public int SelectedWeek
        {
            get => selectedWeek;
            set => SetProperty(ref selectedWeek, value);
        }

        #endregion

        #region Current entry

        private CalendarData currentCalendarData = new();
        /// <summary>
        /// The calendar entry for the currently selected date.
        /// Subscribes to PropertyChanged so edits immediately refresh
        /// day indicator dots and promote transient entries to storage.
        /// Also tracks individual TimeSheetData changes for live grid updates.
        /// </summary>
        public CalendarData CurrentCalendarData
        {
            get => currentCalendarData;
            set
            {
                if (value == null) return;
                if (currentCalendarData != null)
                {
                    currentCalendarData.PropertyChanged -= OnCurrentEntryChanged;
                    UnsubscribeTimesheetItems(currentCalendarData);
                }
                SetProperty(ref currentCalendarData!, value, () =>
                {
                    if (currentCalendarData != null)
                    {
                        currentCalendarData.PropertyChanged += OnCurrentEntryChanged;
                        SubscribeTimesheetItems(currentCalendarData);
                    }
                    SyncSelectedDate();
                });
            }
        }

        private void SubscribeTimesheetItems(CalendarData cal)
        {
            cal.TimeSheets.CollectionChanged += OnTimesheetCollectionChanged;
            foreach (var ts in cal.TimeSheets)
                ts.PropertyChanged += OnTimesheetItemChanged;
        }

        private void UnsubscribeTimesheetItems(CalendarData cal)
        {
            cal.TimeSheets.CollectionChanged -= OnTimesheetCollectionChanged;
            foreach (var ts in cal.TimeSheets)
                ts.PropertyChanged -= OnTimesheetItemChanged;
        }

        private void OnTimesheetCollectionChanged(object? sender,
            System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Unsubscribe removed items
            if (e.OldItems != null)
                foreach (TimeSheetData ts in e.OldItems)
                    ts.PropertyChanged -= OnTimesheetItemChanged;

            // Subscribe new items
            if (e.NewItems != null)
                foreach (TimeSheetData ts in e.NewItems)
                    ts.PropertyChanged += OnTimesheetItemChanged;

            RefreshProjectSummaries();
            RefreshProjectDiarySummary();
            OnPropertyChanged(nameof(DailyTotalDisplay));
            DataChanged?.Invoke();
        }

        /// <summary>
        /// Fires when any property on a timesheet entry changes (hours, project, diary).
        /// Immediately refreshes both summary grids so edits are reflected live.
        /// </summary>
        private void OnTimesheetItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(TimeSheetData.Hours) or nameof(TimeSheetData.Project))
                RefreshProjectSummaries();
            if (e.PropertyName is nameof(TimeSheetData.Hours) or nameof(TimeSheetData.Project) or nameof(TimeSheetData.Diary))
                RefreshProjectDiarySummary();
            if (e.PropertyName is nameof(TimeSheetData.Hours))
                OnPropertyChanged(nameof(DailyTotalDisplay));
            DataChanged?.Invoke();
        }

        /// <summary>
        /// Unified handler for the current entry's property changes:
        /// - Promotes transient (not-yet-persisted) entries to CalendarList on first edit.
        /// - Fires DayIndicatorsChanged for note/time/reminder changes.
        /// </summary>
        private void OnCurrentEntryChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not CalendarData cal) return;

            // Promote transient entry to storage on first meaningful edit
            if (!_dateIndex.ContainsKey(cal.Date))
            {
                bool meaningful = e.PropertyName is nameof(CalendarData.Note1)
                    or nameof(CalendarData.Reminder) or nameof(CalendarData.TimeSheets);
                if (meaningful)
                {
                    CalendarList.Add(cal);
                    _dateIndex[cal.Date] = cal;
                }
            }

            // Refresh calendar dots
            if (e.PropertyName is nameof(CalendarData.HasNote) or nameof(CalendarData.HasTime)
                or nameof(CalendarData.Reminder))
            {
                DayIndicatorsChanged?.Invoke();
            }

            // Mark as dirty for any meaningful property change
            if (e.PropertyName is nameof(CalendarData.Note1) or nameof(CalendarData.Reminder)
                or nameof(CalendarData.HasTime))
            {
                DataChanged?.Invoke();
            }
        }

        /// <summary>
        /// Resolves the CalendarData entry for the currently selected date.
        /// Creates a transient entry if none exists yet (promoted on first edit).
        /// </summary>
        private void SetCurrentCalendarData()
        {
            var date = DateOnly.FromDateTime(SelectedDateTime);
            if (_dateIndex.TryGetValue(date, out var existing))
                CurrentCalendarData = existing;
            else
                CurrentCalendarData = new CalendarData { Date = date };

            // Reset grid selection to first entry for the new day (#8)
            CurrentTimeSheet = CurrentCalendarData.TimeSheets.Count > 0
                ? CurrentCalendarData.TimeSheets[0]
                : null!;
            OnPropertyChanged(nameof(DailyTotalDisplay));
        }

        /// <summary>
        /// Syncs SelectedDateTime when CurrentCalendarData is set externally
        /// (e.g. from a grid selection).
        /// </summary>
        private void SyncSelectedDate()
        {
            if (CurrentCalendarData != null
                && CurrentCalendarData.Date != DateOnly.FromDateTime(SelectedDateTime.Date))
            {
                SelectedDateTime = CurrentCalendarData.Date.ToDateTime(TimeOnly.Parse("10:00 PM"));
            }
        }

        #endregion

        #region Timesheet

        private TimeSheetData currentTimeSheet = new();
        public TimeSheetData CurrentTimeSheet
        {
            get => currentTimeSheet;
            set => SetProperty(ref currentTimeSheet, value);
        }

        private int newEntryHours = 1;
        /// <summary>Staging: hours value for the next entry to be added.</summary>
        public int NewEntryHours
        {
            get => newEntryHours;
            set => SetProperty(ref newEntryHours, value);
        }

        private string newEntryProject = string.Empty;
        /// <summary>Staging: project name for the next entry to be added.</summary>
        public string NewEntryProject
        {
            get => newEntryProject;
            set => SetProperty(ref newEntryProject, value);
        }

        /// <summary>Projects available for selection (excludes the Total summary row).</summary>
        public IEnumerable<TimeSheetProjectData> SelectableProjects
            => TimeProjects.Where(x => x.Project != TOTAL_PROJECT);

        /// <summary>Project name strings for inline ComboBox editing in the daily grid.</summary>
        public IEnumerable<string> SelectableProjectNames
            => TimeProjects.Where(x => x.Project != TOTAL_PROJECT).Select(x => x.Project);

        /// <summary>Display string for the daily total hours.</summary>
        public string DailyTotalDisplay
        {
            get
            {
                var total = CurrentCalendarData?.TotalTime;
                return total.HasValue ? $"Hours: {total.Value} / 8" : "";
            }
        }

        public ObservableCollection<int> Hours { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

        /// <summary>
        /// Refreshes the W1–W5 columns on each TimeProject so the inline
        /// summary grid in the calendar tray shows current monthly totals.
        /// </summary>
        public void RefreshProjectSummaries()
        {
            int month = SelectedDateTime.Month;
            int year = SelectedDateTime.Year;
            var entries = GetMonthEntries(year, month).ToList();

            foreach (var p in TimeProjects.Where(x => (x?.Project ?? string.Empty) != TOTAL_PROJECT))
            {
                var name = p?.Project ?? string.Empty;
                p!.W1 = SumProjectHoursForWeek(entries, 0, name);
                p.W2 = SumProjectHoursForWeek(entries, 1, name);
                p.W3 = SumProjectHoursForWeek(entries, 2, name);
                p.W4 = SumProjectHoursForWeek(entries, 3, name);
                p.W5 = SumProjectHoursForWeek(entries, 4, name);
            }

            var total = TimeProjects.FirstOrDefault(x => x.Project == TOTAL_PROJECT);
            if (total != null)
            {
                var nonTotal = TimeProjects.Where(x => x.Project != TOTAL_PROJECT).ToList();
                total.W1 = nonTotal.Sum(x => x.W1);
                total.W2 = nonTotal.Sum(x => x.W2);
                total.W3 = nonTotal.Sum(x => x.W3);
                total.W4 = nonTotal.Sum(x => x.W4);
                total.W5 = nonTotal.Sum(x => x.W5);
            }
        }

        private TimeSheetProjectData currentTimeSheetProject = new();
        private string? _trackedProjectName;
        public TimeSheetProjectData CurrentTimeSheetProject
        {
            get => currentTimeSheetProject;
            set
            {
                if (currentTimeSheetProject != null)
                    currentTimeSheetProject.PropertyChanged -= OnCurrentProjectPropertyChanged;

                _trackedProjectName = value?.Project;

                SetProperty(ref currentTimeSheetProject!, value, () =>
                {
                    if (currentTimeSheetProject != null)
                        currentTimeSheetProject.PropertyChanged += OnCurrentProjectPropertyChanged;
                    RefreshProjectDiarySummary();
                });
            }
        }

        /// <summary>
        /// When the selected project's name changes, rename all matching
        /// TimeSheetData entries across the entire calendar so summaries
        /// and diary entries stay in sync.
        /// </summary>
        private void OnCurrentProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimeSheetProjectData.Project) && _trackedProjectName != null)
            {
                string oldName = _trackedProjectName;
                string newName = CurrentTimeSheetProject.Project;
                if (oldName != newName && !string.IsNullOrWhiteSpace(newName))
                {
                    foreach (var cal in CalendarList)
                    {
                        foreach (var ts in cal.TimeSheets)
                        {
                            if (ts.Project == oldName)
                                ts.Project = newName;
                        }
                    }
                    _trackedProjectName = newName;
                    RefreshProjectSummaries();
                    RefreshProjectDiarySummary();
                    OnPropertyChanged(nameof(SelectableProjectNames));
                }
            }
        }

        public void NewTimeSheet()
        {
            if (CurrentCalendarData == null) SetCurrentCalendarData();
            if (CurrentCalendarData == null) return;

            string project = !string.IsNullOrWhiteSpace(NewEntryProject)
                ? NewEntryProject
                : SelectableProjects.FirstOrDefault()?.Project ?? "New";

            var entry = new TimeSheetData { Hours = NewEntryHours, Project = project };
            CurrentCalendarData.TimeSheets.Add(entry);
            CurrentTimeSheet = entry;
        }

        public void RemoveTimeSheet()
        {
            if (CurrentCalendarData == null || CurrentTimeSheet == null) return;
            CurrentCalendarData.TimeSheets.Remove(CurrentTimeSheet);
            CurrentTimeSheet = CurrentCalendarData.TimeSheets.Count > 0
                ? CurrentCalendarData.TimeSheets[^1]
                : null!;
        }

        /// <summary>
        /// Copies timesheet entries from the most recent previous day (up to 7 days back)
        /// that has entries. Copies hours and project, leaving diary empty.
        /// </summary>
        public void CopyFromPreviousDay()
        {
            if (CurrentCalendarData == null) SetCurrentCalendarData();
            if (CurrentCalendarData == null) return;

            var currentDate = DateOnly.FromDateTime(SelectedDateTime);
            for (int i = 1; i <= 7; i++)
            {
                var prevDate = currentDate.AddDays(-i);
                if (_dateIndex.TryGetValue(prevDate, out var prevDay) && prevDay.TimeSheets.Count > 0)
                {
                    foreach (var ts in prevDay.TimeSheets)
                    {
                        CurrentCalendarData.TimeSheets.Add(new TimeSheetData
                        {
                            Hours = ts.Hours,
                            Project = ts.Project
                        });
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Adds a new empty timesheet project entry before the Total row.
        /// The user edits the name and number inline via the bound text boxes.
        /// </summary>
        public void AddTimeProject()
        {
            EnsureTotalRow();

            // Generate a unique name to prevent duplicate project entries
            string baseName = "New";
            string name = baseName;
            int counter = 1;
            while (TimeProjects.Any(x => x.Project == name))
            {
                name = $"{baseName} {counter}";
                counter++;
            }

            int idx = TimeProjects.IndexOf(TimeProjects.First(x => x.Project == TOTAL_PROJECT));
            var newProject = new TimeSheetProjectData { Project = name };
            TimeProjects.Insert(idx, newProject);
            CurrentTimeSheetProject = newProject;
            RefreshProjectSummaries();
            OnPropertyChanged(nameof(SelectableProjects));
            OnPropertyChanged(nameof(SelectableProjectNames));
        }

        /// <summary>
        /// Removes the currently selected timesheet project.
        /// </summary>
        public void RemoveTimeProject()
        {
            if (CurrentTimeSheetProject == null || CurrentTimeSheetProject.Project == TOTAL_PROJECT) return;
            TimeProjects.Remove(CurrentTimeSheetProject);
            RefreshProjectSummaries();
            OnPropertyChanged(nameof(SelectableProjects));
            OnPropertyChanged(nameof(SelectableProjectNames));
        }

        /// <summary>
        /// Per-day diary rows for the selected project in the week that
        /// the selected date falls in. Shows one row per weekday with
        /// combined diary text for that project on that day.
        /// </summary>
        public ObservableCollection<WeekDiaryEntry> WeekDiaryEntries { get; } = [];

        /// <summary>
        /// Rebuilds the weekly diary entries for the currently selected project.
        /// Shows 5 rows (Mon–Fri) for the selected week, each with the combined
        /// diary text from all timesheet entries for that project on that day.
        /// </summary>
        private void RefreshProjectDiarySummary()
        {
            WeekDiaryEntries.Clear();

            var projName = CurrentTimeSheetProject?.Project;
            if (string.IsNullOrEmpty(projName) || projName == TOTAL_PROJECT)
                return;

            int year = SelectedDateTime.Year;
            int month = SelectedDateTime.Month;
            var selectedDate = DateOnly.FromDateTime(SelectedDateTime);

            // Use the same day-of-month arithmetic as CalendarData.WeekOfMonth
            // to avoid ISOWeek wraparound issues (e.g. Jan 1 in ISO week 52).
            int selectedWeekOfMonth = (selectedDate.Day - 1) / 7;

            var weekDays = GetMonthEntries(year, month)
                .Where(x => x.WeekOfMonth == selectedWeekOfMonth)
                .Where(x => x.Date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                .OrderBy(x => x.Date)
                .ToList();

            foreach (var day in weekDays)
            {
                var diaries = day.TimeSheets
                    .Where(ts => ts.Project == projName && !string.IsNullOrWhiteSpace(ts.Diary))
                    .Select(ts => ts.Diary.Trim())
                    .ToList();

                string diaryText = diaries.Count > 0 ? string.Join(", ", diaries) : string.Empty;

                WeekDiaryEntries.Add(new WeekDiaryEntry
                {
                    Day = day.Date.Day.ToString(),
                    Diary = diaryText
                });
            }
        }

        #endregion

        #region Project summary helpers

        private IEnumerable<CalendarData> GetMonthEntries(int year, int month)
        {
            foreach (var day in Enumerable.Range(1, DateTime.DaysInMonth(year, month)))
            {
                var date = new DateOnly(year, month, day);
                if (_dateIndex.TryGetValue(date, out var cd)) yield return cd;
            }
        }

        private static int SumProjectHoursForWeek(IEnumerable<CalendarData> entries, int weekOfMonth, string project)
            => entries.Where(x => x.WeekOfMonth == weekOfMonth)
                      .SelectMany(x => x.TimeSheets)
                      .Where(ts => ts.Project == project)
                      .Sum(ts => ts.Hours);

        #endregion

        #region Helpers

        public void ResetDate() => SelectedDateTime = DateTime.Now;

        /// <summary>
        /// Ensures a "Total" summary row always exists at the end of TimeProjects.
        /// </summary>
        private void EnsureTotalRow()
        {
            if (!TimeProjects.Any(x => x.Project == TOTAL_PROJECT))
                TimeProjects.Add(new TimeSheetProjectData { Project = TOTAL_PROJECT });
        }

        /// <summary>
        /// Returns per-day flags for the given date.
        /// </summary>
        public (bool HasNote, bool HasTime, bool HasReminder) GetDayInfo(DateOnly date)
        {
            if (_dateIndex.TryGetValue(date, out var cd))
                return (cd.HasNote, cd.HasTime, !string.IsNullOrWhiteSpace(cd.Reminder));
            return (false, false, false);
        }

        /// <summary>
        /// Raised when day data changes and the calendar day indicators should be refreshed.
        /// </summary>
        public event Action? DayIndicatorsChanged;

        /// <summary>
        /// Raised when calendar data is modified (timesheet edits, project changes, notes, etc.)
        /// so the host can mark the application as dirty.
        /// </summary>
        public event Action? DataChanged;

        private void RebuildDateIndex()
        {
            _dateIndex.Clear();
            foreach (var cd in CalendarList)
                _dateIndex[cd.Date] = cd;
        }

        /// <summary>
        /// Removes calendar entries that have no meaningful data (no notes, no reminders,
        /// no timesheets) to keep Calendar.json compact over time.
        /// </summary>
        private void PruneEmptyEntries()
        {
            var empties = CalendarList
                .Where(cd => !cd.HasNote && !cd.HasTime && string.IsNullOrWhiteSpace(cd.Reminder))
                .ToList();

            foreach (var empty in empties)
            {
                CalendarList.Remove(empty);
                _dateIndex.Remove(empty.Date);
            }
        }

        #endregion
    }
}
