using System.ComponentModel;

namespace Finn.Model
{
    /// <summary>
    /// Represents a timesheet project and its associated data.
    /// </summary>
    public class TimeSheetProjectData : INotifyPropertyChanged
    {
        private string project = string.Empty;
        /// <summary>
        /// Gets or sets the project name.
        /// </summary>
        public string Project
        {
            get => project;
            set { project = value; RaisePropertyChanged(nameof(Project)); }
        }

        private string projectNr = string.Empty;
        /// <summary>
        /// Gets or sets the project number.
        /// </summary>
        public string ProjectNr
        {
            get => projectNr;
            set { projectNr = value; RaisePropertyChanged(nameof(ProjectNr)); }
        }

        private string task = string.Empty;
        /// <summary>
        /// Gets or sets the task.
        /// </summary>
        public string Task
        {
            get => task;
            set { task = value; RaisePropertyChanged(nameof(Task)); }
        }

        private int w1;
        /// <summary>
        /// Gets or sets week 1 value.
        /// </summary>
        public int W1
        {
            get => w1;
            set { w1 = value; RaisePropertyChanged(nameof(W1)); }
        }

        private int w2;
        /// <summary>
        /// Gets or sets week 2 value.
        /// </summary>
        public int W2
        {
            get => w2;
            set { w2 = value; RaisePropertyChanged(nameof(W2)); }
        }

        private int w3;
        /// <summary>
        /// Gets or sets week 3 value.
        /// </summary>
        public int W3
        {
            get => w3;
            set { w3 = value; RaisePropertyChanged(nameof(W3)); }
        }

        private int w4;
        /// <summary>
        /// Gets or sets week 4 value.
        /// </summary>
        public int W4
        {
            get => w4;
            set { w4 = value; RaisePropertyChanged(nameof(W4)); }
        }

        private int w5;
        /// <summary>
        /// Gets or sets week 5 value.
        /// </summary>
        public int W5
        {
            get => w5;
            set { w5 = value; RaisePropertyChanged(nameof(W5)); }
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
