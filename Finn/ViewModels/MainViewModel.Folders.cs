using Avalonia.Controls;
using Finn.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            private bool _isSyncing;
            /// <summary>
            /// True while a sync operation is in progress. Prevents concurrent syncs.
            /// </summary>
            public bool IsSyncing
            {
                get => _isSyncing;
                private set { _isSyncing = value; OnPropertyChanged(nameof(IsSyncing)); }
            }

            /// <summary>
            /// Returns <c>true</c> when the current project already contains a
            /// folder entry whose <see cref="FolderData.Path"/> matches
            /// <paramref name="path"/> (case-insensitive).
            /// </summary>
            private bool IsDuplicateFolderPath(string path)
            {
                foreach (var f in CurrentProject!.Folders)
                {
                    if (string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            private FileData? FindAttachedFolderOwner(FolderData folder)
            {
                var topLevel = CurrentProject!.StoredFiles.Where(x => !x.IsAppendedFile);

                if (!string.IsNullOrWhiteSpace(folder.AttachToFilePath))
                {
                    var byPath = topLevel.FirstOrDefault(x =>
                        string.Equals(x.Sökväg, folder.AttachToFilePath, StringComparison.OrdinalIgnoreCase));
                    if (byPath != null)
                        return byPath;
                }

                if (string.IsNullOrWhiteSpace(folder.AttachToFile))
                    return null;

                var matches = topLevel
                    .Where(x => string.Equals(x.Namn, folder.AttachToFile, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                return matches.Count == 1 ? matches[0] : null;
            }

            private List<FileData> GetSyncedAttachedChildren(FileData parent, string folderPath) =>
                CurrentProject!.GetChildren(parent)
                    .Where(x => string.Equals(x.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            private static void AttachChildToParent(FileData child, FileData parent, string type)
            {
                child.SetParent(parent);
                child.Uppdrag = parent.Uppdrag;
                child.Filtyp = type;
            }

            public void NewFileFolder()
            {
                if (CurrentFile != null)
                {
                    CurrentProject!.Folders.Add(new FolderData() { Name = "New file folder", AttachToFile = CurrentFile.Namn, AttachToFilePath = CurrentFile.Sökväg });
                }
            }

            public void RemoveFolders(List<FolderData> folders)
            {
                foreach (var folder in folders)
                {
                    if (folder.Mode == SyncFolderMode.VersionDelivery)
                    {
                        // Remove all versions whose path falls under this version folder.
                        RemoveVersionsUnderPath(folder.Path);
                        CurrentProject!.Folders.Remove(folder);
                    }
                    else if (folder.IsProjectLevel)
                    {
                        CurrentProject!.StoredFiles.RemoveAll(x => x.IsFromFolder && string.Equals(x.SyncFolder, folder.Path, StringComparison.OrdinalIgnoreCase));
                        UpdateFilter();
                        CurrentProject!.Folders.Remove(folder);
                        SignalTreeViewUpdate();
                    }
                    else
                    {
                        FileData? file = FindAttachedFolderOwner(folder);

                        if (file != null)
                        {
                            if (folder.Mode is SyncFolderMode.AttachedFiles)
                            {
                                var removed = GetSyncedAttachedChildren(file, folder.Path);
                                foreach (var r in removed)
                                {
                                    r.PartOfCollections.Clear();
                                    r.ClearParent();
                                    CurrentProject!.StoredFiles.Remove(r);
                                }
                                CurrentProject!.RefreshHasChildren();
                                UpdateFilter();
                            }

                            if (folder.Mode is SyncFolderMode.OtherFiles)
                            {
                                string folderPath = folder.Path;
                                file.OtherFiles.ReplaceAll(
                                    file.OtherFiles.Where(x => !string.Equals(x.SyncFolder, folderPath, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Name));
                            }
                        }

                        Collections.SetCollectionContent();
                        CurrentProject!.Folders.Remove(folder);
                    }
                }

                if (folders.Count > 0)
                {
                    // Clear any pending sync notifications for removed folders
                    foreach (var folder in folders)
                    {
                        for (int i = PendingSyncFolders.Count - 1; i >= 0; i--)
                        {
                            var entry = PendingSyncFolders[i];
                            if (string.Equals(entry.FolderPath, folder.Path, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(entry.ProjectName, CurrentProject!.Namn, StringComparison.OrdinalIgnoreCase))
                            {
                                PendingSyncFolders.RemoveAt(i);
                            }
                        }
                    }
                    RaiseSyncStatusChanged();
                    MarkDirty();
                    RefreshFolderWatchers();
                }
            }

            public async Task SyncFoldersAsync(List<FolderData> folders, Window? mainWindow = null)
            {
                if (IsSyncing) return;
                IsSyncing = true;
                try
                {
                    int fileCountBefore = CurrentProject!.StoredFiles.Count;
                    int otherCountBefore = CurrentProject!.StoredFiles.Sum(f => f.OtherFiles.Count);
                    var syncedPaths = new List<string>();

                    for (int i = 0; i < folders.Count; i++)
                    {
                        PreviewVM.BackgroundTaskActive = true;
                        PreviewVM.BackgroundTaskMessage = $"Syncing folder {i + 1}/{folders.Count}…";
                        PreviewVM.BackgroundTaskProgress = (int)(100.0 * i / folders.Count);

                        bool confirmed = await SyncFolderAsync(folders[i], mainWindow);
                        if (confirmed && !string.IsNullOrEmpty(folders[i].Path))
                            syncedPaths.Add(folders[i].Path);
                    }

                    PreviewVM.BackgroundTaskActive = false;

                    int delta = CurrentProject!.StoredFiles.Count - fileCountBefore;
                    int otherDelta = CurrentProject!.StoredFiles.Sum(f => f.OtherFiles.Count) - otherCountBefore;
                    int totalDelta = delta + otherDelta;

                    if (totalDelta > 0)
                        PreviewVM.StatusMessage = $"Sync complete — {totalDelta} file(s) added";
                    else if (totalDelta < 0)
                        PreviewVM.StatusMessage = $"Sync complete — {-totalDelta} file(s) removed";
                    else
                        PreviewVM.StatusMessage = "All folders up to date";

                    if (syncedPaths.Count > 0)
                    {
                        ClearSyncEntriesByPath(syncedPaths);
                        MarkDirty();
                    }

                    UpdateFilter();
                }
                finally
                {
                    IsSyncing = false;
                }
            }

            /// <summary>
            /// Builds an O(1) name→FileData lookup from all project files.
            /// Includes both parent files and attached children so that version
            /// matching works uniformly across drag-drop, sync folder, and version folder.
            /// Parents take priority via the first pass so they are the preferred
            /// version target when both a parent and child share a name.
            /// </summary>
            private Dictionary<string, FileData> BuildFileNameLookup()
            {
                var lookup = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
                // Parents first — TryAdd keeps the first entry per name.
                foreach (var f in CurrentProject!.StoredFiles.Where(f => !f.IsAppendedFile))
                    lookup.TryAdd(f.Namn, f);
                // Children fill in names that don't have a parent match.
                foreach (var f in CurrentProject!.StoredFiles.Where(f => f.IsAppendedFile))
                    lookup.TryAdd(f.Namn, f);
                return lookup;
            }

            /// <summary>
            /// Collects all paths already registered as current files, originals,
            /// or versions for quick deduplication.
            /// </summary>
            private HashSet<string> BuildKnownPathSet()
            {
                var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in CurrentProject!.StoredFiles)
                {
                    if (!string.IsNullOrEmpty(file.Sökväg))
                        known.Add(file.Sökväg);
                    if (!string.IsNullOrEmpty(file.OriginalPath))
                        known.Add(file.OriginalPath);
                    foreach (var v in file.Versions)
                        if (!string.IsNullOrEmpty(v.Sökväg))
                            known.Add(v.Sökväg);
                }
                return known;
            }

            /// <summary>
            /// Syncs a single folder. Returns <c>true</c> when the sync completed
            /// successfully (or there was nothing to do), <c>false</c> when the
            /// user cancelled the import dialog.
            /// Callers must set <see cref="IsSyncing"/> before calling.
            /// </summary>
            public async Task<bool> SyncFolderAsync(FolderData folder, Window? mainWindow = null)
            {
                if (folder == null || string.IsNullOrEmpty(folder.Path))
                    return false;

                var integrityIssues = ValidateCurrentProjectIntegrity();
                if (integrityIssues.Any(i => i.Severity == IntegrityIssueSeverity.Error))
                {
                    PreviewVM.StatusMessage = "Sync blocked by project integrity issues. Resolve duplicates/ambiguous links first.";
                    return false;
                }

                if (!folder.ExistsOnDisk())
                {
                    PreviewVM.StatusMessage = $"Folder not found: {folder.Name}";
                    return false;
                }

                if (folder.Mode == SyncFolderMode.VersionDelivery)
                {
                    bool versionResult = await SyncVersionFolderAsync(folder, mainWindow);
                    if (versionResult)
                        await RecordSyncBaselineAsync(folder);
                    return versionResult;
                }

                if (!folder.IsProjectLevel)
                {
                    FileData? file = FindAttachedFolderOwner(folder);

                    if (file == null)
                    {
                        PreviewVM.StatusMessage = $"Parent file \"{folder.AttachToFile}\" not found or ambiguous";
                        return false;
                    }

                    if (folder.Mode == SyncFolderMode.AttachedFiles)
                    {
                        // Diff-based sync: keep existing files that still exist
                        // on disk so user-set attributes are preserved.
                        var oldSynced = GetSyncedAttachedChildren(file, folder.Path);
                        var oldByPath = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
                        foreach (var o in oldSynced)
                            oldByPath.TryAdd(o.Sökväg, o);

                        var diskFiles = await Task.Run(() => GetFilesFromFolder(folder, CurrentProject!.Namn));
                        var diskPaths = new HashSet<string>(
                            diskFiles.Select(f => f.Sökväg), StringComparer.OrdinalIgnoreCase);

                        var removals = oldSynced.Where(o => !diskPaths.Contains(o.Sökväg)).ToList();

                        // Exclude paths already tracked anywhere in the project (e.g. files
                        // that were detached from this folder but kept as top-level entries).
                        var allTrackedPaths = new HashSet<string>(
                            CurrentProject!.StoredFiles.Select(x => x.Sökväg),
                            StringComparer.OrdinalIgnoreCase);
                        var additions = diskFiles
                            .Where(f => !oldByPath.ContainsKey(f.Sökväg) && !allTrackedPaths.Contains(f.Sökväg))
                            .ToList();

                        if (removals.Count == 0 && additions.Count == 0)
                        {
                            // Nothing changed
                            await RecordSyncBaselineAsync(folder);
                            return true;
                        }

                        // Show removal dialog when files disappeared from disk
                        if (removals.Count > 0 && mainWindow != null)
                        {
                            string folderName = new DirectoryInfo(folder.Path).Name;
                            var removeEntries = removals.Select(f => (f.Namn, f.Sökväg)).ToList();
                            var removeDia = new Dialogs.xSyncRemoveDia
                            {
                                DataContext = this,
                                RequestedThemeVariant = mainWindow.ActualThemeVariant
                            };
                            removeDia.SetFiles(removeEntries, folderName);
                            await removeDia.ShowDialog(mainWindow);

                            if (removeDia.Confirmed)
                            {
                                foreach (var r in removals)
                                {
                                    r.PartOfCollections.Clear();
                                    r.ClearParent();
                                    CurrentProject.StoredFiles.Remove(r);
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }
                        else if (removals.Count > 0)
                        {
                            // No window — silently remove
                            foreach (var r in removals)
                            {
                                r.PartOfCollections.Clear();
                                r.ClearParent();
                                CurrentProject.StoredFiles.Remove(r);
                            }
                        }

                        // Show import dialog when new files appeared on disk
                        if (additions.Count > 0 && mainWindow != null)
                        {
                            string folderName = new DirectoryInfo(folder.Path).Name;
                            string defaultCategory = file.Filtyp;
                            var candidatePaths = additions.Select(f => (f.Sökväg, folderName)).ToList();
                            var importDia = new Dialogs.xImportDia
                            {
                                DataContext = this,
                                RequestedThemeVariant = mainWindow.ActualThemeVariant
                            };
                            importDia.SetFiles(candidatePaths, 0, CurrentProject.AllowedTypes, defaultCategory);
                            await importDia.ShowDialog(mainWindow);

                            if (importDia.Confirmed)
                            {
                                string assignedType = importDia.SelectedCategory;
                                var acceptedSet = new HashSet<string>(importDia.AcceptedPaths, StringComparer.OrdinalIgnoreCase);

                                foreach (var f in additions)
                                {
                                    if (!acceptedSet.Contains(f.Sökväg)) continue;
                                    AttachChildToParent(f, file, assignedType);
                                    CurrentProject.StoredFiles.Add(f);
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }
                        else if (additions.Count > 0)
                        {
                            // No window — silently add all
                            foreach (var f in additions)
                            {
                                AttachChildToParent(f, file, file.Filtyp);
                                CurrentProject.StoredFiles.Add(f);
                            }
                        }

                        CurrentProject.RefreshHasChildren();
                    }
                    else if (folder.Mode == SyncFolderMode.OtherFiles)
                    {
                        var remaining = file.OtherFiles
                            .Where(x => !string.Equals(x.SyncFolder, folder.Path, StringComparison.OrdinalIgnoreCase));
                        var newFiles = await Task.Run(() => GetOtherFilesFromFolder(folder));
                        file.OtherFiles.ReplaceAll(remaining.Concat(newFiles).OrderBy(x => x.Name));
                    }

                    await RecordSyncBaselineAsync(folder);
                    return true;
                }

                return await SyncProjectFolderAsync(folder, mainWindow);
            }

            /// <summary>
            /// Syncs a project-level folder: computes what changed on disk,
            /// shows the appropriate confirmation dialogs, and applies the changes.
            /// </summary>
            private async Task<bool> SyncProjectFolderAsync(FolderData folder, Window? mainWindow)
            {
                string folderName = new DirectoryInfo(folder.Path).Name;
                PreviewVM.BackgroundTaskActive = true;
                PreviewVM.BackgroundTaskMessage = $"Scanning \"{folderName}\"…";
                PreviewVM.BackgroundTaskProgress = -1;

                var diff = await ComputeProjectFolderDiffAsync(folder);

                PreviewVM.BackgroundTaskActive = false;
                bool userCancelled = false;

                // Nothing on disk has changed — show a brief notification and heal the baseline
                if (diff.Removals.Count == 0 && diff.Additions.Count == 0
                    && diff.VersionCandidates.Count == 0 && diff.SkippedCount == 0)
                {
                    await RecordSyncBaselineAsync(folder);
                    if (mainWindow != null)
                    {
                        var msgDia = new Dialogs.xMessageDia
                        {
                            RequestedThemeVariant = mainWindow.ActualThemeVariant
                        };
                        msgDia.SetMessage($"Folder \"{folderName}\" is already up to date.\nBaseline refreshed.");
                        await msgDia.ShowDialog(mainWindow);
                    }
                    return true;
                }

                // 1. Handle removals
                if (diff.Removals.Count > 0)
                {
                    userCancelled = !await ConfirmSyncRemovalsAsync(diff.Removals, folder, mainWindow);
                }

                // 2. Handle additions (new files + version candidates)
                if (!userCancelled && (diff.Additions.Count > 0 || diff.VersionCandidates.Count > 0))
                {
                    userCancelled = !await ConfirmSyncAdditionsAsync(
                        diff.Additions, diff.VersionCandidates, diff.SkippedCount, folder, mainWindow);
                }

                CurrentProject!.SetFiletypeList();
                UpdateFilter();
                BuildTreeData();

                if (!userCancelled)
                    await RecordSyncBaselineAsync(folder);

                return !userCancelled;
            }

            /// <summary>
            /// Result of comparing disk files against tracked files for a project-level folder.
            /// </summary>
            private sealed class ProjectFolderDiff
            {
                public List<FileData> Removals { get; init; } = [];
                public List<FileData> Additions { get; init; } = [];
                public List<VersionImportEntry> VersionCandidates { get; init; } = [];
                public int SkippedCount { get; init; }
            }

            /// <summary>
            /// Computes what needs to be added/removed to bring the project in
            /// sync with the folder on disk. Runs the directory scan on a
            /// background thread so the UI stays responsive.
            /// </summary>
            private async Task<ProjectFolderDiff> ComputeProjectFolderDiffAsync(FolderData folder)
            {
                string projectName = CurrentProject!.Namn;
                var diskFiles = await Task.Run(() => GetFilesFromFolder(folder, projectName));
                var diskPaths = new HashSet<string>(diskFiles.Select(f => f.Sökväg), StringComparer.OrdinalIgnoreCase);

                var existingFiles = CurrentProject!.StoredFiles
                    .Where(x => x.IsFromFolder
                        && string.Equals(x.SyncFolder, folder.Path, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var existingPaths = new HashSet<string>(existingFiles.Select(f => f.Sökväg), StringComparer.OrdinalIgnoreCase);

                var removals = existingFiles.Where(p => !diskPaths.Contains(p.Sökväg)).ToList();
                var filesToAdd = diskFiles.Where(p => !existingPaths.Contains(p.Sökväg)).ToList();

                // Classify additions: skip duplicates (adopt orphans), match
                // existing names as version candidates, or treat as new files.
                // Also skip paths that are already registered as versions on any
                // project file — they were auto-grouped on import and are tracked
                // even though they don't appear as top-level StoredFiles entries.
                var versionPaths = new HashSet<string>(
                    CurrentProject!.StoredFiles.SelectMany(f => f.Versions.Select(v => v.Sökväg)),
                    StringComparer.OrdinalIgnoreCase);

                var additions = new List<FileData>();
                var versionCandidates = new List<VersionImportEntry>();
                var filesByName = BuildFileNameLookup();
                int skippedCount = 0;

                foreach (var file in filesToAdd)
                {
                    // Already tracked as a version — silently skip
                    if (versionPaths.Contains(file.Sökväg))
                    {
                        skippedCount++;
                        continue;
                    }

                    var alreadyTracked = CurrentProject!.StoredFiles.FirstOrDefault(
                        x => string.Equals(x.Sökväg, file.Sökväg, StringComparison.OrdinalIgnoreCase));
                    if (alreadyTracked != null)
                    {
                        AdoptFileIntoFolder(alreadyTracked, folder);
                        skippedCount++;
                        continue;
                    }

                    if (filesByName.TryGetValue(file.Namn, out var existing))
                    {
                        versionCandidates.Add(new VersionImportEntry
                        {
                            ExistingFile = existing,
                            NewFilePath = file.Sökväg
                        });
                    }
                    else
                    {
                        additions.Add(file);
                    }
                }

                return new ProjectFolderDiff
                {
                    Removals = removals,
                    Additions = additions,
                    VersionCandidates = versionCandidates,
                    SkippedCount = skippedCount
                };
            }

            /// <summary>
            /// Claims an existing file for a sync folder so the set-based deep
            /// check recognises it as tracked. Used when a file was imported
            /// manually before the sync folder was created.
            /// </summary>
            private static void AdoptFileIntoFolder(FileData file, FolderData folder)
            {
                if (string.IsNullOrEmpty(file.SyncFolder))
                {
                    file.SyncFolder = folder.Path;
                    file.IsFromFolder = true;
                }
            }

            /// <summary>
            /// Shows the removal confirmation dialog and applies the removals
            /// if confirmed. Returns <c>true</c> when confirmed, <c>false</c> on cancel.
            /// </summary>
            private async Task<bool> ConfirmSyncRemovalsAsync(
                List<FileData> filesToRemove, FolderData folder, Window? mainWindow)
            {
                if (mainWindow == null)
                    return false; // No window — can't show dialog

                string folderName = new DirectoryInfo(folder.Path).Name;
                var removeEntries = filesToRemove.Select(f => (f.Namn, f.Sökväg)).ToList();
                var removeDia = new Dialogs.xSyncRemoveDia
                {
                    DataContext = this,
                    RequestedThemeVariant = mainWindow.ActualThemeVariant
                };
                removeDia.SetFiles(removeEntries, folderName);
                await removeDia.ShowDialog(mainWindow);

                if (removeDia.Confirmed)
                {
                    var removeSet = new HashSet<FileData>(filesToRemove);
                    CurrentProject!.StoredFiles.RemoveAll(f => removeSet.Contains(f));
                    return true;
                }
                return false;
            }

            /// <summary>
            /// Shows the import dialog for new files and the version import dialog
            /// for name-matched files. Returns <c>true</c> when all dialogs were
            /// confirmed, <c>false</c> if the user cancelled at any step.
            /// </summary>
            private async Task<bool> ConfirmSyncAdditionsAsync(
                List<FileData> additions, List<VersionImportEntry> versionCandidates,
                int skippedCount, FolderData folder, Window? mainWindow)
            {
                if (mainWindow == null && additions.Count > 0)
                    return false; // No window — can't show dialog

                if (additions.Count > 0 && mainWindow != null)
                {
                    string folderName = new DirectoryInfo(folder.Path).Name;
                    string? defaultCategory = (Type != null && Type != ALL_TYPES) ? Type : null;

                    var candidatePaths = additions.Select(f => (f.Sökväg, folderName)).ToList();
                    var dialog = new Dialogs.xImportDia
                    {
                        DataContext = this,
                        RequestedThemeVariant = mainWindow.ActualThemeVariant
                    };
                    dialog.SetFiles(candidatePaths, skippedCount, CurrentProject!.AllowedTypes, defaultCategory);
                    await dialog.ShowDialog(mainWindow);

                    if (dialog.Confirmed)
                    {
                        string assignedType = dialog.SelectedCategory;
                        var acceptedSet = new HashSet<string>(dialog.AcceptedPaths, StringComparer.OrdinalIgnoreCase);

                        var confirmed = additions.Where(f => acceptedSet.Contains(f.Sökväg)).ToList();
                        foreach (var f in confirmed)
                            f.Filtyp = assignedType;

                        if (confirmed.Count > 0)
                        {
                            CurrentProject!.StoredFiles.AddRange(confirmed);
                            var newPaths = new HashSet<string>(
                                confirmed.Select(f => f.Sökväg),
                                StringComparer.OrdinalIgnoreCase);
                            AutoGroupVersions(CurrentProject!, newPaths);
                        }
                    }
                    else
                    {
                        return false;
                    }
                }

                if (versionCandidates.Count > 0 && mainWindow != null)
                {
                    bool confirmed = await ShowVersionImportDialogAsync(mainWindow, versionCandidates);
                    if (confirmed)
                    {
                        foreach (var entry in versionCandidates)
                            entry.ExistingFile.AddVersion(entry.NewFilePath, entry.SelectedLabel);
                    }
                    // Declining the version import is treated as "skip, not cancel" —
                    // the folder is still considered synced so it clears from the pending list.
                }

                return true;
            }

            public void NewVersionFolder(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    return;

                if (IsDuplicateFolderPath(path))
                    return;

                var folder = new FolderData
                {
                    Name = new DirectoryInfo(path).Name,
                    AttachToFile = "PROJECT",
                    Types = VERSIONS_TYPE,
                    Path = path
                };
                CurrentProject!.Folders.Add(folder);
                MarkDirty();
                RefreshFolderWatchers();
            }

            /// <summary>
            /// Counts all versions across all project files whose path falls
            /// under <paramref name="folderPath"/>. Used to display the total
            /// synced version count regardless of when they were imported.
            /// </summary>
            private int CountVersionsUnderPath(string folderPath)
            {
                string root = folderPath.EndsWith(Path.DirectorySeparatorChar)
                    ? folderPath
                    : folderPath + Path.DirectorySeparatorChar;
                int count = 0;
                foreach (var f in CurrentProject!.StoredFiles)
                    foreach (var v in f.Versions)
                        if (v.Sökväg.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            count++;
                return count;
            }

            /// <summary>
            /// Removes all versions whose path falls under <paramref name="folderPath"/>
            /// from every project file. Cleans up OriginalPath when the last version is removed.
            /// </summary>
            private void RemoveVersionsUnderPath(string folderPath)
            {
                string root = folderPath.EndsWith(Path.DirectorySeparatorChar)
                    ? folderPath
                    : folderPath + Path.DirectorySeparatorChar;
                foreach (var file in CurrentProject!.StoredFiles)
                {
                    int before = file.Versions.Count;
                    for (int i = file.Versions.Count - 1; i >= 0; i--)
                    {
                        if (file.Versions[i].Sökväg.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            file.Versions.RemoveAt(i);
                    }
                    if (before > 0 && file.Versions.Count == 0 && !string.IsNullOrEmpty(file.OriginalPath))
                    {
                        // Last version removed — clear the original path marker
                        file.OriginalPath = string.Empty;
                    }
                }
            }

            /// <summary>
            /// Scans a folder and all subfolders for PDFs that match existing project files
            /// by name, then presents a delivery import dialog grouped by subfolder.
            /// Each delivery folder gets one label applied to all matched files.
            /// Already-registered version paths are skipped.
            /// Returns <c>true</c> when the sync completed (or nothing to do),
            /// <c>false</c> when the user cancelled the delivery import dialog.
            /// </summary>
            private async Task<bool> SyncVersionFolderAsync(FolderData folder, Window? mainWindow = null)
            {
                if (folder?.ExistsOnDisk() != true || folder.Path == null) return false;

                string folderName = new DirectoryInfo(folder.Path).Name;
                PreviewVM.BackgroundTaskActive = true;
                PreviewVM.BackgroundTaskMessage = $"Scanning \"{folderName}\"…";
                PreviewVM.BackgroundTaskProgress = -1;

                // Collect all PDFs recursively on a background thread
                // to avoid blocking the UI on network shares.
                var allPdfs = await Task.Run(() =>
                    Directory.EnumerateFiles(folder.Path, "*.pdf", SearchOption.AllDirectories)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ToList());

                PreviewVM.BackgroundTaskMessage = $"Matching versions in \"{folderName}\"…";
                PreviewVM.BackgroundTaskProgress = 50;

                // Remove stale versions whose files no longer exist on disk
                var diskPaths = new HashSet<string>(allPdfs, StringComparer.OrdinalIgnoreCase);
                string root = folder.Path.EndsWith(Path.DirectorySeparatorChar)
                    ? folder.Path
                    : folder.Path + Path.DirectorySeparatorChar;

                // Collect stale versions before removing
                var staleVersions = new List<(FileData File, FileVersionData Version)>();
                foreach (var file in CurrentProject!.StoredFiles)
                {
                    foreach (var v in file.Versions)
                    {
                        if (v.Sökväg.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                            && !diskPaths.Contains(v.Sökväg))
                        {
                            staleVersions.Add((file, v));
                        }
                    }
                }

                if (staleVersions.Count > 0 && mainWindow != null)
                {
                    PreviewVM.BackgroundTaskActive = false;
                    var removeEntries = staleVersions
                        .Select(sv => (sv.Version.ShortName, sv.Version.Sökväg))
                        .ToList();
                    var removeDia = new Dialogs.xSyncRemoveDia
                    {
                        DataContext = this,
                        RequestedThemeVariant = mainWindow.ActualThemeVariant
                    };
                    removeDia.SetFiles(removeEntries, folderName);
                    await removeDia.ShowDialog(mainWindow);

                    if (removeDia.Confirmed)
                    {
                        foreach (var (file, v) in staleVersions)
                            file.RemoveVersion(v);
                        MarkDirty();
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (staleVersions.Count > 0)
                {
                    // No window — can't show dialog, leave unsynced
                    return false;
                }

                // Shared lookup: includes parents AND attached children
                var filesByName = BuildFileNameLookup();
                int totalProjectFiles = CurrentProject.StoredFiles.Count;

                // Shared dedup: all paths already registered as current, original, or version
                var knownPaths = BuildKnownPathSet();

                // Group matched PDFs by their parent subfolder
                var matchesPerFolder = new Dictionary<string, List<(FileData File, string PdfPath)>>(StringComparer.OrdinalIgnoreCase);

                foreach (string pdfPath in allPdfs)
                {
                    if (knownPaths.Contains(pdfPath))
                        continue;

                    string name = Path.GetFileNameWithoutExtension(pdfPath);
                    if (folder.IsExcludedPath(pdfPath))
                        continue;

                    if (filesByName.TryGetValue(name, out var existing))
                    {
                        string dir = Path.GetDirectoryName(pdfPath)!;
                        if (!matchesPerFolder.TryGetValue(dir, out var list))
                        {
                            list = [];
                            matchesPerFolder[dir] = list;
                        }
                        list.Add((existing, pdfPath));
                    }
                }

                if (matchesPerFolder.Count == 0)
                {
                    // No new files to import — caller records baseline.
                    PreviewVM.BackgroundTaskActive = false;
                    PreviewVM.StatusMessage = $"Version folder \"{folderName}\" — no new versions found";
                    return true;
                }

                // Build delivery entries — one row per subfolder, auto-label from date or letter.
                // Multiple subfolders under the same date folder share the same label,
                // which is fine — AddVersion allows duplicate labels on the same file.
                var deliveries = new List<DeliveryFolderEntry>();
                int letterIndex = 0;

                foreach (var (dir, matches) in matchesPerFolder.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                {
                    string label = TryExtractDateFromPath(dir, folder.Path)
                        ?? GetSequentialLabel(letterIndex++);

                    deliveries.Add(new DeliveryFolderEntry
                    {
                        FolderPath = dir,
                        MatchedFiles = matches,
                        TotalProjectFiles = totalProjectFiles,
                        SelectedLabel = label
                    });
                }

                PreviewVM.BackgroundTaskActive = false;

                if (mainWindow != null)
                {
                    bool confirmed = await ShowDeliveryImportDialogAsync(mainWindow, deliveries);
                    if (confirmed)
                    {
                        foreach (var delivery in deliveries.Where(d => d.IsIncluded))
                        {
                            foreach (var (file, pdfPath) in delivery.MatchedFiles)
                            {
                                file.AddVersion(pdfPath, delivery.SelectedLabel);
                            }
                        }
                        MarkDirty();
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    // No window — can't show dialog, leave unsynced
                    return false;
                }

                // Caller records baseline via RecordSyncBaseline.
                return true;
            }

            /// <summary>
            /// Returns a sequential label: 0→A, 1→B, … 25→Z, 26→AA, 27→AB, etc.
            /// </summary>
            private static string GetSequentialLabel(int index)
            {
                if (index < 26)
                    return ((char)('A' + index)).ToString();
                return GetSequentialLabel(index / 26 - 1) + (char)('A' + index % 26);
            }

            /// <summary>
            /// Attempts to extract a date from anywhere in <paramref name="name"/>.
            /// Tries yyyy-MM-dd first (e.g. "2024-01-15 Granskningshandling"),
            /// then yyMMdd (e.g. "240115_BH"). The returned label is always yyyy-MM-dd.
            /// </summary>
            private static bool TryExtractDate(string name, out string date)
            {
                // Try yyyy-MM-dd first
                var match = DatePatternLong().Match(name);
                if (match.Success
                    && DateTime.TryParseExact(match.Value, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    date = match.Value;
                    return true;
                }

                // Fall back to yyMMdd (6 digits, no separators)
                match = DatePatternShort().Match(name);
                if (match.Success
                    && DateTime.TryParseExact(match.Value, "yyMMdd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                {
                    date = parsed.ToString("yyyy-MM-dd");
                    return true;
                }

                date = string.Empty;
                return false;
            }

            /// <summary>
            /// Walks from <paramref name="dir"/> up the directory tree (stopping at
            /// <paramref name="root"/>) looking for a folder name that contains a date.
            /// Returns the date string or null when none is found.
            /// </summary>
            private static string? TryExtractDateFromPath(string dir, string? root)
            {
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
                {
                    string? folderName = Path.GetFileName(dir);
                    if (string.IsNullOrEmpty(folderName)) break;

                    if (TryExtractDate(folderName, out string date))
                        return date;

                    // Don't walk above the configured root folder
                    if (root != null && string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                        break;

                    string? parent = Path.GetDirectoryName(dir);
                    if (parent == null || parent == dir) break;
                    dir = parent;
                }
                return null;
            }

            [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
            private static partial Regex DatePatternLong();

            [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)")]
            private static partial Regex DatePatternShort();

            private List<FileData> GetFilesFromFolder(FolderData folder, string projectName)
            {
                List<FileData> files = new();

                var (pattern, search) = GetFileFilter(folder);
                foreach (string path in Directory.GetFiles(folder.Path, pattern, search))
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (folder.IsExcluded(fileName)) continue;

                    files.Add(new FileData()
                    {
                        Namn = fileName,
                        Sökväg = path,
                        Uppdrag = projectName,
                        Filtyp = NEW_TYPE,
                        SyncFolder = folder.Path,
                        IsFromFolder = true
                    });
                }

                return files;
            }

            private List<OtherData> GetOtherFilesFromFolder(FolderData folder)
            {
                List<OtherData> files = new();

                foreach (string path in Directory.GetFiles(folder.Path))
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (folder.IsExcluded(fileName)) continue;

                    OtherData newFile = new()
                    {
                        Name = fileName,
                        Filepath = path,
                        SyncFolder = folder.Path,
                        IsFromFolder = true
                    };

                    newFile.SetFile();
                    files.Add(newFile);
                }

                return files;
            }

            /// <summary>
            /// Shows the sync-filter dialog for a folder, letting the user
            /// check/uncheck individual files. On confirm, updates the exclusion
            /// list, removes newly-excluded files, and re-syncs if files were
            /// re-included so they appear immediately.
            /// </summary>
            public async Task ShowSyncFilterDialogAsync(FolderData folder, Window mainWindow)
            {
                if (folder == null || !folder.ExistsOnDisk()) return;

                var (pattern, search) = GetFileFilter(folder);

                // For version delivery folders, only show files that match
                // an existing project file by name — the rest are irrelevant.
                IReadOnlySet<string>? versionFilter = null;
                if (folder.Mode == SyncFolderMode.VersionDelivery)
                {
                    versionFilter = new HashSet<string>(
                        CurrentProject!.StoredFiles.Select(f => f.Namn),
                        StringComparer.OrdinalIgnoreCase);
                }

                PreviewVM.BackgroundTaskActive = true;
                PreviewVM.BackgroundTaskMessage = "Scanning folder…";
                PreviewVM.BackgroundTaskProgress = -1;

                var dialog = new Dialogs.xSyncFilterDia
                {
                    DataContext = this,
                    RequestedThemeVariant = mainWindow.ActualThemeVariant
                };
                await dialog.SetFolderAsync(folder, pattern, search, versionFilter);

                PreviewVM.BackgroundTaskActive = false;

                await dialog.ShowDialog(mainWindow);

                if (!dialog.Confirmed) return;

                if (folder.Mode == SyncFolderMode.VersionDelivery)
                {
                    // Version folders use path-based exclusion because the same
                    // file name can appear in multiple delivery subfolders.
                    var newExcludedPaths = dialog.GetExcludedFilePaths();
                    var oldExcludedSet = new HashSet<string>(folder.ExcludedFiles, StringComparer.OrdinalIgnoreCase);
                    var newExcludedSet = new HashSet<string>(newExcludedPaths, StringComparer.OrdinalIgnoreCase);

                    // Paths that were just unchecked → remove matching versions
                    var justExcludedPaths = newExcludedSet.Except(oldExcludedSet).ToList();
                    if (justExcludedPaths.Count > 0)
                    {
                        // Find versions whose exact path matches a newly-excluded disk path
                        var versionsToRemove = new List<(FileData File, FileVersionData Version)>();
                        var pathSet = new HashSet<string>(justExcludedPaths, StringComparer.OrdinalIgnoreCase);
                        foreach (var file in CurrentProject!.StoredFiles)
                        {
                            foreach (var v in file.Versions)
                            {
                                if (pathSet.Contains(v.Sökväg))
                                    versionsToRemove.Add((file, v));
                            }
                        }

                        if (versionsToRemove.Count > 0)
                        {
                            string folderName = new DirectoryInfo(folder.Path).Name;
                            var removeEntries = versionsToRemove
                                .Select(sv => (sv.Version.ShortName, sv.Version.Sökväg))
                                .ToList();
                            string subtitle = versionsToRemove.Count == 1
                                ? "1 version will be removed"
                                : $"{versionsToRemove.Count} versions will be removed";
                            var removeDia = new Dialogs.xSyncRemoveDia
                            {
                                DataContext = this,
                                RequestedThemeVariant = mainWindow.ActualThemeVariant
                            };
                            removeDia.SetFiles(removeEntries, folderName, subtitle);
                            await removeDia.ShowDialog(mainWindow);

                            if (removeDia.Confirmed)
                            {
                                foreach (var (file, v) in versionsToRemove)
                                    file.RemoveVersion(v);
                            }
                            else
                            {
                                return; // User declined — abort
                            }
                        }
                    }

                    folder.ExcludedFiles = newExcludedPaths;
                    folder.InvalidateExclusionCache();

                    CurrentProject!.RefreshHasChildren();
                    UpdateFilter();
                    MarkDirty();

                    // Re-include
                    bool hasReIncluded = oldExcludedSet.Except(newExcludedSet).Any();
                    if (hasReIncluded)
                    {
                        await SyncFolderAsync(folder, mainWindow);
                        UpdateFilter();
                    }
                    else
                    {
                        await RecordSyncBaselineAsync(folder);
                    }
                }
                else
                {
                    // Non-version folders use name-based exclusion (original logic)
                    var newExcluded = dialog.GetExcludedFileNames();
                    var oldExcluded = new HashSet<string>(folder.ExcludedFiles, StringComparer.OrdinalIgnoreCase);
                    var newExcludedSet = new HashSet<string>(newExcluded, StringComparer.OrdinalIgnoreCase);

                    var justExcluded = newExcludedSet.Except(oldExcluded).ToList();
                    if (justExcluded.Count > 0)
                    {
                        var nameSet = new HashSet<string>(justExcluded, StringComparer.OrdinalIgnoreCase);
                        var filesToRemove = CurrentProject!.StoredFiles
                            .Where(f => f.IsFromFolder
                                && string.Equals(f.SyncFolder, folder.Path, StringComparison.OrdinalIgnoreCase)
                                && nameSet.Contains(f.Namn))
                            .ToList();
                        foreach (var f in filesToRemove)
                            CurrentProject!.StoredFiles.Remove(f);
                    }

                    folder.ExcludedFiles = newExcluded;
                    folder.InvalidateExclusionCache();

                    CurrentProject!.RefreshHasChildren();
                    UpdateFilter();
                    MarkDirty();

                    bool hasReIncluded = oldExcluded.Except(newExcludedSet).Any();
                    if (hasReIncluded)
                    {
                        await SyncFolderAsync(folder, mainWindow);
                        UpdateFilter();
                    }
                    else
                    {
                        await RecordSyncBaselineAsync(folder);
                    }
                }
            }

        /// <summary>
        /// Updates file paths when a synced folder is moved to a new directory,
        /// so files keep their correct paths instead of being treated as removals + new additions on the next sync.
        /// </summary>
        public void RelocateFolderPaths(FolderData folder, string oldFolderPath, string newFolderPath)
        {
            string oldPrefix = oldFolderPath.EndsWith(Path.DirectorySeparatorChar)
                ? oldFolderPath
                : oldFolderPath + Path.DirectorySeparatorChar;
            string newPrefix = newFolderPath.EndsWith(Path.DirectorySeparatorChar)
                ? newFolderPath
                : newFolderPath + Path.DirectorySeparatorChar;

            foreach (var file in CurrentProject!.StoredFiles)
            {
                if (string.Equals(file.SyncFolder, oldFolderPath, StringComparison.OrdinalIgnoreCase))
                    file.SyncFolder = newFolderPath;

                if (!string.IsNullOrEmpty(file.Sökväg))
                {
                    if (file.Sökväg.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        file.Sökväg = newPrefix + file.Sökväg.Substring(oldPrefix.Length);
                    else if (string.Equals(Path.GetDirectoryName(file.Sökväg), oldFolderPath, StringComparison.OrdinalIgnoreCase))
                        file.Sökväg = Path.Combine(newFolderPath, Path.GetFileName(file.Sökväg));
                }

                if (!string.IsNullOrEmpty(file.OriginalPath))
                {
                    if (file.OriginalPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        file.OriginalPath = newPrefix + file.OriginalPath.Substring(oldPrefix.Length);
                    else if (string.Equals(Path.GetDirectoryName(file.OriginalPath), oldFolderPath, StringComparison.OrdinalIgnoreCase))
                        file.OriginalPath = Path.Combine(newFolderPath, Path.GetFileName(file.OriginalPath));
                }

                foreach (var v in file.Versions)
                {
                    if (!string.IsNullOrEmpty(v.Sökväg) && v.Sökväg.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        v.Sökväg = newPrefix + v.Sökväg.Substring(oldPrefix.Length);
                }

                foreach (var other in file.OtherFiles)
                {
                    if (string.Equals(other.SyncFolder, oldFolderPath, StringComparison.OrdinalIgnoreCase))
                        other.SyncFolder = newFolderPath;

                    if (!string.IsNullOrEmpty(other.Filepath) && other.Filepath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        other.Filepath = newPrefix + other.Filepath.Substring(oldPrefix.Length);
                }
            }
        }
    }
}
