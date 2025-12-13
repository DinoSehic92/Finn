using System.ComponentModel;

namespace Finn.Model
{
    public class TimeSheetData : INotifyPropertyChanged
    {

        private int hours;
        public int Hours
        {
            get { return hours; }
            set { hours = value; RaisePropertyChanged("Hours"); }
        }

        private string project = string.Empty;
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
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
