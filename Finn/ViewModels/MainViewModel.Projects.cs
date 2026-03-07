using Finn.Model;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Finn.ViewModels
{
    /// <summary>
    /// Project lifecycle: create, remove, rename, sort, select, and group operations.
    /// </summary>
    public partial class MainViewModel
    {
        public void NewProject(string name, string group = null, string category = PROJECT_CATEGORY)
        {
            if (!Storage.StoredProjects.Any(x => x.Namn == name))
            {
                ProjectData newProject = new() { Namn = name, Parent = group, Category = category };

                Storage.StoredProjects.Add(newProject);
                CurrentProject = newProject;

                SetProjectlist();
                SetDefaultType();
                SortProjects();
            }
        }

        public void RemoveProject()
        {
            Storage.StoredProjects.Remove(CurrentProject);

            foreach (FileData file in CurrentProject.StoredFiles)
            {
                PreviewVM.RecentFiles.Remove(file);
            }

            SetProjectlist();
            SetDefaultSelection();
            SortProjects();
            SetCollectionContent();
        }

        public void RemoveProjects(List<ProjectData> list)
        {
            foreach (ProjectData project in list)
            {
                Storage.StoredProjects.Remove(project);
                foreach (FileData file in CurrentProject.StoredFiles)
                {
                    PreviewVM.RecentFiles.Remove(file);
                }
            }

            SetProjectlist();
            SetDefaultSelection();
            SortProjects();
            SetCollectionContent();
        }

        public void RenameProject(string projectName)
        {
            CurrentProject.Namn = projectName;

            foreach (FileData file in CurrentProject.StoredFiles)
            {
                file.Uppdrag = projectName;
            }
            CurrentProject.SetFiletypeList();
        }

        public void Renameproject(string newProjectName)
        {
            RenameProject(newProjectName);
            SetProjectlist();
        }

        public void GetGroups()
        {
            Groups.Clear();

            List<string> list = Storage.StoredProjects.Select(x => x.Parent).Where(x => x != null).Distinct().ToList();
            list.Remove("");

            Groups = new ObservableCollection<string>(list);
        }

        public void SetGroups(string group)
        {
            CurrentProject.Parent = group;
        }

        public void SortProjects()
        {
            List<ProjectData> sortedLibrary = Storage.StoredProjects.Where(x => x.Category == "Library").OrderBy(x => x.Namn).ToList();
            List<ProjectData> sortedArchive = Storage.StoredProjects.Where(x => x.Category == "Archive").OrderBy(x => x.Namn).ToList();
            List<ProjectData> sortedProject = Storage.StoredProjects.Where(x => x.Category == PROJECT_CATEGORY).OrderBy(x => x.Namn).ToList();

            Storage.StoredProjects.Clear();

            foreach (var project in sortedLibrary) { Storage.StoredProjects.Add(project); }
            foreach (var project in sortedArchive) { Storage.StoredProjects.Add(project); }
            foreach (var project in sortedProject) { Storage.StoredProjects.Add(project); }

            SetProjectlist();
        }

        public void SetProject(string name)
        {
            ProjectData project = Storage.StoredProjects.FirstOrDefault(x => x.Namn == name);

            // Use backing fields directly to avoid cascading UpdateFilter calls.
            // Previously SelectProjectAsync + Type setter each triggered UpdateFilter,
            // doubling the work every time a project was switched.
            currentProject = project;

            if (!CurrentProject.Filetypes.Contains(type))
                type = ALL_TYPES;

            UpdateFilter();

            OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(Type));
        }

        public void SelectProject(string name)
        {
            string currentProjectName = CurrentProject.Namn;
            if (currentProjectName != name)
            {
                SetProject(name);
            }
            OnPropertyChanged("UpdateColumns");
        }

        public void SelectProjectAsync(ProjectData project)
        {
            CurrentProject = project;
        }

        /// <summary>
        /// Batched project + type switch that calls UpdateFilter exactly once,
        /// avoiding the cascading filter/layout recalculations that occur when
        /// SelectProject and SelectType are called separately.
        /// </summary>
        public void NavigateTo(string projectName, string typeName)
        {
            bool projectChanged = currentProject?.Namn != projectName;

            if (projectChanged)
            {
                var project = Storage.StoredProjects.FirstOrDefault(x => x.Namn == projectName);
                if (project != null)
                    currentProject = project;
            }

            if (!CurrentProject.Filetypes.Contains(typeName))
                typeName = ALL_TYPES;

            type = typeName;

            UpdateFilter();

            if (projectChanged)
                OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(Type));
            OnPropertyChanged("UpdateColumns");
        }

        public void ReselectProject()
        {
            SetProject(CurrentProject.Namn);
            OnPropertyChanged("UpdateColumns");
        }

        public void SetProjecCategory(string name)
        {
            CurrentProject.Category = name;

            if (name != PROJECT_CATEGORY)
            {
                CurrentProject.Parent = null;
            }

            SortProjects();
        }

        public void SetDefaultSelection()
        {
            string defaultProject = Storage.StoredProjects.FirstOrDefault().Namn;
            CurrentProject = GetProject(defaultProject);
            Type = ALL_TYPES;
        }

        public ProjectData GetProject(string name)
        {
            return Storage.StoredProjects.FirstOrDefault(x => x.Namn == name);
        }

        public void SetProjectlist()
        {
            ProjectList.Clear();

            List<string> newList = Storage.StoredProjects.Select(x => x.Namn).Distinct().ToList();

            foreach (string item in newList)
            {
                ProjectList.Add(item);
            }
        }

        public ProjectData GetDefaultProject()
        {
            return Storage.StoredProjects.FirstOrDefault();
        }
    }
}
