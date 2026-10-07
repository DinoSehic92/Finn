using System;
using System.ComponentModel;

namespace Finn.Model
{
    public class TimeSheetData : INotifyPropertyChanged
    {

        private int hours = 0;
        public int Hours
        {
            get { return hours; }
            set { hours = value; RaisePropertyChanged("Hours"); }
        }

        private Guid? projectId;
        /// <summary>
        /// Stable reference to the owning project. This is the value persisted and
        /// used for all logic; the display name is resolved from the project catalog.
        /// </summary>
        public Guid? ProjectId
        {
            get { return projectId; }
            set { projectId = value; RaisePropertyChanged("ProjectId"); }
        }

        private string project = string.Empty;
        /// <summary>
        /// Display cache of the project's current name. Not persisted — the source of
        /// truth is <see cref="ProjectId"/>; the name is resolved from the catalog.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Project
        {
            get { return project; }
            set { project = value; RaisePropertyChanged("Project"); }
        }

        private string diary = string.Empty;
        public string Diary
        {
            get { return diary; }
            set { diary = value; RaisePropertyChanged("Diary"); }
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
