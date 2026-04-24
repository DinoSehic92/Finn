using Finn.Model;
using iText.Kernel.XMP.Impl.XPath;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

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

                // Use backing fields to avoid cascading UpdateFilter calls
                // from both the CurrentProject and Type property setters.
                currentProject = newProject;
                type = ALL_TYPES;
                UpdateFilter();
                OnPropertyChanged(nameof(CurrentProject));
                OnPropertyChanged(nameof(IsSearchResult));
                OnPropertyChanged(nameof(Type));

                SetProjectlist();
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
            Collections.SetCollectionContent();
            RefreshFolderWatchers();
            MarkDirty();
        }

        public void RemoveProjects(List<ProjectData> list)
        {
            foreach (ProjectData project in list)
            {
                Storage.StoredProjects.Remove(project);
                foreach (FileData file in project.StoredFiles)
                {
                    PreviewVM.RecentFiles.Remove(file);
                }
            }

            SetProjectlist();
            SetDefaultSelection();
            SortProjects();
            Collections.SetCollectionContent();
        }

        public void RenameProject(string projectName)
        {
            if (CurrentProject.IsShared) return;

            CurrentProject.Namn = projectName;

            foreach (FileData file in CurrentProject.StoredFiles)
            {
                file.Uppdrag = projectName;
            }
            CurrentProject.SetFiletypeList();
            MarkDirty();
        }

        public void Renameproject(string newProjectName)
        {
            RenameProject(newProjectName);
            SetProjectlist();
        }

        public void GetGroups()
        {
            // Groups are now managed via Storage.ProjectGroups; nothing to recompute.
        }

        /// <summary>
        /// One-time migration: for any ProjectData.Parent string that has no
        /// corresponding GroupData record yet, create a root-level GroupData for it.
        /// Called once from DeserializeLoadFile — not on every tree rebuild.
        /// Checks per-name so partially-migrated files are handled correctly.
        /// </summary>
        public void MigrateGroupsOnLoad()
        {
            var existingNames = new HashSet<string>(
                Storage.ProjectGroups.Select(g => g.Name),
                StringComparer.OrdinalIgnoreCase);

            int sortOrder = Storage.ProjectGroups.Count > 0
                ? Storage.ProjectGroups.Max(g => g.SortOrder) + 1 : 0;

            foreach (var project in Storage.StoredProjects)
            {
                if (string.IsNullOrWhiteSpace(project.Parent)) continue;
                if (existingNames.Contains(project.Parent)) continue;

                Storage.ProjectGroups.Add(new Finn.Model.GroupData
                {
                    Name = project.Parent,
                    ParentGroup = null,
                    Category = project.Category,
                    SortOrder = sortOrder++
                });
                existingNames.Add(project.Parent);
            }
        }

        /// <summary>
        /// Creates a new GroupData record and optionally places it inside a parent group.
        /// </summary>
        public Finn.Model.GroupData AddProjectGroup(string name, string category = PROJECT_CATEGORY, string? parentGroup = null)
        {
            // Enforce one-level nesting: reject parentGroup if it already has a ParentGroup itself
            if (parentGroup != null)
            {
                var parentRecord = Storage.ProjectGroups.FirstOrDefault(g => g.Name == parentGroup);
                if (parentRecord != null && !string.IsNullOrEmpty(parentRecord.ParentGroup))
                    parentGroup = parentRecord.ParentGroup; // promote to grandparent so depth stays ≤ 1
            }

            // Compute SortOrder within the same category + parent scope, not globally
            int sortOrder = Storage.ProjectGroups
                .Where(g => g.Category == category && g.ParentGroup == parentGroup)
                .Select(g => g.SortOrder)
                .DefaultIfEmpty(-1)
                .Max() + 1;

            var group = new Finn.Model.GroupData
            {
                Name = EnsureUniqueGroupName(name, category),
                ParentGroup = parentGroup,
                Category = category,
                SortOrder = sortOrder
            };

            Storage.ProjectGroups.Add(group);
            MarkDirty();
            return group;
        }

        /// <summary>
        /// Renames a GroupData record and updates all ProjectData.Parent references.
        /// </summary>
        public void RenameProjectGroup(Finn.Model.GroupData group, string newName)
        {
            if (group == null || string.IsNullOrWhiteSpace(newName)) return;
            newName = EnsureUniqueGroupName(newName, group.Category, group);

            string oldName = group.Name;
            group.Name = newName;

            // Also update the ParentGroup pointer on any subgroups referencing this group
            foreach (var sub in Storage.ProjectGroups.Where(g => g.ParentGroup == oldName))
                sub.ParentGroup = newName;

            // Update all projects that were in this group
            foreach (var project in Storage.StoredProjects.Where(p => p.Parent == oldName))
                project.Parent = newName;

            MarkDirty();
        }

        /// <summary>
        /// Removes a GroupData record. Projects in the group are moved to the parent group
        /// (or made top-level if there is no parent). Subgroups are promoted to the same level.
        /// </summary>
        public void RemoveProjectGroup(Finn.Model.GroupData group)
        {
            if (group == null) return;

            string? newParent = group.ParentGroup;

            // Re-parent subgroups to this group's parent (one level up).
            // Also ensure their Category matches the deleted group's category in case of data drift.
            foreach (var sub in Storage.ProjectGroups.Where(g => g.ParentGroup == group.Name).ToList())
            {
                sub.ParentGroup = newParent;
                sub.Category = group.Category;
            }

            // Re-parent projects to the parent group (or top-level)
            foreach (var project in Storage.StoredProjects.Where(p => p.Parent == group.Name))
                project.Parent = newParent;

            Storage.ProjectGroups.Remove(group);
            MarkDirty();
        }

        private string EnsureUniqueGroupName(string name, string category, Finn.Model.GroupData? exclude = null)
        {
            var existing = Storage.ProjectGroups
                .Where(g => g.Category == category && g != exclude)
                .Select(g => g.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(name)) return name;

            int i = 2;
            while (existing.Contains($"{name} {i}")) i++;
            return $"{name} {i}";
        }

        public void SetGroups(string group)
        {
            CurrentProject.Parent = group;
            MarkDirty();
        }

        /// <summary>
        /// Items for the New Group dialog: categories (creates top-level group)
        /// and top-level groups (creates subgroup). Shows ▶ for categories, ↳ for groups.
        /// </summary>
        public IReadOnlyList<(string Label, string? GroupName, string Category)> GetGroupParentPickerItems()
        {
            var result = new List<(string, string?, string)>();

            foreach (var cat in new[] { "Archive", "Library", "Project" })
            {
                result.Add(($"▶  {cat}", null, cat));

                foreach (var g in Storage.ProjectGroups
                    .Where(g => g.Category == cat && string.IsNullOrEmpty(g.ParentGroup))
                    .OrderBy(g => g.SortOrder))
                {
                    result.Add(($"    ↳ {g.Name}", g.Name, cat));
                }
            }

            return result;
        }

        /// <summary>
        /// Items for the Edit Project Group ComboBox: groups and subgroups only —
        /// no categories (category is set separately). Structured as group → subgroup.
        /// Includes a "No Group" sentinel at the top.
        /// Only groups belonging to <paramref name="category"/> are included so that
        /// a project cannot be assigned to a group from a different category.
        /// </summary>
        public IReadOnlyList<(string Label, string? GroupName, string Category)> GetProjectGroupPickerItems(string category)
        {
            var result = new List<(string, string?, string)>();
            result.Add(("— No Group —", null, category));

            var topLevel = Storage.ProjectGroups
                .Where(g => g.Category == category && string.IsNullOrEmpty(g.ParentGroup))
                .OrderBy(g => g.SortOrder)
                .ToList();

            foreach (var g in topLevel)
            {
                result.Add((g.Name, g.Name, category));

                foreach (var sub in Storage.ProjectGroups
                    .Where(s => s.ParentGroup == g.Name)
                    .OrderBy(s => s.SortOrder))
                {
                    result.Add(($"  ↳ {sub.Name}", sub.Name, category));
                }
            }

            return result;
        }

        public void SortProjects()
        {
            var sorted = Storage.StoredProjects
                .OrderBy(x => x.Category == "Library" ? 0 : x.Category == "Archive" ? 1 : 2)
                .ThenBy(x => x.Namn)
                .ToList();

            // Sort in-place by moving items to their target positions.
            // Avoids replacing the collection instance, which would disconnect
            // any bindings and watcher subscriptions that hold a reference to it.
            for (int i = 0; i < sorted.Count; i++)
            {
                int currentIndex = Storage.StoredProjects.IndexOf(sorted[i]);
                if (currentIndex != i)
                    Storage.StoredProjects.Move(currentIndex, i);
            }

            SetProjectlist();
        }

        public void SetProject(string name)
        {
            ProjectData project = Storage.StoredProjects.FirstOrDefault(x => x.Namn == name);

            // Clear any active search highlights before switching projects.
            ClearSearchMatchFlags();

            // Use backing fields directly
            // Previously SelectProjectAsync + Type setter each triggered UpdateFilter,
            // doubling the work every time a project was switched.
            currentProject = project;

            if (!CurrentProject.Filetypes.Contains(type))
                type = ALL_TYPES;

            UpdateFilter();

            OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(IsSearchResult));
            OnPropertyChanged(nameof(Type));
        }

        public void SelectProject(string name)
        {
            string currentProjectName = CurrentProject.Namn;
            if (currentProjectName != name)
            {
                SetProject(name);
            }
            SignalColumnsChanged();
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

            // Clear any active search highlights before switching projects.
            if (projectChanged)
                ClearSearchMatchFlags();

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
            {
                OnPropertyChanged(nameof(CurrentProject));
                OnPropertyChanged(nameof(IsSearchResult));
            }
            OnPropertyChanged(nameof(Type));
            SignalColumnsChanged();
        }

        public void ReselectProject()
        {
            SetProject(CurrentProject.Namn);
            SignalColumnsChanged();
        }

        public void SetProjecCategory(string name)
        {
            CurrentProject.Category = name;

            if (name != PROJECT_CATEGORY)
            {
                CurrentProject.Parent = null;
            }

            SortProjects();
            MarkDirty();
        }

        public void SetDefaultSelection()
        {
            string defaultProject = Storage.StoredProjects.FirstOrDefault().Namn;

            // Use backing fields to avoid cascading UpdateFilter calls.
            currentProject = GetProject(defaultProject);
            type = ALL_TYPES;
            UpdateFilter();
            OnPropertyChanged(nameof(CurrentProject));
            OnPropertyChanged(nameof(IsSearchResult));
            OnPropertyChanged(nameof(Type));
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

        public void CreateZipFromProject() => _ = CreateZipFromProjectAsync(new ExportProjectOptions());

        public async Task CreateZipFromProjectAsync(ExportProjectOptions options)
        {
            ProjectData? project = CurrentProject;
            string savePath = SavePath;

            if (project == null || string.IsNullOrWhiteSpace(savePath))
                return;

            var integrityIssues = ValidateProjectIntegrity(project);
            int blockingErrors = integrityIssues.Count(i => i.Severity == IntegrityIssueSeverity.Error);
            if (blockingErrors > 0)
            {
                PreviewVM.StatusMessage = "Export blocked by project integrity issues. Resolve duplicates/ambiguous links first.";
                PreviewVM.BackgroundTaskMessage = "Export blocked";
                return;
            }

            if (integrityIssues.Count > 0)
                PreviewVM.StatusMessage = BuildIntegritySummary(integrityIssues);

            using var exportCts = new CancellationTokenSource();
            PreviewVM.SetBackgroundTaskCts(exportCts);
            PreviewVM.BackgroundTaskActive = true;
            PreviewVM.BackgroundTaskMessage = "Preparing export…";
            PreviewVM.BackgroundTaskProgress = 0;

            try
            {
                await Task.Run(() => ExportProjectZip(options, exportCts.Token, project, savePath), exportCts.Token).ConfigureAwait(true);
                PreviewVM.BackgroundTaskMessage = "Export complete";
                PreviewVM.BackgroundTaskProgress = 100;
            }
            catch (OperationCanceledException)
            {
                PreviewVM.BackgroundTaskMessage = "Export cancelled";
                PreviewVM.BackgroundTaskProgress = 0;
            }
            catch (Exception ex)
            {
                Utils.ErrorLogger.Log(ex, "CreateZipFromProject");
                PreviewVM.BackgroundTaskMessage = "Export failed";
            }
            finally
            {
                PreviewVM.SetBackgroundTaskCts(null);
                PreviewVM.BackgroundTaskActive = false;
            }
        }

        private void ExportProjectZip(ExportProjectOptions options, CancellationToken token, ProjectData project, string savePath)
        {
            string? tempDir = null;
            string? tempZipPath = null;

            try
            {
                token.ThrowIfCancellationRequested();

                tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                string storeDir = Path.Combine(savePath, "Exports");
                string zipPath = GetUniqueExportZipPath(storeDir, project.Namn);
                tempZipPath = Path.Combine(storeDir, $"{Path.GetFileNameWithoutExtension(zipPath)}.{Guid.NewGuid():N}.tmp.zip");

                Directory.CreateDirectory(storeDir);
                Directory.CreateDirectory(tempDir);

                var exportReport = new List<string>
                {
                    $"Project: {project.Namn}",
                    $"Exported: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                    string.Empty,
                    "Notes:"
                };

                var childrenByParent = project.StoredFiles
                    .Where(file => !string.IsNullOrWhiteSpace(file.ParentNamn))
                    .GroupBy(file => file.ParentNamn, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

                var rootFiles = project.StoredFiles.Where(file => !file.IsChild).ToList();
                int totalWork = CountExportWork(rootFiles, childrenByParent, options) + 1;
                var progress = new ExportProgressContext(totalWork, ReportExportProgress, token);

                foreach (var rootFile in rootFiles)
                    ExportFileTree(rootFile, tempDir, childrenByParent, new HashSet<string>(StringComparer.OrdinalIgnoreCase), exportReport, progress, options, token, isRoot: true, branchIsGroup: false);

                token.ThrowIfCancellationRequested();
                File.WriteAllLines(Path.Combine(tempDir, "ExportReport.txt"), exportReport);

                progress.Report("Creating archive…");
                System.IO.Compression.ZipFile.CreateFromDirectory(tempDir, tempZipPath);
                File.Move(tempZipPath, zipPath, overwrite: true);
                tempZipPath = null;
                progress.Advance("Export complete");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempZipPath))
                {
                    try
                    {
                        if (File.Exists(tempZipPath))
                            File.Delete(tempZipPath);
                    }
                    catch (Exception ex)
                    {
                        Utils.ErrorLogger.Log(ex, "CreateZipFromProject.TempZipCleanup");
                    }
                }

                CleanupTempExportDirectory(tempDir);
            }
        }

        private int CountExportWork(IEnumerable<FileData> files, IReadOnlyDictionary<string, List<FileData>> childrenByParent, ExportProjectOptions options)
        {
            int total = 0;

            foreach (var file in files)
                total += CountExportWork(file, childrenByParent, options, isRoot: true, branchIsGroup: false);

            return total;
        }

        private int CountExportWork(FileData file, IReadOnlyDictionary<string, List<FileData>> childrenByParent, ExportProjectOptions options, bool isRoot, bool branchIsGroup)
        {
            if (!ShouldIncludeNode(file, isRoot, branchIsGroup, options))
                return 0;

            int total = 1;

            if (options.IncludeOtherFiles)
                total += file.OtherFiles?.Count ?? 0;

            if (childrenByParent.TryGetValue(file.Namn, out var children))
            {
                foreach (var child in children)
                    total += CountExportWork(child, childrenByParent, options, isRoot: false, branchIsGroup: branchIsGroup || file.IsGroup);
            }

            return total;
        }

        private void ReportExportProgress(string message, int progress)
        {
            Dispatcher.UIThread.Post(() =>
            {
                PreviewVM.BackgroundTaskMessage = message;
                PreviewVM.BackgroundTaskProgress = progress;
            });
        }

        private static void CleanupTempExportDirectory(string? tempDir)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch (Exception ex)
            {
                Utils.ErrorLogger.Log(ex, "CreateZipFromProject.Cleanup");
            }
        }

        private void ExportFileTree(FileData file, string destinationDirectory, IReadOnlyDictionary<string, List<FileData>> childrenByParent, HashSet<string> usedNames, List<string> exportReport, ExportProgressContext progress, ExportProjectOptions options, CancellationToken token, bool isRoot, bool branchIsGroup)
        {
            token.ThrowIfCancellationRequested();

            if (!ShouldIncludeNode(file, isRoot, branchIsGroup, options))
                return;

            string baseName = string.IsNullOrWhiteSpace(file.Namn) ? Path.GetFileNameWithoutExtension(file.Sökväg) : file.Namn;
            string safeName = GetUniquePathSegment(destinationDirectory, SanitizePathSegment(baseName), usedNames);
            bool hasChildren = childrenByParent.TryGetValue(file.Namn, out var children) && children.Count > 0;
            bool hasOtherFiles = file.OtherFiles?.Count > 0;
            bool shouldCreateFolder = file.IsGroup || hasChildren || hasOtherFiles;

            string currentDirectory = destinationDirectory;
            if (shouldCreateFolder)
            {
                currentDirectory = Path.Combine(destinationDirectory, safeName);
                Directory.CreateDirectory(currentDirectory);
            }

            if (File.Exists(file.Sökväg))
            {
                string fileName = BuildStableExportFileName(baseName, file.Sökväg);
                fileName = GetUniquePathSegment(currentDirectory, SanitizePathSegment(fileName), null);
                string destPath = Path.Combine(currentDirectory, fileName);
                File.Copy(file.Sökväg, destPath, overwrite: true);
            }
            else if (!file.IsGroup)
            {
                exportReport.Add($"Missing file skipped: {file.Namn} ({file.Sökväg})");
            }

            if (hasOtherFiles && options.IncludeOtherFiles)
                ExportOtherFiles(file, currentDirectory, exportReport, progress, token);

            progress.Advance($"Exported {file.Namn}");

            if (!hasChildren)
                return;

            var childNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in children)
                ExportFileTree(child, currentDirectory, childrenByParent, childNames, exportReport, progress, options, token, isRoot: false, branchIsGroup: branchIsGroup || file.IsGroup);
        }

        private static bool ShouldIncludeNode(FileData file, bool isRoot, bool branchIsGroup, ExportProjectOptions options)
        {
            if (isRoot)
                return !file.IsGroup || options.IncludeGroups;

            if (file.IsGroup)
                return options.IncludeGroups;

            return branchIsGroup ? options.IncludeGroups : options.IncludeAttachedFiles;
        }

        private static void ExportOtherFiles(FileData file, string destinationDirectory, List<string> exportReport, ExportProgressContext progress, CancellationToken token)
        {
            string otherFilesDirectory = Path.Combine(destinationDirectory, "Attachments");
            Directory.CreateDirectory(otherFilesDirectory);

            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var otherFile in file.OtherFiles)
            {
                if (string.IsNullOrWhiteSpace(otherFile.Filepath) || !File.Exists(otherFile.Filepath))
                {
                    exportReport.Add($"Missing attachment skipped: {file.Namn} -> {otherFile.Name} ({otherFile.Filepath})");
                    continue;
                }

                string fileName = BuildStableExportFileName(otherFile.Name, otherFile.Filepath);
                fileName = GetUniquePathSegment(otherFilesDirectory, SanitizePathSegment(fileName), usedNames);

                string destPath = Path.Combine(otherFilesDirectory, fileName);
                File.Copy(otherFile.Filepath, destPath, overwrite: true);
                progress.Advance($"Exported attachment {otherFile.Name}");
                token.ThrowIfCancellationRequested();
            }
        }

        public sealed record ExportProjectOptions(
            bool IncludeGroups = true,
            bool IncludeAttachedFiles = true,
            bool IncludeOtherFiles = true);

        private sealed class ExportProgressContext
        {
            private readonly Action<string, int> _report;
            private readonly CancellationToken _token;
            private int _processed;

            public ExportProgressContext(int totalWork, Action<string, int> report, CancellationToken token)
            {
                TotalWork = Math.Max(totalWork, 1);
                _report = report;
                _token = token;
            }

            public int TotalWork { get; }

            public void Advance(string message)
            {
                _token.ThrowIfCancellationRequested();
                int processed = Interlocked.Increment(ref _processed);
                _report(message, GetProgress(processed));
            }

            public void Report(string message) => _report(message, GetProgress(_processed));

            private int GetProgress(int processed)
            {
                if (TotalWork <= 0)
                    return 0;

                return Math.Clamp((int)Math.Round(processed * 100.0 / TotalWork), 0, 100);
            }
        }

        private static string BuildStableExportFileName(string preferredName, string sourcePath)
        {
            string extension = Path.GetExtension(sourcePath);
            string baseName = SanitizePathSegment(string.IsNullOrWhiteSpace(preferredName)
                ? Path.GetFileNameWithoutExtension(sourcePath)
                : preferredName);

            if (string.IsNullOrWhiteSpace(extension))
                return baseName;

            return baseName + extension;
        }

        private static string GetUniqueExportZipPath(string exportDirectory, string projectName)
        {
            Directory.CreateDirectory(exportDirectory);

            string safeName = SanitizePathSegment(string.IsNullOrWhiteSpace(projectName) ? "Export" : projectName);
            string candidate = Path.Combine(exportDirectory, safeName + ".zip");
            int suffix = 1;

            while (File.Exists(candidate))
            {
                candidate = Path.Combine(exportDirectory, $"{safeName} ({suffix}).zip");
                suffix++;
            }

            return candidate;
        }

        private static string GetUniquePathSegment(string directory, string desiredName, HashSet<string>? usedNames)
        {
            string candidate = string.IsNullOrWhiteSpace(desiredName) ? "Unnamed" : desiredName;
            string extension = Path.GetExtension(candidate);
            string baseName = string.IsNullOrWhiteSpace(extension)
                ? candidate
                : Path.GetFileNameWithoutExtension(candidate);

            int suffix = 1;
            while (true)
            {
                bool existsOnDisk = Directory.Exists(Path.Combine(directory, candidate)) || File.Exists(Path.Combine(directory, candidate));
                bool existsInMemory = usedNames != null && usedNames.Contains(candidate);
                if (!existsOnDisk && !existsInMemory)
                {
                    usedNames?.Add(candidate);
                    return candidate;
                }

                candidate = string.IsNullOrWhiteSpace(extension)
                    ? $"{baseName} ({suffix})"
                    : $"{baseName} ({suffix}){extension}";
                suffix++;
            }
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unnamed";

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
                value = value.Replace(invalidChar, '_');

            return value.Trim().TrimEnd('.');
        }

        /// <summary>
        /// Validates project relationships before sync/export operations.
        /// Reports duplicate top-level names, unresolved/ambiguous parent links,
        /// and folder attachments with unresolved owners.
        /// </summary>
        public List<IntegrityIssue> ValidateProjectIntegrity(ProjectData project)
        {
            var issues = new List<IntegrityIssue>();
            if (project == null)
                return issues;

            var topLevel = project.StoredFiles.Where(f => !f.IsAppendedFile).ToList();

            var topByName = topLevel
                .GroupBy(f => f.Namn, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (var duplicate in topByName.Where(g => !string.IsNullOrWhiteSpace(g.Key) && g.Value.Count > 1))
            {
                issues.Add(new IntegrityIssue(
                    IntegrityIssueSeverity.Error,
                    "DuplicateTopLevelName",
                    $"Duplicate top-level name: \"{duplicate.Key}\" ({duplicate.Value.Count} entries)",
                    FileName: duplicate.Key));
            }

            foreach (var child in project.StoredFiles.Where(f => f.IsAppendedFile))
            {
                if (string.IsNullOrWhiteSpace(child.ParentNamn))
                {
                    issues.Add(new IntegrityIssue(
                        IntegrityIssueSeverity.Error,
                        "MissingParentName",
                        $"Child \"{child.Namn}\" has no parent name.",
                        FileName: child.Namn));
                    continue;
                }

                if (!topByName.TryGetValue(child.ParentNamn, out var parents) || parents.Count == 0)
                {
                    issues.Add(new IntegrityIssue(
                        IntegrityIssueSeverity.Error,
                        "MissingParent",
                        $"Child \"{child.Namn}\" points to missing parent \"{child.ParentNamn}\".",
                        FileName: child.Namn));
                    continue;
                }

                if (parents.Count > 1)
                {
                    issues.Add(new IntegrityIssue(
                        IntegrityIssueSeverity.Error,
                        "AmbiguousParent",
                        $"Child \"{child.Namn}\" has ambiguous parent \"{child.ParentNamn}\".",
                        FileName: child.Namn));
                }
            }

            foreach (var folder in project.Folders.Where(f => !f.IsProjectLevel))
            {
                int pathMatches = 0;
                if (!string.IsNullOrWhiteSpace(folder.AttachToFilePath))
                {
                    string targetPath = NormalizePathForComparison(folder.AttachToFilePath);
                    pathMatches = topLevel.Count(f =>
                        string.Equals(NormalizePathForComparison(f.Sökväg), targetPath, GetPathComparison()));
                }

                if (pathMatches > 1)
                {
                    issues.Add(new IntegrityIssue(
                        IntegrityIssueSeverity.Error,
                        "AmbiguousFolderOwnerPath",
                        $"Folder \"{folder.Name}\" path owner is ambiguous.",
                        FolderPath: folder.Path));
                    continue;
                }

                if (pathMatches == 1)
                    continue;

                if (string.IsNullOrWhiteSpace(folder.AttachToFile))
                {
                    issues.Add(new IntegrityIssue(
                        IntegrityIssueSeverity.Warning,
                        "MissingFolderOwner",
                        $"Folder \"{folder.Name}\" has no attached-file owner.",
                        FolderPath: folder.Path));
                    continue;
                }

                int nameMatches = topLevel.Count(f => string.Equals(f.Namn, folder.AttachToFile, StringComparison.OrdinalIgnoreCase));
                if (nameMatches == 0)
                {
                    issues.Add(new IntegrityIssue(
                        IntegrityIssueSeverity.Warning,
                        "FolderOwnerNotFound",
                        $"Folder \"{folder.Name}\" owner \"{folder.AttachToFile}\" not found.",
                        FolderPath: folder.Path));
                }
                else if (nameMatches > 1)
                {
                    issues.Add(new IntegrityIssue(
                        IntegrityIssueSeverity.Error,
                        "AmbiguousFolderOwner",
                        $"Folder \"{folder.Name}\" owner \"{folder.AttachToFile}\" is ambiguous.",
                        FolderPath: folder.Path));
                }
            }

            return issues;
        }

        public List<IntegrityIssue> ValidateCurrentProjectIntegrity() =>
            CurrentProject == null ? [] : ValidateProjectIntegrity(CurrentProject);

        public static string BuildIntegritySummary(IReadOnlyList<IntegrityIssue> issues)
        {
            if (issues == null || issues.Count == 0)
                return "No project integrity issues found.";

            int errors = issues.Count(i => i.Severity == IntegrityIssueSeverity.Error);
            int warnings = issues.Count - errors;
            string severity = errors > 0
                ? $"Integrity check: {errors} error(s), {warnings} warning(s)."
                : $"Integrity check: {warnings} warning(s).";

            var sample = issues.Take(3).Select(i => i.Message);
            string sampleText = string.Join(" | ", sample);
            return string.IsNullOrWhiteSpace(sampleText)
                ? severity
                : severity + " " + sampleText;
        }

        public static string BuildIntegrityReportMessage(string? projectName, IReadOnlyList<IntegrityIssue> issues, int maxDetails = 20)
        {
            string header = string.IsNullOrWhiteSpace(projectName)
                ? "Integrity check"
                : $"Integrity check for \"{projectName}\"";

            if (issues == null || issues.Count == 0)
                return header + "\n\nNo issues found.";

            int errors = issues.Count(i => i.Severity == IntegrityIssueSeverity.Error);
            int warnings = issues.Count - errors;
            var lines = new List<string>
            {
                header,
                string.Empty,
                $"Errors: {errors}",
                $"Warnings: {warnings}",
                string.Empty
            };

            foreach (var issue in issues.Take(maxDetails))
            {
                string marker = issue.Severity == IntegrityIssueSeverity.Error ? "[Error]" : "[Warning]";
                lines.Add($"{marker} {issue.Message}");
            }

            if (issues.Count > maxDetails)
                lines.Add($"... and {issues.Count - maxDetails} more issue(s)");

            return string.Join(Environment.NewLine, lines);
        }


        #region Shared Projects

        /// <summary>
        /// Links the current project to a shared server location.
        /// Does NOT push immediately — the caller should show the push dialog first.
        /// </summary>
        public void MakeProjectShared(string serverFolder)
        {
            if (CurrentProject == null || string.IsNullOrWhiteSpace(serverFolder))
                return;

            string fileName = SanitizeFileName(CurrentProject.Namn) + ".json";
            string serverPath = Path.Combine(serverFolder, fileName);

            CurrentProject.SharedPath = serverPath;
            CurrentProject.SharedSyncStatus = SharedSyncState.LocalAhead;
            MarkDirty();
        }

        /// <summary>
        /// Builds a list of differences between the local and server copies
        /// of a shared project for display in the pull-diff dialog.
        /// </summary>
        public List<SharedDiffEntry> BuildPullDiff(ProjectData local, ProjectData server)
        {
            var entries = new List<SharedDiffEntry>();

            // --- Files ---
            var localNames = new HashSet<string>(
                local.StoredFiles.Select(f => f.Namn), StringComparer.OrdinalIgnoreCase);
            var serverNames = new HashSet<string>(
                server.StoredFiles.Select(f => f.Namn), StringComparer.OrdinalIgnoreCase);

            foreach (string name in serverNames.Except(localNames, StringComparer.OrdinalIgnoreCase))
                entries.Add(new SharedDiffEntry { Change = "Server only", Category = "File", Detail = name });
            foreach (string name in localNames.Except(serverNames, StringComparer.OrdinalIgnoreCase))
                entries.Add(new SharedDiffEntry { Change = "Local only", Category = "File", Detail = name });

            // Files present in both — check for metadata and content changes
            foreach (string name in localNames.Intersect(serverNames, StringComparer.OrdinalIgnoreCase))
            {
                var lf = local.StoredFiles.FirstOrDefault(f => f.Namn.Equals(name, StringComparison.OrdinalIgnoreCase));
                var sf = server.StoredFiles.FirstOrDefault(f => f.Namn.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (lf == null || sf == null) continue;

                var changes = new List<string>();
                if (lf.Sökväg != sf.Sökväg) changes.Add("path");
                if (lf.Filtyp != sf.Filtyp) changes.Add("type");
                if (lf.CurrentVersion != sf.CurrentVersion) changes.Add("active version");
                if (lf.Tagg != sf.Tagg) changes.Add("tag");
                if (lf.Färg != sf.Färg) changes.Add("color");
                if (lf.Handling != sf.Handling) changes.Add("handling");
                if (lf.Status != sf.Status) changes.Add("status");
                if (lf.Datum != sf.Datum) changes.Add("date");
                if (lf.Ritningstyp != sf.Ritningstyp) changes.Add("drawing type");
                if (lf.Beskrivning1 != sf.Beskrivning1) changes.Add("description 1");
                if (lf.Beskrivning2 != sf.Beskrivning2) changes.Add("description 2");
                if (lf.Beskrivning3 != sf.Beskrivning3) changes.Add("description 3");
                if (lf.Beskrivning4 != sf.Beskrivning4) changes.Add("description 4");
                if (lf.Revidering != sf.Revidering) changes.Add("revision");
                if (lf.DefaultPage != sf.DefaultPage) changes.Add("default page");

                if (changes.Count > 0)
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "File", Detail = $"{name} ({string.Join(", ", changes)})", FileName = name });

                if (lf.Note != sf.Note)
                {
                    string hint = string.IsNullOrEmpty(sf.Note) ? "server note is empty"
                        : string.IsNullOrEmpty(lf.Note) ? "server has note, local is empty"
                        : "notes differ";
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Note", Detail = $"{name} ({hint})", FileName = name });
                }

                int localVer = lf.Versions.Count;
                int serverVer = sf.Versions.Count;
                if (localVer != serverVer)
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Versions", Detail = $"{name}: {localVer} → {serverVer}", FileName = name });

                int localAnn = lf.AnnotationLayers.Sum(l => l.TotalCount);
                int serverAnn = sf.AnnotationLayers.Sum(l => l.TotalCount);
                bool annCountDiffers = localAnn != serverAnn;
                bool annContentDiffers = !annCountDiffers && AnnotationContentDiffers(lf, sf);
                if (annCountDiffers || annContentDiffers)
                {
                    string detail = annContentDiffers
                        ? $"{name}: {localAnn} annotations (content changed)"
                        : $"{name}: {localAnn} → {serverAnn}";
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Annotations", Detail = detail, FileName = name });
                }

                int localBm = lf.FavPages?.Count ?? 0;
                int serverBm = sf.FavPages?.Count ?? 0;
                if (localBm != serverBm)
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Bookmarks", Detail = $"{name}: {localBm} → {serverBm}", FileName = name });

                int localOther = lf.OtherFiles?.Count ?? 0;
                int serverOther = sf.OtherFiles?.Count ?? 0;
                if (localOther != serverOther)
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Other Files", Detail = $"{name}: {localOther} → {serverOther}", FileName = name });
            }

            // --- Folders ---
            int localFolders = local.Folders?.Count ?? 0;
            int serverFolders = server.Folders?.Count ?? 0;
            if (localFolders != serverFolders)
                entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Folders", Detail = $"{localFolders} → {serverFolders}" });

            // --- Todo ---
            int localTodo = local.TodoItems?.Count ?? 0;
            int serverTodo = server.TodoItems?.Count ?? 0;
            if (localTodo != serverTodo)
                entries.Add(new SharedDiffEntry { Change = "Modified", Category = "To-Do", Detail = $"{localTodo} → {serverTodo} items" });
            else if (localTodo > 0 && serverTodo > 0)
            {
                // Same count but different content
                var localTexts = string.Join("|", local.TodoItems.Select(t => t.Text + t.IsDone));
                var serverTexts = string.Join("|", server.TodoItems.Select(t => t.Text + t.IsDone));
                if (localTexts != serverTexts)
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "To-Do", Detail = $"{localTodo} items (content changed)" });
            }

            // --- Category / Group ---
            if (local.Category != server.Category)
                entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Settings", Detail = $"Category: {local.Category} → {server.Category}" });
            if (local.Parent != server.Parent)
                entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Settings", Detail = $"Group: {local.Parent ?? "(none)"} → {server.Parent ?? "(none)"}" });

            // --- Column settings ---
            var colChanges = new List<string>();
            for (int i = 0; i < 17; i++)
            {
                bool localVal = local.GetMetaValue(i);
                bool serverVal = server.GetMetaValue(i);
                if (localVal != serverVal)
                    colChanges.Add($"Meta_{i + 1}");
            }
            if (colChanges.Count > 0)
                entries.Add(new SharedDiffEntry { Change = "Modified", Category = "Settings", Detail = $"Column visibility: {string.Join(", ", colChanges)}" });

            return entries;
        }

        /// <summary>
        /// Builds a summary string for the pull-diff dialog.
        /// </summary>
        public static string BuildPullSummary(List<SharedDiffEntry> entries)
        {
            int serverOnly = entries.Count(e => e.Change == "Server only");
            int localOnly = entries.Count(e => e.Change == "Local only");
            int modified = entries.Count(e => e.Change == "Modified");

            var parts = new List<string>();
            if (serverOnly > 0) parts.Add($"{serverOnly} server-only");
            if (localOnly > 0) parts.Add($"{localOnly} local-only");
            if (modified > 0) parts.Add($"{modified} modified");

            return parts.Count > 0 ? string.Join(", ", parts) : "No differences found";
        }

        /// <summary>
        /// Deserializes the server copy for diff comparison without applying it.
        /// Returns null if the file can't be read or parsed.
        /// </summary>
        public static ProjectData? ReadServerProject(string sharedPath)
        {
            if (!File.Exists(sharedPath)) return null;
            try
            {
                string json = File.ReadAllText(sharedPath);
                var project = Utils.JsonHelper.Deserialize<ProjectData>(json);
                if (project == null) return null;

                // Run migration so counts and relationships are accurate
                project.FlattenAppendedFiles();
                project.WireParentReferences();
                project.RefreshHasChildren();
                foreach (var layer in project.StoredFiles.SelectMany(f => f.AnnotationLayers))
                    layer.RecalculateCounts();
                foreach (var file in project.StoredFiles)
                    file.RefreshAnnotationStatus();
                return project;
            }
            catch (Exception ex)
            {
                Utils.ErrorLogger.Log(ex, $"ReadServerProject({sharedPath})");
                return null;
            }
        }

        /// <summary>
        /// Merges server changes into the local project using server-wins semantics.
        /// Entries in <paramref name="keepLocal"/> preserve the local value instead.
        /// <list type="bullet">
        ///   <item>Files only on server → added to local project</item>
        ///   <item>Files only locally → kept (not removed)</item>
        ///   <item>Files on both sides → server wins per field (unless KeepLocal checked)</item>
        ///   <item>Todos → server replaces local (local-only todos kept)</item>
        ///   <item>Folders → server replaces local (local-only folders kept)</item>
        ///   <item>Settings (Category/Group) → server wins</item>
        /// </list>
        /// </summary>
        public bool MergeProject(ProjectData server, HashSet<(string File, string Category)>? keepLocal = null)
        {
            if (CurrentProject?.SharedPath == null) return false;

            try
            {
                BackupProjectBeforePull(CurrentProject);

                int addedFiles = 0;
                int mergedFiles = 0;
                int addedTodos = 0;
                int addedFolders = 0;

                var localFileMap = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in CurrentProject.StoredFiles)
                    localFileMap.TryAdd(f.Namn, f);

                foreach (var serverFile in server.StoredFiles)
                {
                    if (!localFileMap.TryGetValue(serverFile.Namn, out var localFile))
                    {
                        serverFile.Uppdrag = CurrentProject.Namn;
                        foreach (var layer in serverFile.AnnotationLayers)
                            layer.RecalculateCounts();
                        serverFile.RefreshAnnotationStatus();
                        CurrentProject.StoredFiles.Add(serverFile);
                        addedFiles++;
                    }
                    else
                    {
                        bool changed = false;
                        string name = localFile.Namn;
                        bool isViewer = CurrentProject.IsViewer;

                        // File metadata — server wins unless KeepLocal is checked
                        changed |= SharedProjectMerge.MergeFileMetadata(localFile, serverFile,
                            SharedProjectMerge.ShouldKeepLocal(keepLocal, name, "File"));

                        changed |= SharedProjectMerge.MergeNote(localFile, serverFile,
                            SharedProjectMerge.ShouldKeepLocal(keepLocal, name, "Note"));

                        // Viewers: preserve local annotation layers, add server layers alongside
                        if (isViewer)
                            changed |= SharedProjectMerge.MergeAnnotationsForViewer(localFile, serverFile);
                        else
                            changed |= SharedProjectMerge.MergeAnnotations(localFile, serverFile,
                                SharedProjectMerge.ShouldKeepLocal(keepLocal, name, "Annotations"));

                        changed |= SharedProjectMerge.MergeBookmarks(localFile, serverFile,
                            SharedProjectMerge.ShouldKeepLocal(keepLocal, name, "Bookmarks"));

                        changed |= SharedProjectMerge.MergeOtherFiles(localFile, serverFile,
                            SharedProjectMerge.ShouldKeepLocal(keepLocal, name, "Other Files"));

                        changed |= SharedProjectMerge.MergeVersions(localFile, serverFile,
                            SharedProjectMerge.ShouldKeepLocal(keepLocal, name, "Versions"));

                        if (changed) mergedFiles++;
                    }
                }

                // Viewers mirror the server — remove local files the owner deleted.
                // Owners keep local-only files so they can push them later.
                int removedFiles = 0;
                if (CurrentProject.IsViewer)
                {
                    var serverNameSet = new HashSet<string>(
                        server.StoredFiles.Select(f => f.Namn), StringComparer.OrdinalIgnoreCase);

                    for (int i = CurrentProject.StoredFiles.Count - 1; i >= 0; i--)
                    {
                        if (!serverNameSet.Contains(CurrentProject.StoredFiles[i].Namn))
                        {
                            CurrentProject.StoredFiles.RemoveAt(i);
                            removedFiles++;
                        }
                    }
                }

                // Merge todos — server wins, local-only kept
                if (server.TodoItems is { Count: > 0 })
                {
                    var serverTexts = new HashSet<string>(
                        server.TodoItems.Select(t => t.Text ?? ""),
                        StringComparer.OrdinalIgnoreCase);

                    // Replace matching todos with server versions
                    CurrentProject.TodoItems ??= new();
                    for (int i = CurrentProject.TodoItems.Count - 1; i >= 0; i--)
                    {
                        if (serverTexts.Contains(CurrentProject.TodoItems[i].Text ?? ""))
                            CurrentProject.TodoItems.RemoveAt(i);
                    }

                    // Add all server todos (they replace what we just removed + add new ones)
                    foreach (var serverTodo in server.TodoItems)
                    {
                        CurrentProject.TodoItems.Add(serverTodo);
                        addedTodos++;
                    }
                }

                // Merge folders — server wins, local-only kept
                if (server.Folders is { Count: > 0 })
                {
                    var serverFolderPaths = new HashSet<string>(
                        server.Folders.Select(f => f.Path ?? ""),
                        StringComparer.OrdinalIgnoreCase);

                    CurrentProject.Folders ??= new();
                    for (int i = CurrentProject.Folders.Count - 1; i >= 0; i--)
                    {
                        if (serverFolderPaths.Contains(CurrentProject.Folders[i].Path ?? ""))
                            CurrentProject.Folders.RemoveAt(i);
                    }

                    foreach (var serverFolder in server.Folders)
                    {
                        CurrentProject.Folders.Add(serverFolder);
                        addedFolders++;
                    }
                }

                // Settings — server wins
                if (server.Category != null)
                    CurrentProject.Category = server.Category;
                if (server.Parent != CurrentProject.Parent)
                    CurrentProject.Parent = server.Parent;

                // Column visibility — server wins
                for (int i = 0; i < 17; i++)
                {
                    bool serverVal = server.GetMetaValue(i);
                    if (CurrentProject.GetMetaValue(i) != serverVal)
                        CurrentProject.SetMetaValue(i, serverVal);
                }

                // Refresh project state
                CurrentProject.SetFiletypeList();
                CurrentProject.WireParentReferences();
                CurrentProject.RefreshHasChildren();
                foreach (var file in CurrentProject.StoredFiles)
                    file.RefreshAnnotationStatus();
                UpdateFilter();
                MarkDirty();

                var parts = new List<string>();
                if (addedFiles > 0) parts.Add($"{addedFiles} new files");
                if (removedFiles > 0) parts.Add($"{removedFiles} files removed");
                if (mergedFiles > 0) parts.Add($"{mergedFiles} files enriched");
                if (addedTodos > 0) parts.Add($"{addedTodos} todos");
                if (addedFolders > 0) parts.Add($"{addedFolders} folders");
                string detail = parts.Count > 0 ? string.Join(", ", parts) : "no new content";

                // Record the server file's timestamp as our baseline so
                // CheckPushConflict won't flag our own merge as a conflict.
                try { CurrentProject.LastPushedUtc = File.GetLastWriteTimeUtc(CurrentProject.SharedPath!); } catch { }
                CurrentProject.LastPulledUtc = DateTime.UtcNow;

                // If any rows were kept local, local ≠ server → LocalAhead.
                // Viewers can't push, so they stay InSync regardless.
                CurrentProject.SharedSyncStatus = keepLocal is { Count: > 0 } && !CurrentProject.IsViewer
                    ? SharedSyncState.LocalAhead
                    : SharedSyncState.InSync;
                WriteSharedActivityLog(CurrentProject, $"merged ({detail})");
                BuildTreeData();
                SignalColumnsChanged();

                PreviewVM.StatusMessage = $"Merged \"{CurrentProject.Namn}\": {detail}";
                return true;
            }
            catch (Exception ex)
            {
                PreviewVM.StatusMessage = $"Merge failed: {ex.Message}";
                Utils.ErrorLogger.Log(ex, "MergeProject");
                return false;
            }
        }

        /// <summary>
        /// Serializes the current project to its <see cref="ProjectData.SharedPath"/>
        /// after applying the push filter from the dialog.
        /// </summary>
        public bool PushProjectFiltered(Dialogs.xSharedPushDia options)
        {
            if (CurrentProject?.SharedPath == null) return false;
            if (CurrentProject.IsViewer)
            {
                PreviewVM.StatusMessage = "Viewers cannot push to a one-way shared project.";
                return false;
            }

            // Persist the one-way sharing flag on the project
            CurrentProject.OneWayShare = options.OneWayShare;

            try
            {
                // Build a filtered copy for serialization
                string json = SerializeForPush(CurrentProject);

                string dir = Path.GetDirectoryName(CurrentProject.SharedPath)!;
                Directory.CreateDirectory(dir);

                string tmpPath = CurrentProject.SharedPath + ".tmp";

                // Suppress watcher so our own write doesn't trigger a sync event
                _suppressSharedWatcher = true;
                try
                {
                    File.WriteAllText(tmpPath, json);
                    File.Move(tmpPath, CurrentProject.SharedPath, overwrite: true);
                }
                catch
                {
                    // Clean up the temp file if the move failed
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                    throw;
                }
                finally
                {
                    // Delay clearing the flag so the debounced watcher event
                    // fires while the flag is still set.
                    Task.Delay(1500).ContinueWith(_ => _suppressSharedWatcher = false);
                }

                CurrentProject.LastPushedUtc = DateTime.UtcNow;
                WriteSharedActivityLog(CurrentProject, "pushed");
                MarkDirty();
                // Set InSync after MarkDirty so it doesn't flip to LocalAhead
                CurrentProject.SharedSyncStatus = SharedSyncState.InSync;
                BuildTreeData();
                PreviewVM.StatusMessage = $"Pushed \"{CurrentProject.Namn}\" to server";
                return true;
            }
            catch (Exception ex)
            {
                PreviewVM.StatusMessage = $"Push failed: {ex.Message}";
                Utils.ErrorLogger.Log(ex, "PushProjectFiltered");
                return false;
            }
        }

        /// <summary>
        /// Checks whether the server file has been modified since the last push.
        /// Returns a warning message, or null if no conflict.
        /// </summary>
        public string? CheckPushConflict()
        {
            if (CurrentProject?.SharedPath == null)
                return null;

            try
            {
                if (!File.Exists(CurrentProject.SharedPath))
                    return null;

                var serverModified = File.GetLastWriteTimeUtc(CurrentProject.SharedPath);

                // First push after import: warn if server file already exists
                if (CurrentProject.LastPushedUtc == null)
                {
                    return "⚠ Server file already exists. Pushing will overwrite it.";
                }

                if (serverModified > CurrentProject.LastPushedUtc.Value.AddSeconds(5))
                {
                    var ago = DateTime.UtcNow - serverModified;
                    string timeDesc = ago.TotalMinutes < 60
                        ? $"{(int)ago.TotalMinutes} min ago"
                        : ago.TotalHours < 24
                            ? $"{(int)ago.TotalHours}h ago"
                            : $"{(int)ago.TotalDays}d ago";

                    return $"⚠ Server file was modified {timeDesc} by another user. Pushing will overwrite their changes.";
                }
            }
            catch { /* ignore — push will succeed or fail on its own */ }

            return null;
        }

        /// <summary>
        /// Serializes the project for the server, stripping local-only properties.
        /// Uses JSON round-trip: serialize full → parse → prune → re-serialize.
        /// </summary>
        private static string SerializeForPush(ProjectData project)
        {
            string fullJson = Utils.JsonHelper.Serialize(project);

            using var doc = JsonDocument.Parse(fullJson);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                WriteFiltered(writer, doc.RootElement);
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        /// <summary>Local-only properties that must not appear in the server file.</summary>
        private static readonly HashSet<string> LocalOnlyProperties = ["SharedPath", "LastPushedUtc", "LastPulledUtc", "SharedRole"];

        private static void WriteFiltered(Utf8JsonWriter writer, JsonElement element, int depth = 0)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var prop in element.EnumerateObject())
                    {
                        if (depth == 0 && LocalOnlyProperties.Contains(prop.Name)) continue;

                        writer.WritePropertyName(prop.Name);
                        WriteFiltered(writer, prop.Value, depth + 1);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteFiltered(writer, item, depth + 1);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
        }

        /// <summary>
        /// Imports a shared project file from a server path into the local
        /// project list. Appends "-shared" to distinguish it from the
        /// original owner's copy. The suffix is preserved across merges
        /// because <see cref="MergeProject"/> keeps the local name.
        /// </summary>
        public bool ImportSharedProject(string serverFilePath)
        {
            try
            {
                if (!File.Exists(serverFilePath))
                {
                    PreviewVM.StatusMessage = "Import failed: file not found";
                    return false;
                }

                string json = File.ReadAllText(serverFilePath);
                var imported = Utils.JsonHelper.Deserialize<ProjectData>(json);
                if (imported == null || string.IsNullOrWhiteSpace(imported.Namn))
                {
                    PreviewVM.StatusMessage = "Import failed: file is not a valid Finn project";
                    return false;
                }

                // Append "-shared" so importing users can distinguish
                // their copy from the original owner's project
                if (!imported.Namn.EndsWith("-shared", StringComparison.OrdinalIgnoreCase))
                    imported.Namn += "-shared";

                // Ensure uniqueness if already imported
                string baseName = imported.Namn;
                int counter = 2;
                while (Storage.StoredProjects.Any(p => p.Namn == imported.Namn))
                {
                    imported.Namn = $"{baseName} ({counter++})";
                }

                // Update file Uppdrag to match the local project name
                foreach (var file in imported.StoredFiles)
                    file.Uppdrag = imported.Namn;

                // Link to the server path
                imported.SharedPath = serverFilePath;
                imported.LastPushedUtc = null;

                // Determine role: one-way shares make the importer a viewer
                imported.SharedRole = imported.OneWayShare
                    ? SharedRole.Viewer
                    : SharedRole.Owner;

                // Mark as in-sync since we just imported the server content.
                // Record the server file timestamp so CheckPushConflict has a baseline.
                try { imported.LastPushedUtc = File.GetLastWriteTimeUtc(serverFilePath); } catch { }
                imported.SharedSyncStatus = SharedSyncState.InSync;

                // Run migration
                imported.FlattenAppendedFiles();
                imported.WireParentReferences();
                imported.RefreshHasChildren();
                foreach (var layer in imported.StoredFiles.SelectMany(f => f.AnnotationLayers))
                    layer.RecalculateCounts();
                foreach (var file in imported.StoredFiles)
                    file.RefreshAnnotationStatus();

                Storage.StoredProjects.Add(imported);
                SetProjectlist();
                SortProjects();
                SetProject(imported.Namn);
                BuildTreeData();
                MarkDirty();

                string role = imported.IsViewer ? "viewer" : "co-owner";
                WriteSharedActivityLog(imported, $"imported as {role}");
                PreviewVM.StatusMessage = $"Imported \"{imported.Namn}\"";
                return true;
            }
            catch (Exception ex)
            {
                PreviewVM.StatusMessage = $"Import failed: {ex.Message}";
                Utils.ErrorLogger.Log(ex, "ImportSharedProject");
                return false;
            }
        }

        /// <summary>
        /// Removes the shared link from the current project, making it
        /// a normal local-only project again.
        /// </summary>
        public void UnshareProject()
        {
            if (CurrentProject == null) return;
            CurrentProject.SharedPath = null;
            CurrentProject.LastPushedUtc = null;
            CurrentProject.LastPulledUtc = null;
            CurrentProject.SharedSyncStatus = SharedSyncState.Unknown;
            CurrentProject.SharedRole = SharedRole.Owner;
            CurrentProject.OneWayShare = false;
            MarkDirty();
            BuildTreeData();
            PreviewVM.StatusMessage = $"\"{CurrentProject.Namn}\" is now local-only";
        }

        /// <summary>
        /// Returns the backup directory path for shared project backups.
        /// </summary>
        public string GetBackupDirectory() => Path.Combine(SavePath, "shared-backup");

        /// <summary>
        /// Restores a shared project from a backup file.
        /// Works like <see cref="MergeProject"/> but reads from a local backup instead of the server.
        /// </summary>
        public bool RestoreFromBackup(string backupPath)
        {
            if (CurrentProject == null) return false;

            try
            {
                string json = File.ReadAllText(backupPath);
                var restored = Utils.JsonHelper.Deserialize<ProjectData>(json);
                if (restored == null)
                {
                    PreviewVM.StatusMessage = "Restore failed: could not parse backup";
                    return false;
                }

                restored.FlattenAppendedFiles();
                restored.WireParentReferences();
                restored.RefreshHasChildren();
                foreach (var layer in restored.StoredFiles.SelectMany(f => f.AnnotationLayers))
                    layer.RecalculateCounts();
                foreach (var file in restored.StoredFiles)
                    file.RefreshAnnotationStatus();

                // Preserve current identity
                restored.SharedPath = CurrentProject.SharedPath;
                restored.LastPushedUtc = CurrentProject.LastPushedUtc;
                restored.Namn = CurrentProject.Namn;
                restored.SharedSyncStatus = CurrentProject.SharedSyncStatus;
                restored.SharedRole = CurrentProject.SharedRole;
                restored.OneWayShare = CurrentProject.OneWayShare;

                foreach (var file in restored.StoredFiles)
                    file.Uppdrag = CurrentProject.Namn;

                int index = Storage.StoredProjects.IndexOf(CurrentProject);
                if (index >= 0)
                    Storage.StoredProjects[index] = restored;
                else
                    Storage.StoredProjects.Add(restored);

                currentProject = restored;
                OnPropertyChanged(nameof(CurrentProject));
                UpdateFilter();
                SetProjectlist();
                BuildTreeData();
                MarkDirty();

                PreviewVM.StatusMessage = $"Restored \"{restored.Namn}\" from backup";
                return true;
            }
            catch (Exception ex)
            {
                PreviewVM.StatusMessage = $"Restore failed: {ex.Message}";
                Utils.ErrorLogger.Log(ex, "RestoreFromBackup");
                return false;
            }
        }

        /// <summary>
        /// Saves a backup of the project before a pull overwrites it.
        /// </summary>
        private void BackupProjectBeforePull(ProjectData project)
        {
            try
            {
                string backupDir = Path.Combine(SavePath, "shared-backup");
                Directory.CreateDirectory(backupDir);
                string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string prefix = SanitizeFileName(project.Namn);
                string backupPath = Path.Combine(backupDir, $"{prefix}-backup-{timestamp}.json");
                string json = Utils.JsonHelper.Serialize(project);
                File.WriteAllText(backupPath, json);

                // Prune old backups: keep last 10 per project
                var oldBackups = Directory.GetFiles(backupDir, $"{prefix}-backup-*.json")
                    .OrderByDescending(File.GetCreationTimeUtc)
                    .Skip(10);
                foreach (var old in oldBackups)
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch (Exception ex)
            {
                Utils.ErrorLogger.Log(ex, "BackupProjectBeforePull");
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>
        /// Appends a line to the activity log next to the server JSON file.
        /// Format: "2025-06-09 14:32 | DESKTOP-ABC\User | pushed (12 files)"
        /// </summary>
        private static void WriteSharedActivityLog(ProjectData project, string action)
        {
            if (string.IsNullOrEmpty(project.SharedPath)) return;
            try
            {
                string? dir = Path.GetDirectoryName(project.SharedPath);
                if (dir == null) return;
                string logPath = Path.Combine(dir,
                    Path.GetFileNameWithoutExtension(project.SharedPath) + ".log");
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm} | {Environment.MachineName}\\{Environment.UserName} | {action}";
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch { }
        }

        /// <summary>
        /// Compares annotation content beyond simple total-count equality.
        /// Checks layer names, page keys, and per-page item counts.
        /// </summary>
        private static bool AnnotationContentDiffers(FileData local, FileData server)
        {
            if (local.AnnotationLayers.Count != server.AnnotationLayers.Count) return true;

            var localMap = new Dictionary<string, AnnotationLayer>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in local.AnnotationLayers)
                localMap.TryAdd(l.Name, l);

            foreach (var sl in server.AnnotationLayers)
            {
                if (!localMap.TryGetValue(sl.Name, out var ll)) return true;
                if (PageDictDiffers(ll.PageStrokes, sl.PageStrokes)) return true;
                if (PageDictDiffers(ll.PageShapes, sl.PageShapes)) return true;
                if (PageDictDiffers(ll.PageTexts, sl.PageTexts)) return true;
                if (PageDictDiffers(ll.PageMeasurements, sl.PageMeasurements)) return true;
            }
            return false;
        }

        private static bool PageDictDiffers<T>(Dictionary<int, List<T>> a, Dictionary<int, List<T>> b)
        {
            if (a.Count != b.Count) return true;
            foreach (var (page, items) in a)
            {
                if (!b.TryGetValue(page, out var other)) return true;
                if (items.Count != other.Count) return true;
            }
            return false;
        }

        #endregion
    }
}
