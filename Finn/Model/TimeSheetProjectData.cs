using System.ComponentModel;

namespace Finn.Model
{
    /// <summary>
    /// Represents a timesheet project and its associated data.
    /// </summary>
    /// 
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

        private int w1;
        public int W1
        {
            get => w1;
            set { w1 = value; RaisePropertyChanged(nameof(W1)); RaisePropertyChanged(nameof(MonthTotal)); }
        }

        private int w2;
        public int W2
        {
            get => w2;
            set { w2 = value; RaisePropertyChanged(nameof(W2)); RaisePropertyChanged(nameof(MonthTotal)); }
        }

        private int w3;
        public int W3
        {
            get => w3;
            set { w3 = value; RaisePropertyChanged(nameof(W3)); RaisePropertyChanged(nameof(MonthTotal)); }
        }

        private int w4;
        public int W4
        {
            get => w4;
            set { w4 = value; RaisePropertyChanged(nameof(W4)); RaisePropertyChanged(nameof(MonthTotal)); }
        }

        private int w5;
        public int W5
        {
            get => w5;
            set { w5 = value; RaisePropertyChanged(nameof(W5)); RaisePropertyChanged(nameof(MonthTotal)); }
        }

        /// <summary>Sum of W1–W5, used for compact inline display.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public int MonthTotal => W1 + W2 + W3 + W4 + W5;

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
