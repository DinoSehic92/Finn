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

        private string? GetDetachedChildType() => Type != ALL_TYPES ? Type : null;

        /// <summary>
        /// Returns true when <paramref name="file"/> is managed by an
        /// <see cref="SyncFolderMode.AttachedFiles"/> folder. Such files must
        /// stay together and cannot be independently detached or reparented.
        /// </summary>
        private bool IsAttachedFolderFile(FileData file) =>
            file.IsAppendedFile
            && file.IsFromFolder
            && !string.IsNullOrEmpty(file.SyncFolder)
            && CurrentProject!.Folders.Any(f =>
                f.Mode == SyncFolderMode.AttachedFiles
                && string.Equals(f.Path, file.SyncFolder, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Returns true when <paramref name="file"/> has any appended children
        /// that originate from a sync folder. Used to block moving a parent
        /// file to another project when doing so would silently drag along
        /// synced children, breaking the source folder's baseline.
        /// </summary>
        private bool HasSyncedChildren(FileData file) =>
            CurrentProject!.GetChildren(file).Any(c => c.IsFromFolder);

        private bool CanMoveToParent(FileData target, FileData file)
        {
            if (target.IsChild) return false;
            if (file == target) return false;
            if (!file.IsRegularFile) return false;
            if (file.HasChildren) return false;
            // Files managed by an AttachedFiles sync folder must stay together
            if (IsAttachedFolderFile(file)) return false;
            // Prevent circular parentage: target must not be a descendant of file
            if (IsDescendantOf(target, file)) return false;

            return !string.Equals(file.ParentNamn, target.Namn, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true when <paramref name="item"/> is a descendant of <paramref name="ancestor"/>
        /// at any depth by walking the parent chain.
        /// </summary>
        private static bool IsDescendantOf(FileData item, FileData ancestor)
        {
            var current = item;
            while (current.IsAppendedFile && current.ParentFile != null)
            {
                if (ReferenceEquals(current.ParentFile, ancestor))
                    return true;
                current = current.ParentFile;
            }
            return false;
        }

        private void DetachChild(FileData file, string? detachedType = null)
        {
            if (!file.IsAppendedFile) return;
            file.ClearParent(detachedType);
        }

        /// <summary>
        /// Detaches all children of <paramref name="parent"/>, making them top-level.
        /// Does not refresh hierarchy state — the caller is responsible for that.
        /// </summary>
        private void DetachChildren(FileData parent, string? detachedType = null)
        {
            var children = CurrentProject!.GetChildren(parent);
            foreach (var child in children)
                DetachChild(child, detachedType);
        }

        /// <summary>
        /// Refreshes child state derived from the parent entry, optionally syncing category.
        /// </summary>
        private void RefreshChildrenFromParent(FileData parent, bool syncCategory = false)
        {
            foreach (var child in CurrentProject!.GetChildren(parent))
            {
                if (syncCategory)
                    child.Filtyp = parent.Filtyp;

                child.RefreshParentRelationshipState();
            }
        }

        private void NotifyCurrentSelectionStructureChanged()
        {
            InvalidateAvailableParentsCache();
            OnPropertyChanged(nameof(SelectedFileIsTopLevel));
            OnPropertyChanged(nameof(SelectedFileIsChild));
            OnPropertyChanged(nameof(SelectedFileIsGroup));
            OnPropertyChanged(nameof(SelectedFileCanMarkAsParent));
            OnPropertyChanged(nameof(HasAvailableGroups));
            OnPropertyChanged(nameof(HasAvailableParents));
            OnPropertyChanged(nameof(CanMoveSelectedFiles));
            OnPropertyChanged(nameof(CanCategorizeSelectedFiles));
            OnPropertyChanged(nameof(AvailableGroups));
        }

        private void RefreshHierarchyState(bool refreshCollections = false, bool updateFilter = true)
        {
            CurrentProject!.RefreshHasChildren();
            CurrentProject!.SetFiletypeList();

            if (refreshCollections)
                Collections.SetCollectionContent();

            InvalidateAvailableParentsCache();
            OnPropertyChanged(nameof(AvailableParents));
            OnPropertyChanged(nameof(AvailableGroups));

            if (updateFilter)
                UpdateFilter();

            SignalTreeViewUpdate();
        }

        /// <summary>
        /// Toggles the <see cref="FileData.IsDesignatedParent"/> flag on the given file.
        /// After toggling, the AvailableParents cache is invalidated so the file
        /// appears in (or disappears from) the "Attach to…" submenu immediately.
        /// </summary>
        public void ToggleDesignatedParent(FileData file)
        {
            if (file == null || file.IsChild || file.IsGroup || file.HasChildren) return;

            file.IsDesignatedParent = !file.IsDesignatedParent;

            InvalidateAvailableParentsCache();
            OnPropertyChanged(nameof(AvailableParents));
            OnPropertyChanged(nameof(AvailableGroups));
            OnPropertyChanged(nameof(HasAvailableParents));
            OnPropertyChanged(nameof(HasAvailableGroups));
            OnPropertyChanged(nameof(SelectedFileIsDesignatedParent));
            MarkDirty();
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
                Uppdrag = CurrentProject!.Namn,
                Sökväg = string.Empty
            };

            CurrentProject!.StoredFiles.Add(group);
            RefreshHierarchyState();
            MarkDirty();
            return group;
        }

        /// <summary>
        /// Moves the selected files into a group. Children inherit the
        /// group's category. Works for both groups and regular parent files.
        /// Returns the number of files actually moved (skipped files are not counted).
        /// </summary>
        public int MoveFilesToParent(FileData target, IList<FileData> files)
        {
            if (target == null || files == null || files.Count == 0) return 0;

            int moved = 0;
            foreach (var file in files.ToList())
            {
                if (!CanMoveToParent(target, file)) continue;
                file.SetParent(target);
                file.TransferOtherFilesTo(target);
                moved++;
            }

            if (moved > 0)
            {
                target.IsExpanded = true;
                RefreshHierarchyState();
                MarkDirty();
            }

            return moved;
        }

        /// <summary>
        /// Detaches files from their parent (group or attached file parent),
        /// making them top-level project files again. Each file keeps its own
        /// existing category; the type filter is only applied when no category
        /// is set, so detaching never silently overwrites metadata.
        /// </summary>
        public void DetachFiles(IList<FileData> files)
        {
            if (files == null || files.Count == 0) return;

            string? activeFilter = GetDetachedChildType();

            foreach (var file in files.ToList())
            {
                // Files from an AttachedFiles sync folder must stay together —
                // detaching individual files would break the folder's tracking.
                if (IsAttachedFolderFile(file)) continue;

                // Preserve the file's own category; only fall back to the
                // active filter when the file has no category of its own.
                string? detachedType = string.IsNullOrEmpty(file.Filtyp)
                    ? activeFilter
                    : null;
                DetachChild(file, detachedType);
            }

            RefreshHierarchyState();
            MarkDirty();
        }

        /// <summary>
        /// Renames a group header and updates all children and folder links that reference it.
        /// </summary>
        public void RenameGroup(FileData group, string newName)
        {
            if (group == null || !group.IsGroup || string.IsNullOrWhiteSpace(newName)) return;

            newName = EnsureUniqueName(newName, group);

            string oldName = group.Namn;
            group.Namn = newName;
            UpdateFileLinks(oldName, newName, newPath: null);

            RefreshHierarchyState();
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
            // Synced files cannot be converted to groups: the group header would
            // have no file path while still carrying IsFromFolder/SyncFolder,
            // which would corrupt the source folder's baseline tracking.
            if (file.IsFromFolder) return;

            file.IsGroup = true;
            file.Sökväg = string.Empty;
            file.OriginalPath = string.Empty;
            RefreshChildrenFromParent(file, syncCategory: true);

            RefreshHierarchyState();
            NotifyCurrentSelectionStructureChanged();
            MarkDirty();
        }

        /// <summary>
        /// Ensures the given name is unique among all files in the current project.
        /// Appends " (2)", " (3)", etc. when a collision is found.
        /// </summary>
        private string EnsureUniqueName(string name, FileData? exclude = null)
        {
            var existing = new HashSet<string>(
                CurrentProject!.StoredFiles
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
        /// removes the group header itself. Each child keeps its own category
        /// (typically inherited from the group when it was added).
        /// </summary>
        public void DissolveGroup(FileData group)
        {
            if (group == null || !group.IsGroup) return;

            var children = CurrentProject!.GetChildren(group).ToList();

            // AttachedFiles-mode synced children must stay together as a set and
            // cannot be independently detached — remove them and let the next sync
            // re-import them. All other children (free or ProjectFiles-synced) can
            // simply be detached back to top-level since they track individual paths.
            var removableChildren = children.Where(c => IsAttachedFolderFile(c)).ToList();
            var detachableChildren = children.Where(c => !IsAttachedFolderFile(c)).ToList();

            // Detach children that can stand alone as top-level files
            foreach (var child in detachableChildren)
                DetachChild(child, detachedType: null);

            // Remove AttachedFiles-mode synced children; the source folder will
            // re-import them on the next sync if they still exist on disk.
            foreach (var child in removableChildren)
            {
                child.PartOfCollections.Clear();
                CurrentProject!.StoredFiles.Remove(child);
                PreviewVM.RecentFiles.Remove(child);
            }

            CurrentProject!.StoredFiles.Remove(group);
            PreviewVM.RecentFiles.Remove(group);

            // Flag only the folders whose synced children were removed.
            var affectedFolders = removableChildren
                .Where(c => !string.IsNullOrEmpty(c.SyncFolder))
                .Select(c => c.SyncFolder!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (affectedFolders.Count > 0)
                FlagSyncFoldersAsPending(affectedFolders);

            RefreshHierarchyState(refreshCollections: true);
            MarkDirty();
        }

        private IReadOnlyList<FileData>? _cachedAvailableParents;

        /// <summary>
        /// Invalidates the <see cref="AvailableParents"/> cache. Call whenever
        /// the parent/group structure of the project changes.
        /// </summary>
        private void InvalidateAvailableParentsCache() => _cachedAvailableParents = null;

        /// <summary>
        /// Returns all top-level parent targets that can accept nested children.
        /// Includes placeholder parents plus regular files that already have children.
        /// Result is cached and invalidated whenever the hierarchy changes.
        /// </summary>
        public IReadOnlyList<FileData> AvailableParents =>
            _cachedAvailableParents ??= CurrentProject!.StoredFiles
                .Where(f => f.IsParent)
                .OrderByDescending(f => f.IsGroup)
                .ThenBy(f => f.Namn)
                .ToList();

        public IReadOnlyList<FileData> AvailableGroups => AvailableParents;

        /// <summary>
        /// True when there are parent targets in the project to move files into.
        /// </summary>
        public bool HasAvailableParents => AvailableParents.Count > 0;

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
                    file.PartOfCollections.Clear();
                    DetachChild(file);
                    PreviewVM.RecentFiles.Remove(file);
                    CurrentProject!.StoredFiles.Remove(file);
                    continue;
                }

                file.PartOfCollections.Clear();

                var children = CurrentProject!.GetChildren(file);
                foreach (var child in children)
                {
                    if (child.IsFromFolder && !string.IsNullOrEmpty(child.SyncFolder))
                        affectedSyncFolders.Add(child.SyncFolder);

                    child.PartOfCollections.Clear();
                    DetachChild(child);
                    PreviewVM.RecentFiles.Remove(child);
                    CurrentProject!.StoredFiles.Remove(child);
                }

                CurrentProject!.RemoveFile(file);
                PreviewVM.RecentFiles.Remove(file);
            }

            RefreshHierarchyState(refreshCollections: true, updateFilter: false);

            // Reset the type filter when the removed files were the last
            // of their kind, so the grid doesn't stay stuck on an empty type.
            if (type != ALL_TYPES && !CurrentProject!.Filetypes.Contains(type))
            {
                type = ALL_TYPES;
                OnPropertyChanged(nameof(Type));
            }

            UpdateFilter();
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
            if (CurrentFiles == null) return;
            foreach (FileData file in CurrentFiles.Where(f => f.IsTopLevel))
            {
                file.Filtyp = type;

                // Propagate to children so they stay consistent with their parent
                if (file.HasChildren)
                {
                    foreach (var child in CurrentProject!.GetChildren(file))
                        child.Filtyp = type;
                }
            }
            currentProject!.SetFiletypeList();
            UpdateFilter();
            SignalTreeViewUpdate();
            MarkDirty();
        }

        public void UpdateFilter()
        {
            var topLevel = CurrentProject!.StoredFiles.Where(x => x.IsTopLevel);
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
                    var children = CurrentProject!.GetChildren(file)
                        .OrderBy(x => x.Namn);
                    result.AddRange(children);
                }
            }

            filteredFiles.ReplaceAll(result);
            OnPropertyChanged(nameof(NrFilteredFiles));

            if (CurrentProject!.Category != SEARCH_CATEGORY)
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
        /// Determines whether <paramref name="child"/> is a direct child of 
        /// the file identified by <paramref name="parentName"/>.
        /// </summary>
        private static bool IsChildOf(FileData child, string parentName) =>
            string.Equals(child.ParentNamn, parentName, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Collects children of <paramref name="parent"/> in sorted order,
        /// recursively including expanded grandchildren so the result matches
        /// what <see cref="UpdateFilter"/> would produce for this subtree.
        /// </summary>
        private List<FileData> CollectExpandedDescendants(FileData parent)
        {
            var result = new List<FileData>();
            var children = CurrentProject!.GetChildren(parent)
                .OrderBy(x => x.Namn);

            foreach (var child in children)
            {
                result.Add(child);
                if (child.IsExpanded && child.HasChildren)
                    result.AddRange(CollectExpandedDescendants(child));
            }
            return result;
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
        /// Toggles expansion by inserting or removing only the affected child
        /// rows in <see cref="FilteredFiles"/>, avoiding a full list rebuild.
        /// This keeps scroll position stable and reduces visual flicker.
        /// Uses <see cref="IsDescendantOf"/> to correctly handle multi-level
        /// nesting so no orphan rows are left behind on collapse.
        /// </summary>
        public void ToggleExpansionInPlace(FileData file)
        {
            if (file.IsAppendedFile || !file.HasChildren) return;

            file.IsExpanded = !file.IsExpanded;

            int parentIdx = FilteredFiles.IndexOf(file);
            if (parentIdx < 0) { UpdateFilter(); return; }

            if (file.IsExpanded)
            {
                // Insert children and, recursively, any expanded grandchildren
                var toInsert = CollectExpandedDescendants(file);
                FilteredFiles.InsertRange(parentIdx + 1, toInsert);
            }
            else
            {
                // Descendants are contiguous in the list (inserted inline by
                // UpdateFilter). Scan until we leave the descendant block,
                // using IsDescendantOf to also catch multi-level children.
                int removeCount = 0;
                for (int i = parentIdx + 1; i < FilteredFiles.Count; i++)
                {
                    if (IsChildOf(FilteredFiles[i], file.Namn) ||
                        IsDescendantOf(FilteredFiles[i], file))
                        removeCount++;
                    else
                        break;
                }
                if (removeCount > 0)
                    FilteredFiles.RemoveRange(parentIdx + 1, removeCount);
            }

            OnPropertyChanged(nameof(NrFilteredFiles));
        }

        /// <summary>
        /// Expands the selected parent and leaves other parents in their
        /// current user-toggled expansion state.
        /// </summary>
        private void SyncExpansionToSelection()
        {
            var selected = CurrentFile;
            if (selected == null) return;

            // If the selected file is an appended child, keep its parent expanded
            var activeParent = selected.IsAppendedFile ? selected.ParentFile : selected;

            bool changed = false;
            foreach (var file in CurrentProject!.StoredFiles)
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
