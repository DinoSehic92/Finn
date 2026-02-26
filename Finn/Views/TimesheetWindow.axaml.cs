using System;
using System.Linq;
using System.Reflection;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Finn.Model;
using Finn.ViewModels;

namespace Finn.Views
{
    public partial class TimesheetWindow : Window
    {
        private ObservableCollection<TimeSheetProjectData> _localProjects = new();

        public TimesheetWindow()
        {
            InitializeComponent();
            // Ensure summaries are refreshed when the window is shown
            this.Opened += TimesheetWindow_Opened;
        }

        private void OnProjectTextChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
        {
            // Apply edits in the textboxes to the selected local project
            if (ProjectList.SelectedItem is TimeSheetProjectData sel)
            {
                try
                {
                    sel.Project = ProjNameBox.Text ?? string.Empty;
                    sel.ProjectNr = ProjNrBox.Text ?? string.Empty;
                    sel.Task = ProjTaskBox.Text ?? string.Empty;
                    // After a rename/update, refresh local summaries because project key changed
                    if (DataContext is MainViewModel vm) RefreshLocalSummaries(vm);
                }
                catch { }
            }
        }

        private void RefreshLocalSummaries(MainViewModel vm)
        {
            try
            {
                // Determine month context same as CalendarViewModel.WeeklyTimeSummary
                int month = vm.Calendar.SelectedDateTime.Month;
                int year = vm.Calendar.SelectedDateTime.Year;
                var monthEntries = vm.Calendar.CalendarList.Where(x => x.Date.Month == month && x.Date.Year == year);

                // Update each local project's weekly sums
                foreach (var lp in _localProjects)
                {
                    if (lp == null) continue;
                    if (lp.Project == "Total") continue;

                    lp.W1 = monthEntries.Where(x => x.WeekOfMonth == 0).SelectMany(x => x.TimeSheets).Where(ts => ts.Project == lp.Project).Sum(ts => ts.Hours);
                    lp.W2 = monthEntries.Where(x => x.WeekOfMonth == 1).SelectMany(x => x.TimeSheets).Where(ts => ts.Project == lp.Project).Sum(ts => ts.Hours);
                    lp.W3 = monthEntries.Where(x => x.WeekOfMonth == 2).SelectMany(x => x.TimeSheets).Where(ts => ts.Project == lp.Project).Sum(ts => ts.Hours);
                    lp.W4 = monthEntries.Where(x => x.WeekOfMonth == 3).SelectMany(x => x.TimeSheets).Where(ts => ts.Project == lp.Project).Sum(ts => ts.Hours);
                    lp.W5 = monthEntries.Where(x => x.WeekOfMonth == 4).SelectMany(x => x.TimeSheets).Where(ts => ts.Project == lp.Project).Sum(ts => ts.Hours);
                }

                // Update total summary
                var total = _localProjects.FirstOrDefault(x => x.Project == "Total");
                if (total != null)
                {
                    var nonTotal = _localProjects.Where(x => x.Project != "Total").ToList();
                    total.W1 = nonTotal.Sum(x => x.W1);
                    total.W2 = nonTotal.Sum(x => x.W2);
                    total.W3 = nonTotal.Sum(x => x.W3);
                    total.W4 = nonTotal.Sum(x => x.W4);
                    total.W5 = nonTotal.Sum(x => x.W5);
                }

                // Also refresh MonthlyNotes diary/time to reflect selected local project
                if (ProjectList.SelectedItem is TimeSheetProjectData sel)
                {
                    foreach (var cal in vm.Calendar.MonthlyNotes)
                    {
                        cal.SetCurrentTimeSheetProjectDiary(sel.Project);
                    }
                }
            }
            catch { }
        }

        public void AttachDataContext(object? dc)
        {
            DataContext = dc;

            // Prepare an editable local copy of the TimeProjects so edits are
            // kept local until the user presses Save.
            _localProjects.Clear();
            if (dc is MainViewModel vm && vm.Calendar?.TimeProjects != null)
            {
                foreach (var tp in vm.Calendar.TimeProjects)
                {
                    _localProjects.Add(CloneProject(tp));
                }

                // Bind the DataGrid to the local collection
                try { ProjectList.ItemsSource = _localProjects; } catch { }
                // Refresh local summary columns from the calendar data
                RefreshLocalSummaries(vm);
            }
        }

        private void OnNewTimeSheet(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Add new project to the local editable collection only
            var newProj = new TimeSheetProjectData { Project = "New Project" };
            _localProjects.Add(newProj);
            // Select the new item in the UI
            try { ProjectList.SelectedItem = newProj; } catch { }
        }

        private void OnRemoveTimeSheet(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // Remove from the local editable collection only
            if (ProjectList.SelectedItem is TimeSheetProjectData sel)
            {
                _localProjects.Remove(sel);
            }
        }

        private void OnProjectListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            // Update the editable text boxes from the selected local project
            if (sender is DataGrid grid)
            {
                if (grid.SelectedItem is TimeSheetProjectData tp)
                {
                    ProjNameBox.Text = tp.Project;
                    ProjNrBox.Text = tp.ProjectNr;
                    ProjTaskBox.Text = tp.Task;
                }
                else
                {
                    ProjNameBox.Text = string.Empty;
                    ProjNrBox.Text = string.Empty;
                    ProjTaskBox.Text = string.Empty;
                }
            }
        }

        private void TimesheetWindow_Opened(object? sender, EventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                try
                {
                    // Ensure calendar month entries and monthly notes are populated
                    vm.Calendar.SetCalendarMonth();
                    // Refresh summaries so the UI shows up-to-date values when opened
                    vm.Calendar.UpdateTimeSheetSummary();
                    vm.Calendar.WeeklyTimeSummary();

                    // Update local project summary values to reflect VM calendar data
                    RefreshLocalSummaries(vm);
                }
                catch { }
            }
        }

        private static TimeSheetProjectData CloneProject(TimeSheetProjectData src)
        {
            if (src == null) return new TimeSheetProjectData();
            return new TimeSheetProjectData
            {
                Project = src.Project,
                ProjectNr = src.ProjectNr,
                Task = src.Task,
                W1 = src.W1,
                W2 = src.W2,
                W3 = src.W3,
                W4 = src.W4,
                W5 = src.W5
            };
        }

        private void OnSaveTimeProjects(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.Calendar != null)
            {
                try
                {
                    // Replace the ViewModel's TimeProjects with clones of the local collection
                    vm.Calendar.TimeProjects.Clear();
                    foreach (var lp in _localProjects)
                    {
                        if (lp == null) continue;
                        vm.Calendar.TimeProjects.Add(CloneProject(lp));
                    }

                    // After saving, refresh the VM summaries to reflect the new projects
                    vm.Calendar.UpdateTimeSheetSummary();
                    vm.Calendar.WeeklyTimeSummary();
                }
                catch { }
            }
        }
    }
}
