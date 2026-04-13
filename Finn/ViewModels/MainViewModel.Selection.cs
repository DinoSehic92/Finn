using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;

namespace Finn.ViewModels
{
    /// <summary>
    /// File selection, type filtering, and view update operations.
    /// </summary>
    public partial class MainViewModel
    {
        public void SelectFiles(IList<FileData> files)
        {
            CurrentFiles = files;
            SyncExpansionToSelection();
        }

        public void SelectType(string name)
        {
            string currentType = Type;

            if (currentType != name)
            {
                Type = name;
            }
            SignalColumnsChanged();
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
            CurrentProject.SetFiletypeList();
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        public void RemoveSelectedFiles()
        {
            // Track sync folders affected by removal so we can
            // flag them as pending sync immediately.
            var affectedSyncFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FileData file in CurrentFiles.ToList())
            {
                if (file.IsFromFolder && !string.IsNullOrEmpty(file.SyncFolder))
                    affectedSyncFolders.Add(file.SyncFolder);

                if (file.IsAppendedFile)
                {
                    // Detach appended file
                    file.PartOfCollections.Clear();
                    file.ParentNamn = string.Empty;
                    file.ParentFile = null;
                    PreviewVM.RecentFiles.Remove(file);
                    CurrentProject.StoredFiles.Remove(file);
                    continue;
                }

                // Remove appended children from StoredFiles
                var children = CurrentProject.StoredFiles
                    .Where(x => x.ParentNamn == file.Namn).ToList();
                foreach (var child in children)
                {
                    if (child.IsFromFolder && !string.IsNullOrEmpty(child.SyncFolder))
                        affectedSyncFolders.Add(child.SyncFolder);

                    child.PartOfCollections.Clear();
                    child.ParentNamn = string.Empty;
                    child.ParentFile = null;
                    PreviewVM.RecentFiles.Remove(child);
                    CurrentProject.StoredFiles.Remove(child);
                }

                CurrentProject.RemoveFile(file);
                PreviewVM.RecentFiles.Remove(file);
            }

            Collections.SetCollectionContent();
            CurrentProject.RefreshHasChildren();
            CurrentProject.SetFiletypeList();

            // Reset the type filter when the removed files were the last
            // of their kind, so the grid doesn't stay stuck on an empty type.
            if (type != ALL_TYPES && !CurrentProject.Filetypes.Contains(type))
            {
                type = ALL_TYPES;
                OnPropertyChanged(nameof(Type));
            }

            MarkDirty();

            // Flag affected sync folders as needing re-sync
            if (affectedSyncFolders.Count > 0)
                FlagSyncFoldersAsPending(affectedSyncFolders);
        }

        public void SetDefaultType()
        {
            Type = ALL_TYPES;
        }

        public void SetTypeSelected(string type)
        {
            foreach (FileData file in CurrentFiles.Where(f => !f.IsAppendedFile))
            {
                file.Filtyp = type;
            }
            currentProject.SetFiletypeList();
            UpdateFilter();
            MarkDirty();
        }

        public void UpdateFilter()
        {
            var topLevel = CurrentProject.StoredFiles.Where(x => !x.IsAppendedFile);
            var sorted = Type != ALL_TYPES
                ? topLevel.Where(x => x.Filtyp == Type).OrderBy(x => x.Namn)
                : topLevel.OrderBy(x => x.Namn).OrderByDescending(x => x.Filtyp);

            // Build the list with expanded children inserted inline
            var result = new List<FileData>();
            foreach (var file in sorted)
            {
                result.Add(file);
                if (file.IsExpanded)
                {
                    var children = CurrentProject.StoredFiles
                        .Where(x => x.ParentNamn == file.Namn)
                        .OrderBy(x => x.Namn);
                    result.AddRange(children);
                }
            }

            filteredFiles.ReplaceAll(result);
            OnPropertyChanged(nameof(NrFilteredFiles));

            if (CurrentProject.Category != SEARCH_CATEGORY)
            {
                IndexedSearch = false;
                PreviewVM.SearchMode = false;
                SearchText = String.Empty;
            }
        }

        private bool _filterScheduled;

        /// <summary>
        /// Schedules a single <see cref="UpdateFilter"/> call on the next UI
        /// dispatch cycle. Multiple rapid calls collapse into one update,
        /// avoiding redundant work when batch operations add/remove many files.
        /// </summary>
        public void ScheduleFilterUpdate()
        {
            if (_filterScheduled) return;
            _filterScheduled = true;
            Dispatcher.UIThread.Post(() =>
            {
                _filterScheduled = false;
                UpdateFilter();
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Toggles inline expansion of a parent file's appended children.
        /// </summary>
        public void ToggleExpansion(FileData file)
        {
            if (file.IsAppendedFile) return;
            file.IsExpanded = !file.IsExpanded;
            UpdateFilter();
        }

        /// <summary>
        /// Collapses any currently expanded file and expands the given file
        /// if it has children. Called on selection change so only the selected
        /// parent ever shows its children inline.
        /// </summary>
        private void SyncExpansionToSelection()
        {
            var selected = CurrentFile;
            if (selected == null) return;

            // If the selected file is an appended child, keep its parent expanded
            var activeParent = selected.IsAppendedFile ? selected.ParentFile : selected;

            bool changed = false;
            foreach (var file in CurrentProject.StoredFiles)
            {
                if (!file.IsExpanded) continue;
                if (file == activeParent) continue;
                file.IsExpanded = false;
                changed = true;
            }

            if (activeParent is { HasChildren: true, IsExpanded: false })
            {
                activeParent.IsExpanded = true;
                changed = true;
            }

            if (changed)
                UpdateFilter();
        }

        public void UpdateTreeview()
        {
            SignalTreeViewUpdate();
        }
    }
}
