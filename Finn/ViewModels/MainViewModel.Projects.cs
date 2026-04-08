using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

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
            MarkDirty();
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
            Groups.Clear();

            List<string> list = Storage.StoredProjects.Select(x => x.Parent).Where(x => x != null).Distinct().ToList();
            list.Remove("");

            Groups = new ObservableCollection<string>(list);
        }

        public void SetGroups(string group)
        {
            CurrentProject.Parent = group;
            MarkDirty();
        }

        public void SortProjects()
        {
            var sorted = Storage.StoredProjects
                .OrderBy(x => x.Category == "Library" ? 0 : x.Category == "Archive" ? 1 : 2)
                .ThenBy(x => x.Namn)
                .ToList();

            // Replace the entire collection in one shot instead of
            // Clear + N individual Add calls (each firing CollectionChanged).
            Storage.StoredProjects = new ObservableCollection<ProjectData>(sorted);

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
                entries.Add(new SharedDiffEntry { Change = "Added", Category = "File", Detail = name });
            foreach (string name in localNames.Except(serverNames, StringComparer.OrdinalIgnoreCase))
                entries.Add(new SharedDiffEntry { Change = "Removed", Category = "File", Detail = name });

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

                if (changes.Count > 0)
                    entries.Add(new SharedDiffEntry { Change = "Modified", Category = "File", Detail = $"{name} ({string.Join(", ", changes)})", FileName = name });

                if (lf.Note != sf.Note)
                {
                    string hint = string.IsNullOrEmpty(lf.Note) ? "merge: will add" : "accept incoming to overwrite";
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

            return entries;
        }

        /// <summary>
        /// Builds a summary string for the pull-diff dialog.
        /// </summary>
        public static string BuildPullSummary(List<SharedDiffEntry> entries)
        {
            int added = entries.Count(e => e.Change == "Added");
            int removed = entries.Count(e => e.Change == "Removed");
            int modified = entries.Count(e => e.Change == "Modified");

            var parts = new List<string>();
            if (added > 0) parts.Add($"{added} added");
            if (removed > 0) parts.Add($"{removed} removed");
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

                // Run migration so counts are accurate
                project.FlattenAppendedFiles();
                foreach (var layer in project.StoredFiles.SelectMany(f => f.AnnotationLayers))
                    layer.RecalculateCounts();
                return project;
            }
            catch { return null; }
        }

        /// <summary>
        /// Replaces the local project data with the server copy.
        /// Preserves local <see cref="ProjectData.SharedPath"/>,
        /// <see cref="ProjectData.LastPushedUtc"/>, and project name.
        /// When <paramref name="keepLocal"/> is provided, matching (file, category)
        /// pairs are restored from the local copy after the replace — allowing
        /// selective keep-local during an otherwise full replacement.
        /// A backup of the pre-pull state is saved to the shared-backup folder.
        /// </summary>
        public bool PullProject(ProjectData pulled, HashSet<(string File, string Category)>? keepLocal = null)
        {
            if (CurrentProject?.SharedPath == null) return false;

            try
            {
                // Backup current local state before overwriting
                BackupProjectBeforePull(CurrentProject);

                // Snapshot local files for selective keep-local
                Dictionary<string, FileData>? localFileMap = null;
                if (keepLocal is { Count: > 0 })
                {
                    localFileMap = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in CurrentProject.StoredFiles)
                        localFileMap.TryAdd(f.Namn, f);
                }

                // Preserve local identity
                string localSharedPath = CurrentProject.SharedPath;
                DateTime? localLastPushed = CurrentProject.LastPushedUtc;
                string localName = CurrentProject.Namn;
                int index = Storage.StoredProjects.IndexOf(CurrentProject);

                // Run full migration
                pulled.WireParentReferences();
                pulled.RefreshHasChildren();
                foreach (var file in pulled.StoredFiles)
                    file.RefreshAnnotationStatus();

                // Restore shared metadata and preserve local name
                pulled.SharedPath = localSharedPath;
                pulled.LastPushedUtc = localLastPushed;
                pulled.Namn = localName;

                // Ensure all files reference the local project name
                foreach (var file in pulled.StoredFiles)
                    file.Uppdrag = localName;

                // Apply selective keep-local: restore local values for checked entries
                if (localFileMap is { Count: > 0 } && keepLocal is { Count: > 0 })
                {
                    foreach (var pulledFile in pulled.StoredFiles)
                    {
                        if (!localFileMap.TryGetValue(pulledFile.Namn, out var localFile))
                            continue;

                        string name = pulledFile.Namn;

                        if (keepLocal.Contains((name, "File")))
                            SharedProjectMerge.AcceptFileMetadata(pulledFile, localFile);

                        if (keepLocal.Contains((name, "Note")))
                            pulledFile.Note = localFile.Note;

                        if (keepLocal.Contains((name, "Annotations")))
                        {
                            pulledFile.AnnotationLayers = new System.Collections.ObjectModel.ObservableCollection<AnnotationLayer>(localFile.AnnotationLayers);
                            foreach (var layer in pulledFile.AnnotationLayers)
                                layer.RecalculateCounts();
                        }

                        if (keepLocal.Contains((name, "Bookmarks")))
                            pulledFile.FavPages = localFile.FavPages;

                        if (keepLocal.Contains((name, "Other Files")))
                            pulledFile.OtherFiles = localFile.OtherFiles;

                        if (keepLocal.Contains((name, "Versions")))
                            pulledFile.Versions = localFile.Versions;
                    }
                }

                // Swap in the pulled project
                if (index >= 0)
                    Storage.StoredProjects[index] = pulled;
                else
                    Storage.StoredProjects.Add(pulled);

                currentProject = pulled;
                pulled.SharedSyncStatus = SharedSyncState.InSync;
                WriteSharedActivityLog(pulled, "pulled (replace)");
                OnPropertyChanged(nameof(CurrentProject));
                UpdateFilter();
                SetProjectlist();
                BuildTreeData();
                MarkDirty();

                PreviewVM.StatusMessage = $"Pulled \"{pulled.Namn}\" from server";
                return true;
            }
            catch (Exception ex)
            {
                PreviewVM.StatusMessage = $"Pull failed: {ex.Message}";
                Utils.ErrorLogger.Log(ex, "PullProject");
                return false;
            }
        }

        /// <summary>
        /// Merges server changes into the local project without losing local work.
        /// Entries with AcceptIncoming checked get their server value applied as
        /// a full overwrite for that specific file + category.
        /// <list type="bullet">
        ///   <item>Files only on server → added to local project</item>
        ///   <item>Files only locally → kept (not removed)</item>
        ///   <item>Files on both sides → per-field merge (additive by default,
        ///     full replace for categories where AcceptIncoming was checked)</item>
        ///   <item>Todos only on server → appended</item>
        ///   <item>Folders only on server → added</item>
        /// </list>
        /// </summary>
        public bool MergeProject(ProjectData server, HashSet<(string File, string Category)>? acceptEntries = null)
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

                        // File metadata (path, type, tag, color)
                        if (SharedProjectMerge.ShouldAccept(acceptEntries, name, "File"))
                        {
                            SharedProjectMerge.AcceptFileMetadata(localFile, serverFile);
                            changed = true;
                        }

                        changed |= SharedProjectMerge.MergeNote(localFile, serverFile,
                            SharedProjectMerge.ShouldAccept(acceptEntries, name, "Note"));

                        changed |= SharedProjectMerge.MergeAnnotations(localFile, serverFile,
                            SharedProjectMerge.ShouldAccept(acceptEntries, name, "Annotations"));

                        changed |= SharedProjectMerge.MergeBookmarks(localFile, serverFile,
                            SharedProjectMerge.ShouldAccept(acceptEntries, name, "Bookmarks"));

                        changed |= SharedProjectMerge.MergeOtherFiles(localFile, serverFile,
                            SharedProjectMerge.ShouldAccept(acceptEntries, name, "Other Files"));

                        changed |= SharedProjectMerge.MergeVersions(localFile, serverFile,
                            SharedProjectMerge.ShouldAccept(acceptEntries, name, "Versions"));

                        if (changed) mergedFiles++;
                    }
                }

                // Merge todos
                if (server.TodoItems is { Count: > 0 })
                {
                    var localTodoTexts = new HashSet<string>(
                        CurrentProject.TodoItems?.Select(t => t.Text ?? "") ?? [],
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var serverTodo in server.TodoItems)
                    {
                        if (!localTodoTexts.Contains(serverTodo.Text ?? ""))
                        {
                            CurrentProject.TodoItems ??= new();
                            CurrentProject.TodoItems.Add(serverTodo);
                            addedTodos++;
                        }
                    }
                }

                // Merge folders
                if (server.Folders is { Count: > 0 })
                {
                    var localFolderPaths = new HashSet<string>(
                        CurrentProject.Folders?.Select(f => f.Path ?? "") ?? [],
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var serverFolder in server.Folders)
                    {
                        if (!localFolderPaths.Contains(serverFolder.Path ?? ""))
                        {
                            CurrentProject.Folders ??= new();
                            CurrentProject.Folders.Add(serverFolder);
                            addedFolders++;
                        }
                    }
                }

                // Refresh project state
                CurrentProject.SetFiletypeList();
                CurrentProject.WireParentReferences();
                CurrentProject.RefreshHasChildren();
                foreach (var file in CurrentProject.StoredFiles)
                    file.RefreshAnnotationStatus();
                UpdateFilter();
                BuildTreeData();
                MarkDirty();

                var parts = new List<string>();
                if (addedFiles > 0) parts.Add($"{addedFiles} new files");
                if (mergedFiles > 0) parts.Add($"{mergedFiles} files enriched");
                if (addedTodos > 0) parts.Add($"{addedTodos} todos");
                if (addedFolders > 0) parts.Add($"{addedFolders} folders");
                string detail = parts.Count > 0 ? string.Join(", ", parts) : "no new content";

                CurrentProject.SharedSyncStatus = SharedSyncState.InSync;
                WriteSharedActivityLog(CurrentProject, $"merged ({detail})");

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

            try
            {
                // Build a filtered copy for serialization
                string json = SerializeForPush(CurrentProject, options);

                string dir = Path.GetDirectoryName(CurrentProject.SharedPath)!;
                Directory.CreateDirectory(dir);

                string tmpPath = CurrentProject.SharedPath + ".tmp";
                File.WriteAllText(tmpPath, json);
                File.Move(tmpPath, CurrentProject.SharedPath, overwrite: true);

                CurrentProject.LastPushedUtc = DateTime.UtcNow;
                CurrentProject.SharedSyncStatus = SharedSyncState.InSync;
                WriteSharedActivityLog(CurrentProject, "pushed");
                MarkDirty();
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
        /// Serializes the project with selective exclusions based on push options.
        /// Uses JSON round-trip: serialize full → parse → prune → re-serialize.
        /// </summary>
        private static string SerializeForPush(ProjectData project, Dialogs.xSharedPushDia options)
        {
            string fullJson = Utils.JsonHelper.Serialize(project);

            // Always run through the filter pass so local-only
            // properties (SharedPath, LastPushedUtc) are stripped.
            using var doc = JsonDocument.Parse(fullJson);
            using var ms = new MemoryStream();
            using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
            {
                WriteFiltered(writer, doc.RootElement, options, depth: 0);
            }
            return System.Text.Encoding.UTF8.GetString(ms.ToArray());
        }

        private static void WriteFiltered(Utf8JsonWriter writer, JsonElement element,
            Dialogs.xSharedPushDia options, int depth, string? parentProp = null)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var prop in element.EnumerateObject())
                    {
                        // Top-level project properties
                        if (depth == 0)
                        {
                            // Always strip local-only metadata from server file
                            if (prop.Name is "SharedPath" or "LastPushedUtc") continue;
                            if (!options.PushFolders && prop.Name == "Folders") continue;
                            if (!options.PushTodo && prop.Name == "TodoItems") continue;
                            if (!options.PushSettings && prop.Name is "Meta_1" or "Meta_2" or "Meta_3"
                                or "Meta_4" or "Meta_5" or "Meta_6" or "Meta_7" or "Meta_8" or "Meta_9"
                                or "Meta_10" or "Meta_11" or "Meta_12" or "Meta_13" or "Meta_14"
                                or "Meta_15" or "Meta_16" or "Meta_17" or "MetaCheckDefault") continue;
                        }

                        // File-level properties (inside StoredFiles array items)
                        if (parentProp == "StoredFiles")
                        {
                            if (!options.PushVersions && prop.Name is "Versions" or "OriginalPath" or "CurrentVersion") continue;
                            if (!options.PushAnnotations && prop.Name == "AnnotationLayers") continue;
                            if (!options.PushOtherFiles && prop.Name == "OtherFiles") continue;
                        }

                        writer.WritePropertyName(prop.Name);
                        // Track "StoredFiles" so array items know to filter file-level props
                        string? childProp = prop.Name == "StoredFiles" ? "StoredFiles" : parentProp;
                        WriteFiltered(writer, prop.Value, options, depth + 1, childProp);
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteFiltered(writer, item, options, depth + 1, parentProp);
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
        /// original owner's copy. The suffix is preserved across pulls
        /// because <see cref="PullProject"/> keeps the local name.
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
                if (imported == null)
                {
                    PreviewVM.StatusMessage = "Import failed: could not deserialize file";
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
            CurrentProject.SharedSyncStatus = SharedSyncState.Unknown;
            MarkDirty();
            BuildTreeData();
            PreviewVM.StatusMessage = $"\"{CurrentProject.Namn}\" is no longer shared";
        }

        /// <summary>
        /// Returns the backup directory path for shared project backups.
        /// </summary>
        public string GetBackupDirectory() => Path.Combine(SavePath, "shared-backup");

        /// <summary>
        /// Restores a shared project from a backup file.
        /// Works like <see cref="PullProject"/> but reads from a local backup instead of the server.
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
                    .OrderByDescending(f => f)
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
        /// Format: "2025-06-09 14:32 | DESKTOP-ABC | pushed (12 files)"
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
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm} | {Environment.MachineName} | {action}";
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
