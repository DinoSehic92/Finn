using CommunityToolkit.Mvvm.ComponentModel;
using Finn.Model;
using System.Collections.Generic;
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

        /// <summary>
        /// Explicit group/subgroup records. Populated from ProjectData.Parent strings
        /// on first load via MigrateGroupsOnLoad(), then managed directly.
        /// </summary>
        public List<GroupData> ProjectGroups { get; set; } = new List<GroupData>();
    }
}
