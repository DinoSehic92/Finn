using System.ComponentModel;

namespace Finn.Model
{
    public class TimeSheetProjectData : INotifyPropertyChanged
    {

        private string project;
        public string Project
        {
            get { return project; }
            set { project = value; RaisePropertyChanged("Project"); }
        }

        private string projectNr;
        public string ProjectNr
        {
            get { return projectNr; }
            set { projectNr = value; RaisePropertyChanged("ProjectNr"); }
        }

        private string task;
        public string Task
        {
            get { return task; }
            set { task = value; RaisePropertyChanged("Task"); }
        }

        private int w1 = 0;
        public int W1
        {
            get { return w1; }
            set { w1 = value; RaisePropertyChanged("W1"); }
        }

        private int w2 = 0;
        public int W2
        {
            get { return w2; }
            set { w2 = value; RaisePropertyChanged("W2"); }
        }

        private int w3 = 0;
        public int W3
        {
            get { return w3; }
            set { w3 = value; RaisePropertyChanged("W3"); }
        }

        private int w4 = 0;
        public int W4
        {
            get { return w4; }
            set { w4 = value; RaisePropertyChanged("W4"); }
        }

        private int w5 = 0;
        public int W5
        {
            get { return w5; }
            set { w5 = value; RaisePropertyChanged("W4"); }
        }

        private void RaisePropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
