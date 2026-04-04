using CommunityToolkit.Mvvm.ComponentModel;
using Finn.Model;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Finn.Storage
{
    public class ProjectStorage : ObservableObject
    {
        private ObservableCollection<ProjectData> storedProjects = new ObservableCollection<ProjectData>();
        public ObservableCollection<ProjectData> StoredProjects
        {
            get { return storedProjects; }
            set { storedProjects = value; OnPropertyChanged(nameof(StoredProjects)); }
        }

        private ObservableCollection<string> collections = new ObservableCollection<string>();
        public ObservableCollection<string> Collections
        {
            get { return collections; }
            set { collections = value; OnPropertyChanged(nameof(Collections)); }
        }
    }
}
