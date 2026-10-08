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
        private Dictionary<Guid, TimeSheetProjectData> _projectById = new();

        private const string TOTAL_PROJECT = "Total";
        private bool storageLoaded;

        public bool StorageLoaded => storageLoaded;

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
            storageLoaded = false;
            if (string.IsNullOrWhiteSpace(savePath)) return;

            try
            {
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
                string file = Path.Combine(savePath, "Calendar.json");

                if (File.Exists(file))
                {
                    string json = await File.ReadAllTextAsync(file);
                    var cs = JsonHelper.Deserialize<CalendarStorage>(json);
                    if (cs == null)
                        throw new InvalidDataException($"Calendar data in '{file}' could not be deserialized.");

                    CalendarStorage = cs;
                    CalendarStorage.CalendarList = new ObservableCollection<CalendarData>(CalendarStorage.CalendarList ?? []);
                    CalendarStorage.TimeProjects = new ObservableCollection<TimeSheetProjectData>(CalendarStorage.TimeProjects ?? []);
                    RebuildDateIndex();
                    OnPropertyChanged(nameof(CalendarStorage));
                    OnPropertyChanged(nameof(CalendarList));
                    OnPropertyChanged(nameof(TimeProjects));
                }
                else
                {
                    string json = JsonHelper.Serialize(CalendarStorage);
                    await File.WriteAllTextAsync(file, json);
                }

                storageLoaded = true;
                SetCurrentCalendarData();
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarViewModel.LoadOrCreateStorageAsync");
                StatusMessageRequested?.Invoke("Calendar data could not be loaded. The existing file will not be overwritten.");
                return;
            }

            EnsureTotalRow();
            RebuildProjectIndex();
            HydrateProjectDisplayNames();
            var firstProject = SelectableProjects.FirstOrDefault();
            if (firstProject != null)
                NewEntryProjectId = firstProject.Id;
            RefreshProjectSummaries();
            RefreshProjectDiarySummary();
            DayIndicatorsChanged?.Invoke();
        }

        /// <summary>
        /// Saves the current CalendarStorage to Calendar.json using atomic write.
        /// </summary>
        public async Task<bool> SaveStorageAsync(string savePath)
        {
            if (!storageLoaded || string.IsNullOrWhiteSpace(savePath))
                return false;

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
                return true;
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarViewModel.SaveStorageAsync");
                return false;
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
            // Guard against double-subscription when the same cached instance is set twice.
            cal.TimeSheets.CollectionChanged -= OnTimesheetCollectionChanged;
            cal.TimeSheets.CollectionChanged += OnTimesheetCollectionChanged;
            foreach (var ts in cal.TimeSheets)
            {
                ts.PropertyChanged -= OnTimesheetItemChanged;
                ts.PropertyChanged += OnTimesheetItemChanged;
            }
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
            // Promote transient entry to storage when timesheets are added
            if (sender is ObservableCollection<TimeSheetData> col && col.Count > 0
                && CurrentCalendarData != null && !_dateIndex.ContainsKey(CurrentCalendarData.Date))
            {
                CalendarList.Add(CurrentCalendarData);
                _dateIndex[CurrentCalendarData.Date] = CurrentCalendarData;
            }

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
            NotifyDailyTotalChanged();
            DataChanged?.Invoke();
        }

        private void NotifyDailyTotalChanged()
        {
            OnPropertyChanged(nameof(DailyTotalDisplay));
            OnPropertyChanged(nameof(IsDailyTotalExceeded));
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
                NotifyDailyTotalChanged();
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
                : null;
            NotifyDailyTotalChanged();
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
                SelectedDateTime = CurrentCalendarData.Date.ToDateTime(new TimeOnly(22, 0));
            }
        }

        #endregion

        #region Timesheet

        private TimeSheetData? currentTimeSheet;
        public TimeSheetData? CurrentTimeSheet
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

        private Guid? newEntryProjectId;
        /// <summary>Staging: project Id for the next entry to be added.</summary>
        public Guid? NewEntryProjectId
        {
            get => newEntryProjectId;
            set => SetProperty(ref newEntryProjectId, value);
        }

        /// <summary>Projects available for selection (excludes the Total summary row).</summary>
        public IEnumerable<TimeSheetProjectData> SelectableProjects
            => TimeProjects.Where(x => x.Project != TOTAL_PROJECT);

        /// <summary>Resolves a project entry by its stable Id, or null if unknown.</summary>
        public TimeSheetProjectData? GetProjectById(Guid? id)
            => id.HasValue && _projectById.TryGetValue(id.Value, out var p) ? p : null;

        /// <summary>Display name for an entry's project, resolved via ProjectId.</summary>
        public string GetProjectName(Guid? id) => GetProjectById(id)?.Project ?? string.Empty;

        /// <summary>
        /// Rebuilds the Id → project lookup. Call whenever TimeProjects membership changes.
        /// </summary>
        private void RebuildProjectIndex()
        {
            _projectById = TimeProjects
                .Where(x => x != null)
                .GroupBy(x => x.Id)
                .ToDictionary(g => g.Key, g => g.First());
        }

        /// <summary>
        /// Populates the non-persisted Project display cache on each entry from the
        /// catalog, so the grid can show names after a fresh load.
        /// </summary>
        private void HydrateProjectDisplayNames()
        {
            foreach (var cal in CalendarList)
                foreach (var ts in cal.TimeSheets)
                    ts.SetProject(ts.ProjectId, GetProjectName(ts.ProjectId));
        }

        /// <summary>Display string for the daily total hours.</summary>
        public string DailyTotalDisplay
        {
            get
            {
                var total = CurrentCalendarData?.TotalTime;
                return total.HasValue ? $"Hours: {total.Value} / 8" : "";
            }
        }

        /// <summary>True when the day's logged hours exceed the 8-hour target.</summary>
        public bool IsDailyTotalExceeded => (CurrentCalendarData?.TotalTime ?? 0) > 8;

        public ObservableCollection<int> Hours { get; } = new(Enumerable.Range(0, 25));

        /// <summary>
        /// Refreshes the W1–W5 columns on each TimeProject so the inline
        /// summary grid in the calendar tray shows current monthly totals.
        /// </summary>
        public void RefreshProjectSummaries()
        {
            int month = SelectedDateTime.Month;
            int year = SelectedDateTime.Year;
            var selectedDate = DateOnly.FromDateTime(SelectedDateTime);
            int selectedWeekOfMonth = Math.Min(CalendarData.GetWeekOfMonth(selectedDate), 4);

            var entries = GetSelectedWorkWeekDates(selectedDate)
                .Select(date => _dateIndex.TryGetValue(date, out var entry) ? entry : null)
                .Where(entry => entry != null)
                .Cast<CalendarData>()
                .ToList();

            foreach (var p in TimeProjects.Where(x => (x?.Project ?? string.Empty) != TOTAL_PROJECT))
            {
                var pid = p!.Id;
                int weekHours = SumProjectHours(entries, pid);
                p.W1 = selectedWeekOfMonth == 0 ? weekHours : 0;
                p.W2 = selectedWeekOfMonth == 1 ? weekHours : 0;
                p.W3 = selectedWeekOfMonth == 2 ? weekHours : 0;
                p.W4 = selectedWeekOfMonth == 3 ? weekHours : 0;
                p.W5 = selectedWeekOfMonth == 4 ? weekHours : 0;
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

        private TimeSheetProjectData? currentTimeSheetProject;
        public TimeSheetProjectData? CurrentTimeSheetProject
        {
            get => currentTimeSheetProject;
            set
            {
                if (currentTimeSheetProject != null)
                    currentTimeSheetProject.PropertyChanged -= OnCurrentProjectPropertyChanged;

                SetProperty(ref currentTimeSheetProject, value, () =>
                {
                    if (currentTimeSheetProject != null)
                        currentTimeSheetProject.PropertyChanged += OnCurrentProjectPropertyChanged;
                    OnPropertyChanged(nameof(CurrentTimeSheetProjectName));
                    RefreshProjectDiarySummary();
                });
            }
        }

        public string CurrentTimeSheetProjectName
        {
            get => CurrentTimeSheetProject?.Project ?? string.Empty;
            set
            {
                if (CurrentTimeSheetProject == null)
                    return;

                string name = value.Trim();
                if (string.IsNullOrWhiteSpace(name)
                    || string.Equals(name, TOTAL_PROJECT, StringComparison.OrdinalIgnoreCase)
                    || TimeProjects.Any(project => project.Id != CurrentTimeSheetProject.Id
                        && string.Equals(project.Project, name, StringComparison.OrdinalIgnoreCase)))
                {
                    StatusMessageRequested?.Invoke("Project names must be non-empty, unique, and cannot be 'Total'.");
                    OnPropertyChanged();
                    return;
                }

                CurrentTimeSheetProject.Project = name;
            }
        }

        /// <summary>
        /// When the selected project's name changes, entries reference the project by
        /// stable Id, so no row needs rewriting — only the display-facing caches and
        /// summaries need to refresh to show the new name.
        /// </summary>
        private void OnCurrentProjectPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimeSheetProjectData.Project))
            {
                foreach (var entry in CalendarList.SelectMany(day => day.TimeSheets)
                             .Where(entry => entry.ProjectId == CurrentTimeSheetProject?.Id))
                    entry.SetProject(entry.ProjectId, CurrentTimeSheetProject.Project);

                RefreshProjectSummaries();
                RefreshProjectDiarySummary();
                OnPropertyChanged(nameof(CurrentTimeSheetProjectName));
                DataChanged?.Invoke();
            }
            else if (e.PropertyName == nameof(TimeSheetProjectData.ProjectNr))
            {
                DataChanged?.Invoke();
            }
        }

        public void NewTimeSheet()
        {
            if (CurrentCalendarData == null) SetCurrentCalendarData();
            if (CurrentCalendarData == null) return;

            var project = GetProjectById(NewEntryProjectId) ?? SelectableProjects.FirstOrDefault();

            var entry = new TimeSheetData { Hours = NewEntryHours };
            entry.SetProject(project?.Id, project?.Project ?? string.Empty);
            CurrentCalendarData.TimeSheets.Add(entry);
            CurrentTimeSheet = entry;
        }

        public void RemoveTimeSheet()
        {
            if (CurrentCalendarData == null || CurrentTimeSheet == null) return;
            CurrentCalendarData.TimeSheets.Remove(CurrentTimeSheet);
            CurrentTimeSheet = CurrentCalendarData.TimeSheets.Count > 0
                ? CurrentCalendarData.TimeSheets[^1]
                : null;
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
            if (CurrentCalendarData.TimeSheets.Count > 0)
            {
                StatusMessageRequested?.Invoke("The selected day already has timesheet entries; copy-forward was skipped.");
                return;
            }

            for (int i = 1; i <= 7; i++)
            {
                var prevDate = currentDate.AddDays(-i);
                if (_dateIndex.TryGetValue(prevDate, out var prevDay) && prevDay.TimeSheets.Count > 0)
                {
                    foreach (var ts in prevDay.TimeSheets)
                    {
                        var copy = new TimeSheetData { Hours = ts.Hours };
                        copy.SetProject(ts.ProjectId, ts.Project);
                        CurrentCalendarData.TimeSheets.Add(copy);
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
            while (TimeProjects.Any(x => string.Equals(x.Project, name, StringComparison.OrdinalIgnoreCase)))
            {
                name = $"{baseName} {counter}";
                counter++;
            }

            int idx = TimeProjects.IndexOf(TimeProjects.First(x => x.Project == TOTAL_PROJECT));
            var newProject = new TimeSheetProjectData { Project = name };
            TimeProjects.Insert(idx, newProject);
            OnPropertyChanged(nameof(SelectableProjects));
            _projectById[newProject.Id] = newProject;
            CurrentTimeSheetProject = newProject;
            RefreshProjectSummaries();
            DataChanged?.Invoke();
        }

        /// <summary>
        /// Removes the currently selected timesheet project.
        /// </summary>
        public void RemoveTimeProject()
        {
            if (CurrentTimeSheetProject == null || CurrentTimeSheetProject.Project == TOTAL_PROJECT) return;
            var removed = CurrentTimeSheetProject;

            int referencingEntries = CalendarList
                .SelectMany(day => day.TimeSheets)
                .Count(entry => entry.ProjectId == removed.Id);
            if (referencingEntries > 0)
            {
                StatusMessageRequested?.Invoke($"Cannot remove '{removed.Project}': {referencingEntries} timesheet entr{(referencingEntries == 1 ? "y refers" : "ies refer")} to it.");
                return;
            }

            int removedIndex = TimeProjects.IndexOf(removed);
            TimeProjects.Remove(removed);
            OnPropertyChanged(nameof(SelectableProjects));
            _projectById.Remove(removed.Id);
            CurrentTimeSheetProject = SelectableProjects
                .ElementAtOrDefault(Math.Min(removedIndex, SelectableProjects.Count() - 1));
            RefreshProjectSummaries();
            DataChanged?.Invoke();
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

            var proj = CurrentTimeSheetProject;
            if (proj == null || proj.Project == TOTAL_PROJECT)
                return;
            var projId = proj.Id;

            var selectedDate = DateOnly.FromDateTime(SelectedDateTime);
            foreach (var date in GetSelectedWorkWeekDates(selectedDate))
            {
                _dateIndex.TryGetValue(date, out var day);
                var diaries = (day?.TimeSheets ?? [])
                    .Where(ts => ts.ProjectId == projId && !string.IsNullOrWhiteSpace(ts.Diary))
                    .Select(ts => ts.Diary.Trim())
                    .ToList();

                string diaryText = diaries.Count > 0 ? string.Join(", ", diaries) : string.Empty;

                WeekDiaryEntries.Add(new WeekDiaryEntry
                {
                    Day = date.ToString("ddd M/d", CultureInfo.CurrentCulture),
                    Diary = diaryText
                });
            }
        }

        #endregion

        #region Project summary helpers

        private static IEnumerable<DateOnly> GetSelectedWorkWeekDates(DateOnly selectedDate)
        {
            int daysFromMonday = ((int)selectedDate.DayOfWeek + 6) % 7;
            var monday = selectedDate.AddDays(-daysFromMonday);
            for (int day = 0; day < 5; day++)
                yield return monday.AddDays(day);
        }

        private static int SumProjectHours(IEnumerable<CalendarData> entries, Guid projectId)
            => entries.SelectMany(x => x.TimeSheets)
                      .Where(ts => ts.ProjectId == projectId)
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
                TimeProjects.Add(new TimeSheetProjectData { Id = TimeSheetProjectData.TotalRowId, Project = TOTAL_PROJECT });
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

        public event Action<string>? StatusMessageRequested;

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
