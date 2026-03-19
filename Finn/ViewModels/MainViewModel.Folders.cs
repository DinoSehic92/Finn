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
            public void NewFolder()
            {
                CurrentProject.Folders.Add(new FolderData() { Name = "New Folder" });
            }

            public void NewFileFolder()
            {
                if (CurrentFile != null)
                {
                    CurrentProject.Folders.Add(new FolderData() { Name = "New file folder", AttachToFile = CurrentFile.Namn, AttachToFilePath = CurrentFile.Sökväg });
                }
            }

            public void RemoveFolders(List<FolderData> folders)
            {
                foreach (var folder in folders)
                {
                    if (folder.Types == VERSIONS_TYPE)
                    {
                        // Remove all versions whose path falls under this version folder.
                        RemoveVersionsUnderPath(folder.Path);
                        CurrentProject.Folders.Remove(folder);
                        MarkDirty();
                    }
                    else if (folder.IsProjectLevel)
                    {
                        CurrentProject.StoredFiles.RemoveAll(x => x.IsFromFolder && x.SyncFolder == folder.Path);
                        UpdateFilter();
                        CurrentProject.Folders.Remove(folder);
                        OnPropertyChanged("TreeViewUpdate");
                    }
                    else
                    {
                        FileData file = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == folder.AttachToFile);

                        if (file != null)
                        {
                            if (folder.Types is PDF_TYPE or "" or null)
                            {
                                string folderPath = folder.Path;
                                var removed = CurrentProject.StoredFiles
                                    .Where(x => x.ParentNamn == file.Namn && x.SyncFolder == folderPath)
                                    .ToList();
                                foreach (var r in removed)
                                {
                                    r.PartOfCollections.Clear();
                                    r.ParentNamn = string.Empty;
                                    r.ParentFile = null;
                                    CurrentProject.StoredFiles.Remove(r);
                                }
                                CurrentProject.RefreshHasChildren();
                                UpdateFilter();
                            }

                            if (folder.Types == OTHER_FILES_TYPE)
                            {
                                string folderPath = folder.Path;
                                file.OtherFiles.ReplaceAll(
                                    file.OtherFiles.Where(x => x.SyncFolder != folderPath).OrderBy(x => x.Name));
                            }
                        }

                        Collections.SetCollectionContent();
                        CurrentProject.Folders.Remove(folder);
                    }
                }
            }

            public async Task SyncFoldersAsync(List<FolderData> folders, Window? mainWindow = null)
            {
                foreach (var folder in folders)
                {
                    await SyncFolderAsync(folder, mainWindow);
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
                foreach (var f in CurrentProject.StoredFiles.Where(f => !f.IsAppendedFile))
                    lookup.TryAdd(f.Namn, f);
                // Children fill in names that don't have a parent match.
                foreach (var f in CurrentProject.StoredFiles.Where(f => f.IsAppendedFile))
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
                foreach (var file in CurrentProject.StoredFiles)
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

            public async Task SyncFileAsync()
            {
                if (CurrentFile != null)
                {
                    foreach (FolderData folder in CurrentProject.Folders.Where(x => x.AttachToFilePath == CurrentFile.Sökväg))
                    {
                        await SyncFolderAsync(folder);
                    }
                }
            }

            public async Task SyncFolderAsync(FolderData folder, Window? mainWindow = null)
            {
                if (folder?.IsValid() != true || folder.Path == null)
                {
                    return;
                }

                if (folder.Types == VERSIONS_TYPE)
                {
                    await SyncVersionFolderAsync(folder, mainWindow);
                    return;
                }

                if (!folder.IsProjectLevel)
                {
                    FileData file = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == folder.AttachToFile);

                    if (file != null)
                    {
                        int count = 0;

                        if (folder.Types == PDF_TYPE)
                        {
                            // Remove old synced appended files for this folder
                            var oldSynced = CurrentProject.StoredFiles
                                .Where(x => x.ParentNamn == file.Namn && x.SyncFolder == folder.Path)
                                .ToList();
                            foreach (var old in oldSynced)
                                CurrentProject.StoredFiles.Remove(old);

                            var newFiles = GetFilesFromFolder(folder);
                            foreach (var f in newFiles)
                            {
                                f.ParentNamn = file.Namn;
                                f.ParentFile = file;
                                f.Uppdrag = file.Uppdrag;
                                f.Filtyp = file.Filtyp;
                            }
                            count = newFiles.Count;
                            CurrentProject.StoredFiles.AddRange(newFiles);
                            CurrentProject.RefreshHasChildren();
                        }

                        if (folder.Types == OTHER_FILES_TYPE)
                        {
                            var remaining = file.OtherFiles.Where(x => x.SyncFolder != folder.Path);
                            var newFiles = GetOtherFilesFromFolder(folder);
                            count = newFiles.Count;
                            file.OtherFiles.ReplaceAll(remaining.Concat(newFiles).OrderBy(x => x.Name));
                        }

                        folder.SyncedFileCount = count;
                    }
                }
                else
                {
                    List<FileData> files = GetFilesFromFolder(folder);

                    // Use HashSets for O(1) path lookups instead of nested Any() which is O(n×m)
                    var newPaths = new HashSet<string>(files.Select(f => f.Sökväg), StringComparer.OrdinalIgnoreCase);
                    List<FileData> existingFiles = CurrentProject.StoredFiles.Where(x => x.IsFromFolder).Where(x => x.SyncFolder == folder.Path).ToList();
                    var existingPaths = new HashSet<string>(existingFiles.Select(f => f.Sökväg), StringComparer.OrdinalIgnoreCase);

                    List<FileData> filesToRemove = existingFiles.Where(p => !newPaths.Contains(p.Sökväg)).ToList();
                    List<FileData> filesToAdd = files.Where(p => !existingPaths.Contains(p.Sökväg)).ToList();

                    var removeSet = new HashSet<FileData>(filesToRemove);
                    if (removeSet.Count > 0)
                        CurrentProject.StoredFiles.RemoveAll(f => removeSet.Contains(f));

                    var actualAdds = new List<FileData>();
                    var versionCandidates = new List<VersionImportEntry>();
                    var filesByName = BuildFileNameLookup();

                    foreach (FileData file in filesToAdd)
                    {
                        if (CurrentProject.StoredFiles.Any(x => string.Equals(x.Sökväg, file.Sökväg, StringComparison.OrdinalIgnoreCase)))
                            continue;

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
                            actualAdds.Add(file);
                        }
                    }

                    if (actualAdds.Count > 0)
                        CurrentProject.StoredFiles.AddRange(actualAdds);

                    if (versionCandidates.Count > 0 && mainWindow != null)
                    {
                        bool confirmed = await ShowVersionImportDialogAsync(mainWindow, versionCandidates);
                        if (confirmed)
                        {
                            foreach (var entry in versionCandidates)
                                entry.ExistingFile.AddVersion(entry.NewFilePath, entry.SelectedLabel);
                        }
                    }

                    folder.SyncedFileCount = CurrentProject.StoredFiles.Count(x => x.IsFromFolder && x.SyncFolder == folder.Path);

                    CurrentProject.SetFiletypeList();
                    UpdateFilter();
                    BuildTreeData();
                }
            }

            public void NewVersionFolder(string path)
            {
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                    return;

                var folder = new FolderData
                {
                    Name = new DirectoryInfo(path).Name,
                    AttachToFile = "PROJECT",
                    Types = VERSIONS_TYPE,
                    Path = path
                };
                CurrentProject.Folders.Add(folder);
                MarkDirty();
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
                foreach (var f in CurrentProject.StoredFiles)
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
                foreach (var file in CurrentProject.StoredFiles)
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
                        file.OriginalPath = null;
                    }
                }
            }

            /// <summary>
            /// Scans a folder and all subfolders for PDFs that match existing project files
            /// by name, then presents a delivery import dialog grouped by subfolder.
            /// Each delivery folder gets one label applied to all matched files.
            /// Already-registered version paths are skipped.
            /// </summary>
            public async Task SyncVersionFolderAsync(FolderData folder, Window? mainWindow = null)
            {
                if (folder?.IsValid() != true || folder.Path == null) return;

                // Collect all PDFs recursively, sorted by path so subfolder order is consistent
                var allPdfs = Directory.EnumerateFiles(folder.Path, "*.pdf", SearchOption.AllDirectories)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                    .ToList();

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
                    // No new files to import, but still reflect total synced versions.
                    folder.SyncedFileCount = CountVersionsUnderPath(folder.Path);
                    return;
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

                int importedCount = 0;

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
                                importedCount++;
                            }
                        }
                        MarkDirty();
                    }
                }

                // Count all versions under this folder, not just the ones imported this sync.
                folder.SyncedFileCount = CountVersionsUnderPath(folder.Path);
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
                    if (parent == dir) break;
                    dir = parent;
                }
                return null;
            }

            [GeneratedRegex(@"\d{4}-\d{2}-\d{2}")]
            private static partial Regex DatePatternLong();

            [GeneratedRegex(@"(?<!\d)\d{6}(?!\d)")]
            private static partial Regex DatePatternShort();

            private List<FileData> GetFilesFromFolder(FolderData folder)
            {
                List<FileData> files = new();

                if (folder.IsValid())
                {
                    foreach (string path in Directory.GetFiles(folder.Path))
                    {
                        if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            files.Add(new FileData()
                            {
                                Namn = System.IO.Path.GetFileNameWithoutExtension(path),
                                Sökväg = path,
                                Uppdrag = CurrentProject.Namn,
                                Filtyp = NEW_TYPE,
                                SyncFolder = folder.Path,
                                IsFromFolder = true
                            });
                        }
                    }
                }

                return files;
            }

            private List<OtherData> GetOtherFilesFromFolder(FolderData folder)
            {
                List<OtherData> files = new();

                if (folder.IsValid())
                {
                    foreach (string path in Directory.GetFiles(folder.Path))
                    {
                        OtherData newFile = new()
                        {
                            Name = System.IO.Path.GetFileNameWithoutExtension(path),
                            Filepath = path,
                            SyncFolder = folder.Path,
                            IsFromFolder = true
                        };

                        newFile.SetFile();
                        files.Add(newFile);
                    }
                }

                return files;
            }
        }
    }
