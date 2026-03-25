using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Finn.Model;
using Finn.Storage;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            public async Task LoadFile(Visual window)
            {
                var topLevel = TopLevel.GetTopLevel(window);

                var jsonformat = new FilePickerFileType("Json format") { Patterns = new[] { "*.json" } };
                List<FilePickerFileType> formatlist = new() { jsonformat };
                IReadOnlyList<FilePickerFileType> fileformat = formatlist;

                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Load File",
                    AllowMultiple = false,
                    FileTypeFilter = fileformat
                });

                if (files.Count > 0)
                {
                    // Remember the loaded file path so comparisons target the correct file
                    try { CurrentProjectsFilePath = files[0].Path.LocalPath; } catch { CurrentProjectsFilePath = null; }

                    await using var stream = await files[0].OpenReadAsync();
                    using var streamReader = new StreamReader(stream);
                    string fileContent = await streamReader.ReadToEndAsync();
                    DeserializeLoadFile(fileContent);
                }
            }

            public void LoadFileAuto()
            {
                string path = Path.Combine(SavePath, "Projects.json");
                try { CurrentProjectsFilePath = path; } catch { CurrentProjectsFilePath = null; }

                try
                {
                    using StreamReader streamReader = new(path);
                    string fileContent = streamReader.ReadToEnd();
                    DeserializeLoadFile(fileContent);
                }
                catch
                {
                    Storage = new ProjectStorage();
                    SetProjectlist();
                    SetDefaultSelection();
                }
            }

            /// <summary>
            /// Reconciles the local file cache against the currently loaded
            /// projects. Removes cache entries for files that are no longer
            /// marked <see cref="FileData.IsCached"/> (e.g. the user toggled
            /// caching but never saved Projects.json).
            /// </summary>
            public void ReconcileFileCache()
            {
                var validPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var file in project.StoredFiles)
                    {
                        if (!file.IsCached) continue;
                        if (!string.IsNullOrEmpty(file.Sökväg))
                            validPaths.Add(file.Sökväg);
                        foreach (var ver in file.Versions)
                        {
                            if (!string.IsNullOrEmpty(ver.Sökväg))
                                validPaths.Add(ver.Sökväg);
                        }
                    }
                }

                PreviewVM.ReconcileCache(validPaths);
            }

            public void DeserializeLoadFile(string fileContent)
            {
                Storage = new ProjectStorage();
                try // Trying reading v.2 save file
                {
                    Storage = JsonConvert.DeserializeObject<ProjectStorage>(fileContent);
                }
                catch // If not, try read as v.1 save file
                {
                    Storage.StoredProjects = JsonConvert.DeserializeObject<ObservableCollection<ProjectData>>(fileContent);
                    RemoveProjects(Storage.StoredProjects.Where(x => x.Category == SEARCH_CATEGORY).ToList());
                    RemoveProjects(Storage.StoredProjects.Where(x => x.Category == "Favorites").ToList());
                }

                // Clear ThumbnailSource for any file whose thumbnail no longer exists on disk
                // and wire ParentFile back-references for appended files.
                foreach (var project in Storage.StoredProjects)
                {
                    // Migrate legacy nested AppendedFiles into the flat StoredFiles list.
                    project.FlattenAppendedFiles();

                    foreach (var file in project.StoredFiles)
                    {
                        if (!string.IsNullOrEmpty(file.ThumbnailSource) && !File.Exists(file.ThumbnailSource))
                            file.ThumbnailSource = string.Empty;

                        // Migrate legacy ORIGINAL version entries to OriginalPath.
                        if (file.Versions.Count > 0 && string.IsNullOrEmpty(file.OriginalPath))
                        {
                            var original = file.Versions.FirstOrDefault(v => v.Label == "ORIGINAL");
                            if (original != null)
                            {
                                file.OriginalPath = original.Sökväg;
                                bool wasOnOriginal = file.CurrentVersion == "ORIGINAL";
                                if (wasOnOriginal)
                                    file.CurrentVersion = string.Empty;
                                file.Versions.Remove(original);
                            }
                        }

                        // Inherit parent metadata for appended files that are missing it
                        if (file.IsAppendedFile && file.ParentFile is { } parent)
                        {
                            if (string.IsNullOrEmpty(file.Uppdrag))
                                file.Uppdrag = parent.Uppdrag;
                            if (string.IsNullOrEmpty(file.Filtyp))
                                file.Filtyp = parent.Filtyp;
                        }
                    }

                    // Resolve ParentFile back-references from the serialized ParentNamn field.
                    project.WireParentReferences();
                    project.RefreshHasChildren();

                    // Recompute annotation counts so HasAnnotations is accurate after deserialization.
                    foreach (var f in project.StoredFiles)
                    {
                        foreach (var layer in f.AnnotationLayers)
                            layer.RecalculateCounts();
                        f.RefreshAnnotationStatus();
                    }
                }

                SetProjectlist();
                SetDefaultSelection();
                GetGroups();
                SyncPreviewRegionColor();
                Data.SyncPlainText();
            }

            public async Task SaveFile(Avalonia.Visual window)
            {
                var topLevel = TopLevel.GetTopLevel(window);

                var jsonformat = new FilePickerFileType("Json format") { Patterns = new[] { "*.json" } };
                List<FilePickerFileType> formatlist = new() { jsonformat };
                IReadOnlyList<FilePickerFileType> fileformat = formatlist;

                var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save File",
                    FileTypeChoices = fileformat
                });

                if (file is not null)
                {
                    // Remember the path the user chose for future comparisons
                    try { CurrentProjectsFilePath = file.Path.LocalPath; } catch { CurrentProjectsFilePath = null; }

                    await using var stream = await file.OpenWriteAsync();
                    using var streamWriter = new StreamWriter(stream);
                    var data = JsonConvert.SerializeObject(Storage);
                    await streamWriter.WriteLineAsync(data);
                    ClearDirty();
                }
            }

            public async Task SaveFileAuto()
            {
                if (!Directory.Exists(SavePath))
                {
                    Directory.CreateDirectory(SavePath);
                }

                string path = Path.Combine(SavePath, "Projects.json");
                try { CurrentProjectsFilePath = path; } catch { CurrentProjectsFilePath = null; }

                var data = JsonConvert.SerializeObject(Storage);
                await File.WriteAllTextAsync(path, data);
                ClearDirty();
            }

            /// <summary>
            /// Compare the in-memory Storage (Projects) with the on-disk Projects.json file.
            /// Returns true when the two are different (i.e. there are unsaved changes).
            /// </summary>
            public bool IsStorageDifferentFromFile(string? projectsFilePath = null)
            {
                try
                {
                    string path;
                    if (!string.IsNullOrWhiteSpace(projectsFilePath))
                        path = projectsFilePath;
                    else if (!string.IsNullOrWhiteSpace(CurrentProjectsFilePath))
                        path = CurrentProjectsFilePath;
                    else
                        path = Path.Combine(SavePath, "Projects.json");

                    Debug.WriteLine($"Comparing storage to file: '{path}'");
                    if (!File.Exists(path))
                    {
                        // No file on disk -> consider storage different (unsaved)
                        return true;
                    }

                    string fileContent = File.ReadAllText(path);

                    // Parse saved JSON
                    JToken saved = JToken.Parse(fileContent);

                    // Prune transient UI-related fields that may exist in older save files
                    // but are no longer part of StoreData. This avoids false positives when
                    // comparing the in-memory model to an on-disk file from an older format.
                    PruneTransientUiFields(saved);

                    // Serialize current storage using same JsonConvert pipeline as Save to avoid
                    // differences caused by serializer variations (null vs omitted, converters, etc.)
                    string currentJson = JsonConvert.SerializeObject(Storage);
                    var current = JToken.Parse(currentJson);

                    // Prune transient fields from the current representation as well
                    PruneTransientUiFields(current);

                    return !JToken.DeepEquals(saved, current);
                }
                catch
                {
                    // If comparison fails for any reason, assume changed so caller can decide to save.
                    return true;
                }
            }

            // Remove transient UI fields that used to be stored in Projects.json but
            // are now part of UISettings.json / UI viewmodel. This prevents the
            // comparison from treating those legacy fields as meaningful differences.
            private static readonly string[] TransientPropertyNames = new[]
            {
                "ThumbnailSource",
                "IsFileMissing",
                // Derived / UI-only properties that should not affect storage equality
                "HasNote",
                "HasBookmarks",
                "HasAppendedFiles",
                "FiletypesTree",
            };

            private static void PruneTransientUiFields(JToken? token)
            {
                if (token == null) return;

                // Recursively remove any properties with names considered transient.
                void Recurse(JToken t)
                {
                    if (t.Type == JTokenType.Object)
                    {
                        var obj = (JObject)t;
                        // Collect properties to remove to avoid modifying collection during enumeration
                        var toRemove = obj.Properties().Where(p => TransientPropertyNames.Contains(p.Name)).ToList();
                        foreach (var p in toRemove)
                            p.Remove();

                        // Recurse into remaining properties
                        foreach (var child in obj.Properties())
                            Recurse(child.Value);
                    }
                    else if (t.Type == JTokenType.Array)
                    {
                        foreach (var item in (JArray)t)
                            Recurse(item);
                    }
                }

                Recurse(token);
            }

            public void BackupSaveFile()
            {
                string backupDir = $"{SavePath}\\Backup_{DateTime.Today:d}";

                Directory.CreateDirectory(backupDir);

                foreach (string fileName in new[] { "Projects.json", "Content.json", "Calendar.json", "UISettings.json" })
                {
                    string src = Path.Combine(SavePath, fileName);
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(backupDir, fileName), overwrite: true);
                }
            }
        }
    }
