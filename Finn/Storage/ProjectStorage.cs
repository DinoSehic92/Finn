using Finn.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Finn.Storage
{
    public class ProjectStorage : INotifyPropertyChanged
    {

        private string savePath = "C:\\FIlePathManager";

        public string SavePath
        {
            get => savePath;
            set { savePath = value; RaisePropertyChanged(nameof(SavePath)); }
        }

        private ObservableCollection<ProjectData> storedProjects = new ObservableCollection<ProjectData>();
        public ObservableCollection<ProjectData> StoredProjects
        {
            get { return storedProjects; }
            set { storedProjects = value; RaisePropertyChanged("StoredProjects"); }
        }

        private ObservableCollection<string> collections = new ObservableCollection<string>();
        public ObservableCollection<string> Collections
        {
            get { return collections; }
            set { collections = value; RaisePropertyChanged("Collections"); }
        }


        private void RaisePropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
