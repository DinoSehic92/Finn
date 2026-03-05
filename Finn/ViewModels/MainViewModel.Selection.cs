using Finn.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Finn.ViewModels
{
    /// <summary>
    /// File selection, type filtering, and view update operations.
    /// </summary>
    public partial class MainViewModel
    {
        public void select_files(IList<FileData> files)
        {
            CurrentFiles = files;
            SetAttachedView();
        }

        private void SetAttachedView()
        {
            if (CurrentFile != null)
            {
                AttachedView = CurrentFile.HasAppendedFiles;
            }
        }

        public void SelectType(string name)
        {
            string currentType = Type;

            if (currentType != name)
            {
                Type = name;
            }
            OnPropertyChanged("UpdateColumns");
        }

        public void AddPlaceholderFile(string name)
        {
            FileData newfile = new()
            {
                Namn = name,
                Filtyp = NEW_TYPE,
                Uppdrag = CurrentProject.Namn,
                Sökväg = string.Empty
            };

            CurrentProject.StoredFiles.Add(newfile);
            UpdateFilter();
            OnPropertyChanged("TreeViewUpdate");
            MarkDirty();
        }

        public void RemoveSelectedFiles()
        {
            foreach (FileData file in CurrentFiles)
            {
                CurrentProject.RemoveFile(file);
                PreviewVM.RecentFiles.Remove(file);
                SetCollectionContent();
            }

            CurrentProject.SetFiletypeList();
            MarkDirty();

            if (FilteredFiles == null)
            {
                SetDefaultSelection();
            }
        }

        public void SetDefaultType()
        {
            Type = ALL_TYPES;
        }

        public void SetTypeSelected(string type)
        {
            foreach (FileData file in CurrentFiles)
            {
                file.Filtyp = type;
            }
            currentProject.SetFiletypeList();
            UpdateFilter();
            MarkDirty();
        }

        public void UpdateFilter()
        {
            FilteredFiles.Clear();

            if (Type != ALL_TYPES)
            {
                foreach (FileData file in CurrentProject.StoredFiles.Where(x => x.Filtyp == Type).OrderBy(x => x.Namn))
                {
                    FilteredFiles.Add(file);
                }
            }
            else
            {
                foreach (FileData file in CurrentProject.StoredFiles.OrderBy(x => x.Namn).OrderByDescending(x => x.Filtyp))
                {
                    FilteredFiles.Add(file);
                }
            }
            OnPropertyChanged(nameof(NrFilteredFiles));

            if (CurrentProject.Category != SEARCH_CATEGORY)
            {
                IndexedSearch = false;
                PreviewVM.SearchMode = false;
                SearchText = String.Empty;
            }
        }

        public void UpdateTreeview()
        {
            OnPropertyChanged("TreeViewUpdate");
        }
    }
}
