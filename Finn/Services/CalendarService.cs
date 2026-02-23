using Finn.Model;
using Finn.ViewModels;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Finn.Services
{
    /// <summary>
    /// Persists calendar data to a separate JSON file under Storage.General.SavePath.
    /// </summary>
    public class CalendarService : ICalendarService
    {
        private CalendarViewModel? _calendar;
        private string _fileName = string.Empty;
        private string? _loadedJson;

        public void Initialize(CalendarViewModel calendar, string savePath)
        {
            _calendar = calendar;
            if (string.IsNullOrWhiteSpace(savePath)) savePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
            _fileName = Path.Combine(savePath, "Calendar.json");

            // Load existing calendar file if present
            try
            {
                if (File.Exists(_fileName))
                {
                    _loadedJson = File.ReadAllText(_fileName);
                    ApplyLoadedJsonIfReady();
                }
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarService.Initialize");
            }

            // Attach handlers to the provided calendar VM
            AttachToCalendarVm();
        }

        /// <summary>
        /// Initialize the service for saving without loading existing JSON from disk.
        /// This avoids overwriting an in-memory CalendarViewModel when the caller
        /// wants to persist the current runtime state.
        /// </summary>
        public void InitializeForSave(CalendarViewModel calendar, string savePath)
        {
            _calendar = calendar;
            if (string.IsNullOrWhiteSpace(savePath)) savePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);
            _fileName = Path.Combine(savePath, "Calendar.json");

            // Do not read existing file into _loadedJson; just attach handlers so Save() will write current state.
            AttachToCalendarVm();
        }

        // Public method to allow callers to force an immediate save
        public void SaveNow()
        {
            Save();
        }

        /// <summary>
        /// Fast, no-side-effects save helper. Serializes the provided calendar
        /// and writes it to Calendar.json under savePath. Does not attach any
        /// event handlers to the calendar VM.
        /// </summary>
        public static void SaveCalendarToFile(CalendarViewModel calendar, string savePath)
        {
            try
            {
                if (calendar == null) return;

                if (string.IsNullOrWhiteSpace(savePath)) savePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!Directory.Exists(savePath)) Directory.CreateDirectory(savePath);

                string fileName = Path.Combine(savePath, "Calendar.json");

                var dto = new CalendarDto
                {
                    CalendarList = new ObservableCollection<CalendarData>(calendar.CalendarList ?? new ObservableCollection<CalendarData>()),
                    TimeProjects = new ObservableCollection<TimeSheetProjectData>(calendar.TimeProjects ?? new ObservableCollection<TimeSheetProjectData>())
                };

                string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                File.WriteAllText(fileName, json);
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarService.SaveCalendarToFile");
            }
        }


        private void AttachCollectionHandlers(ObservableCollection<CalendarData>? list)
        {
            if (list == null) return;
            try { list.CollectionChanged -= CalendarList_CollectionChanged; } catch { }
            list.CollectionChanged += CalendarList_CollectionChanged;
            // subscribe existing items
            foreach (var it in list) SubscribeCalendarItem(it);
        }

        private void AttachTimeProjectHandlers(ObservableCollection<TimeSheetProjectData>? list)
        {
            if (list == null) return;
            try { list.CollectionChanged -= TimeProjects_CollectionChanged; } catch { }
            list.CollectionChanged += TimeProjects_CollectionChanged;
        }

        private void TimeProjects_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Save();
        }

        private void AttachToCalendarVm()
        {
            if (_calendar == null) return;

            _calendar.PropertyChanged -= Calendar_PropertyChanged;
            _calendar.PropertyChanged += Calendar_PropertyChanged;

            AttachCollectionHandlers(_calendar.CalendarList);
            AttachTimeProjectHandlers(_calendar.TimeProjects);

            ApplyLoadedJsonIfReady();
        }

        private void ApplyLoadedJsonIfReady()
        {
            if (string.IsNullOrEmpty(_loadedJson) || _calendar == null) return;

            try
            {
                var dto = JsonConvert.DeserializeObject<CalendarDto>(_loadedJson!);
                if (dto != null)
                {
                    _calendar.CalendarList.Clear();
                    foreach (var item in dto.CalendarList ?? new ObservableCollection<CalendarData>())
                        _calendar.CalendarList.Add(item);

                    _calendar.TimeProjects.Clear();
                    foreach (var tp in dto.TimeProjects ?? new ObservableCollection<TimeSheetProjectData>())
                        _calendar.TimeProjects.Add(tp);
                }
                else
                {
                    var list = JsonConvert.DeserializeObject<ObservableCollection<CalendarData>>(_loadedJson!);
                    if (list != null)
                    {
                        _calendar.CalendarList.Clear();
                        foreach (var item in list) _calendar.CalendarList.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarService.ApplyLoadedJsonIfReady");
            }
            finally
            {
                _loadedJson = null;
            }
        }

        private void Calendar_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_calendar == null) return;

            if (e.PropertyName == nameof(CalendarViewModel.CalendarList))
            {
                AttachCollectionHandlers(_calendar.CalendarList);
                Save();
            }
            else if (e.PropertyName == nameof(CalendarViewModel.TimeProjects))
            {
                AttachTimeProjectHandlers(_calendar.TimeProjects);
                Save();
            }
        }

        private void CalendarList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e == null) { Save(); return; }

            if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
            {
                foreach (CalendarData item in e.NewItems)
                {
                    SubscribeCalendarItem(item);
                }
            }

            if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
            {
                foreach (CalendarData item in e.OldItems)
                {
                    UnsubscribeCalendarItem(item);
                }
            }

            Save();
        }

        private void SubscribeCalendarItem(CalendarData item)
        {
            if (item == null) return;
            try { item.PropertyChanged -= CalendarItem_PropertyChanged; } catch { }
            item.PropertyChanged += CalendarItem_PropertyChanged;
            // subscribe to timesheet collection
            try { item.TimeSheets.CollectionChanged -= TimeSheets_CollectionChanged; } catch { }
            item.TimeSheets.CollectionChanged += TimeSheets_CollectionChanged;
            // subscribe to existing timesheets
            foreach (var ts in item.TimeSheets)
            {
                try { ts.PropertyChanged -= TimeSheet_PropertyChanged; } catch { }
                ts.PropertyChanged += TimeSheet_PropertyChanged;
            }
        }

        private void UnsubscribeCalendarItem(CalendarData item)
        {
            if (item == null) return;
            try { item.PropertyChanged -= CalendarItem_PropertyChanged; } catch { }
            try { item.TimeSheets.CollectionChanged -= TimeSheets_CollectionChanged; } catch { }
            foreach (var ts in item.TimeSheets)
            {
                try { ts.PropertyChanged -= TimeSheet_PropertyChanged; } catch { }
            }
        }

        private void CalendarItem_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Persist when meaningful calendar item properties change
            if (e.PropertyName == nameof(CalendarData.Note1) || e.PropertyName == nameof(CalendarData.Note2) ||
                e.PropertyName == nameof(CalendarData.Reminder) || e.PropertyName == nameof(CalendarData.TimeSheets))
            {
                Save();
            }
        }

        private void TimeSheets_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e == null) { Save(); return; }
            if (e.NewItems != null)
            {
                foreach (TimeSheetData ts in e.NewItems)
                {
                    try { ts.PropertyChanged -= TimeSheet_PropertyChanged; } catch { }
                    ts.PropertyChanged += TimeSheet_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (TimeSheetData ts in e.OldItems)
                {
                    try { ts.PropertyChanged -= TimeSheet_PropertyChanged; } catch { }
                }
            }
            Save();
        }

        private void TimeSheet_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // save on any timesheet field change
            Save();
        }

        // Note: CalendarService no longer listens to MainViewModel; methods removed.

        private void Save()
        {
            try
            {
                if (_calendar == null || string.IsNullOrEmpty(_fileName)) return;

                var dir = Path.GetDirectoryName(_fileName);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var dto = new CalendarDto
                {
                    CalendarList = new ObservableCollection<CalendarData>(_calendar.CalendarList ?? new ObservableCollection<CalendarData>()),
                    TimeProjects = new ObservableCollection<TimeSheetProjectData>(_calendar.TimeProjects ?? new ObservableCollection<TimeSheetProjectData>())
                };

                string json = JsonConvert.SerializeObject(dto, Formatting.Indented);
                File.WriteAllText(_fileName, json);
            }
            catch (Exception ex)
            {
                Finn.Utils.ErrorLogger.Log(ex, "CalendarService.Save");
            }
        }

        private class CalendarDto
        {
            public ObservableCollection<CalendarData>? CalendarList { get; set; }
            public ObservableCollection<TimeSheetProjectData>? TimeProjects { get; set; }
        }
    }
}
