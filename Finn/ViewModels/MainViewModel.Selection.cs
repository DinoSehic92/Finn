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

        public FileData AddGroup(string name, string? categoryOverride = null)
        {
            name = EnsureUniqueName(name);

            FileData group = new()
            {
                Namn = name,
                IsGroup = true,
                IsExpanded = true,
                Filtyp = categoryOverride ?? (Type != ALL_TYPES ? Type : NEW_TYPE),
                Uppdrag = CurrentProject.Namn,
                Sökväg = string.Empty
            };

            CurrentProject.StoredFiles.Add(group);
            CurrentProject.SetFiletypeList();
            UpdateFilter();
            OnPropertyChanged(nameof(AvailableGroups));
            SignalTreeViewUpdate();
            MarkDirty();
            return group;
        }

        /// <summary>
        /// Moves the selected files into a group. Children inherit the
        /// group's category. Works for both groups and regular parent files.
        /// </summary>
        public void MoveFilesToParent(FileData target, IList<FileData> files)
        {
            if (target == null || files == null || files.Count == 0) return;

            foreach (var file in files.ToList())
            {
                // Don't allow nesting a group inside another group,
                // moving a file into itself, or re-parenting to the same parent.
                if (file == target) continue;
                if (file.IsGroup) continue;
                if (file.ParentNamn == target.Namn) continue;

                // Detach from any previous parent first
                if (file.IsAppendedFile)
                    file.ParentFile = null;

                file.ParentNamn = target.Namn;
                file.ParentFile = target;

                // Inherit the group's category
                if (target.IsGroup)
                    file.Filtyp = target.Filtyp;
            }

            target.HasChildren = true;
            target.IsExpanded = true;
            CurrentProject.RefreshHasChildren();
            CurrentProject.SetFiletypeList();
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        /// <summary>
        /// Detaches files from their parent (group or attached file parent),
        /// making them top-level project files again. The detached files keep
        /// their current type so re-categorization isn't forced.
        /// </summary>
        public void DetachFiles(IList<FileData> files)
        {
            if (files == null || files.Count == 0) return;

            // Assign the active filter type so detached files stay visible in
            // the current view instead of disappearing into a different category.
            string detachedType = Type != ALL_TYPES ? Type : NEW_TYPE;

            foreach (var file in files.ToList())
            {
                if (!file.IsAppendedFile) continue;
                file.ParentNamn = string.Empty;
                file.ParentFile = null;
                file.Filtyp = detachedType;
            }

            CurrentProject.RefreshHasChildren();
            CurrentProject.SetFiletypeList();
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        /// <summary>
        /// Renames a group header and updates all children that reference it.
        /// </summary>
        public void RenameGroup(FileData group, string newName)
        {
            if (group == null || !group.IsGroup || string.IsNullOrWhiteSpace(newName)) return;

            newName = EnsureUniqueName(newName, group);

            string oldName = group.Namn;
            group.Namn = newName;

            // Update ParentNamn on all children that reference the old name
            foreach (var file in CurrentProject.StoredFiles.Where(f => f.ParentNamn == oldName))
                file.ParentNamn = newName;

            OnPropertyChanged(nameof(AvailableGroups));
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        /// <summary>
        /// Converts a regular file into a group header by clearing its file
        /// path and setting <see cref="FileData.IsGroup"/>. Existing children
        /// (if any) are preserved.
        /// </summary>
        public void ConvertToGroup(FileData file)
        {
            if (file == null || file.IsGroup || file.IsAppendedFile) return;

            file.IsGroup = true;
            file.Sökväg = string.Empty;
            file.OriginalPath = string.Empty;

            CurrentProject.RefreshHasChildren();
            CurrentProject.SetFiletypeList();
            OnPropertyChanged(nameof(AvailableGroups));
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        /// <summary>
        /// Ensures the given name is unique among all files in the current project.
        /// Appends " (2)", " (3)", etc. when a collision is found.
        /// </summary>
        private string EnsureUniqueName(string name, FileData? exclude = null)
        {
            var existing = new HashSet<string>(
                CurrentProject.StoredFiles
                    .Where(f => f != exclude)
                    .Select(f => f.Namn),
                StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(name))
                return name;

            int suffix = 2;
            while (existing.Contains($"{name} ({suffix})"))
                suffix++;

            return $"{name} ({suffix})";
        }

        /// <summary>
        /// Dissolves a group: detaches all children (making them top-level) and
        /// removes the group header itself. Children keep their current type.
        /// </summary>
        public void DissolveGroup(FileData group)
        {
            if (group == null || !group.IsGroup) return;

            var children = CurrentProject.StoredFiles
                .Where(f => f.ParentNamn == group.Namn).ToList();

            foreach (var child in children)
            {
                child.ParentNamn = string.Empty;
                child.ParentFile = null;
            }

            CurrentProject.StoredFiles.Remove(group);
            PreviewVM.RecentFiles.Remove(group);

            CurrentProject.RefreshHasChildren();
            CurrentProject.SetFiletypeList();
            Collections.SetCollectionContent();
            OnPropertyChanged(nameof(AvailableGroups));
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        /// <summary>
        /// Returns all group headers in the current project for the
        /// "Move to Group" context menu submenu.
        /// </summary>
        public IReadOnlyList<FileData> AvailableGroups =>
            CurrentProject.StoredFiles
                .Where(f => f.IsGroup && !f.IsAppendedFile)
                .OrderBy(f => f.Namn)
                .ToList();

        public void RemoveSelectedFiles()
        {
            if (CurrentFiles == null) return;

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
            OnPropertyChanged(nameof(AvailableGroups));

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
            if (CurrentFiles == null) return;
            foreach (FileData file in CurrentFiles.Where(f => !f.IsAppendedFile))
            {
                file.Filtyp = type;

                // When a group changes category, propagate to its children
                if (file.IsGroup && file.HasChildren)
                {
                    foreach (var child in CurrentProject.StoredFiles.Where(c => c.ParentNamn == file.Namn))
                        child.Filtyp = type;
                }
            }
            currentProject.SetFiletypeList();
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        public void UpdateFilter()
        {
            var topLevel = CurrentProject.StoredFiles.Where(x => !x.IsAppendedFile);
            var filtered = Type != ALL_TYPES
                ? topLevel.Where(x => x.Filtyp == Type)
                : topLevel;

            // Groups first, then regular files, both sorted alphabetically
            var sorted = filtered
                .OrderByDescending(x => x.IsGroup)
                .ThenBy(x => x.Namn);

            // Build the list with expanded children inserted inline.
            // Groups default to expanded but can be collapsed via chevron.
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
        /// Toggles inline expansion of a parent file or group's children.
        /// </summary>
        public void ToggleExpansion(FileData file)
        {
            if (file.IsAppendedFile) return;
            file.IsExpanded = !file.IsExpanded;
            UpdateFilter();
        }

        /// <summary>
        /// Collapses any currently expanded non-group file and expands the
        /// selected parent. Groups keep their user-toggled expansion state.
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
                if (file.IsAppendedFile) continue;
                if (!file.HasChildren) continue;

                // Keep the active parent expanded
                if (file == activeParent)
                {
                    if (!file.IsExpanded)
                    {
                        file.IsExpanded = true;
                        changed = true;
                    }
                    continue;
                }

                // Groups keep their current expansion state;
                // non-group parents collapse when not active.
                if (file.IsGroup) continue;

                if (file.IsExpanded)
                {
                    file.IsExpanded = false;
                    changed = true;
                }
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
