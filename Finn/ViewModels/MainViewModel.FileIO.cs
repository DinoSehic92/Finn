using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Finn.Model;
using Finn.Storage;
using Finn.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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
                    CurrentProjectsFilePath = files[0].Path.LocalPath;

                    await using var stream = await files[0].OpenReadAsync();
                    using var streamReader = new StreamReader(stream);
                    string fileContent = await streamReader.ReadToEndAsync();
                    DeserializeLoadFile(fileContent);
                }
            }

            public async Task LoadFileAutoAsync()
            {
                string path = Path.Combine(SavePath, "Projects.json");
                CurrentProjectsFilePath = path;

                try
                {
                    PreviewVM.StatusMessage = "Loading projects…";
                    string fileContent = await Task.Run(() => File.ReadAllText(path)).ConfigureAwait(true);
                    DeserializeLoadFile(fileContent);
                    PreviewVM.StatusMessage = "Ready!";
                }
                catch (Exception ex)
                {
                    Utils.ErrorLogger.Log(ex, "LoadFileAutoAsync");
                    Storage = new ProjectStorage();
                    EnsureDefaultProject();
                    SetProjectlist();
                    SetDefaultSelection();
                    PreviewVM.StatusMessage = "Ready!";
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

            /// <summary>
            /// Ensures at least one project exists in <see cref="Storage"/>.
            /// Called during load so that files dragged in by the user always
            /// have a project to attach to.
            /// </summary>
            private void EnsureDefaultProject()
            {
                if (Storage.StoredProjects.Count == 0)
                {
                    Storage.StoredProjects.Add(new ProjectData
                    {
                        Namn = "Project",
                        Category = "Project"
                    });
                }
            }

            public void DeserializeLoadFile(string fileContent)
            {
                Storage = new ProjectStorage();
                try // Try reading as v.2 format (ProjectStorage wrapper)
                {
                    var deserialized = JsonHelper.Deserialize<ProjectStorage>(fileContent);
                    if (deserialized != null)
                        Storage = deserialized;
                }
                catch (Exception v2Ex) when (v2Ex is not OutOfMemoryException
                                                        and not StackOverflowException)
                // If not, try read as v.1 save file (bare project list)
                {
                    try
                    {
                        var projects = JsonHelper.Deserialize<ObservableCollection<ProjectData>>(fileContent);
                        if (projects != null)
                        {
                            // Filter stale categories directly — avoids per-item SetProjectlist/SortProjects
                            Storage.StoredProjects = new ObservableCollection<ProjectData>(
                                projects.Where(x => x.Category != SEARCH_CATEGORY && x.Category != "Favorites"));
                        }
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException
                                                    and not StackOverflowException)
                    {
                        Utils.ErrorLogger.Log(ex, "DeserializeLoadFile: failed to parse v1 and v2 format");
                    }
                }

                // Guard against null collections from partial/corrupt JSON
                Storage.StoredProjects ??= new ObservableCollection<ProjectData>();

                EnsureDefaultProject();

                // Single pass: migrate, wire references, and validate all files.
                foreach (var project in Storage.StoredProjects)
                {
                    project.FlattenAppendedFiles();

                    foreach (var file in project.StoredFiles)
                    {
                        // Clear stale thumbnail references
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

                        // Recompute annotation counts so HasAnnotations is accurate.
                        foreach (var layer in file.AnnotationLayers)
                            layer.RecalculateCounts();
                        file.RefreshAnnotationStatus();
                    }

                    // Resolve ParentFile back-references from the serialized ParentNamn field.
                    project.WireParentReferences();
                    project.RefreshHasChildren();

                    // Inherit parent metadata for appended files (must run after WireParentReferences).
                    foreach (var file in project.StoredFiles)
                    {
                        if (file.IsAppendedFile && file.ParentFile is { } parent)
                        {
                            if (string.IsNullOrEmpty(file.Uppdrag))
                                file.Uppdrag = parent.Uppdrag;
                            if (string.IsNullOrEmpty(file.Filtyp))
                                file.Filtyp = parent.Filtyp;
                        }
                    }
                }

                // SyncPlainText does synchronous file I/O — run on background thread
                // so it does not block the UI during startup.
                Task.Run(() => Data.SyncPlainText())
                    .ContinueWith(t => Utils.ErrorLogger.Log(t.Exception?.InnerException ?? t.Exception, "SyncPlainText"),
                        System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);

                SetProjectlist();
                SetDefaultSelection();
                MigrateGroupsOnLoad();
                SyncPreviewRegionColor();
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
                    CurrentProjectsFilePath = file.Path.LocalPath;

                    await using var stream = await file.OpenWriteAsync();
                    await JsonHelper.SerializeAsync(Storage, stream);
                    await Calendar.SaveStorageAsync(SavePath);
                    ClearDirty();
                }
            }

            public async Task SaveFileAuto()
            {
                try
                {
                    if (!Directory.Exists(SavePath))
                    {
                        Directory.CreateDirectory(SavePath);
                    }

                    string path = Path.Combine(SavePath, "Projects.json");
                    string tmpPath = path + ".tmp";
                    CurrentProjectsFilePath = path;

                    // Stream-serialize to a temp file first (no intermediate string)
                    await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await JsonHelper.SerializeAsync(Storage, fs);
                    }

                    // Rotate backups: keep last 3 copies
                    JsonHelper.RotateBackups(path, maxBackups: 3);

                    File.Move(tmpPath, path, overwrite: true);

                    await Calendar.SaveStorageAsync(SavePath);
                    ClearDirty();
                    PreviewVM.StatusMessage = "Saved";
                }
                catch (Exception ex)
                {
                    PreviewVM.StatusMessage = $"Save failed: {ex.Message}";
                    Utils.ErrorLogger.Log(ex, "SaveFileAuto");
                }
            }

            /// <summary>
            /// Compare the in-memory Storage (Projects) with the on-disk Projects.json file.
            /// Returns true when the two are different (i.e. there are unsaved changes).
            /// </summary>
            public async Task<bool> IsStorageDifferentFromFileAsync(string? projectsFilePath = null)
            {
                string path = !string.IsNullOrWhiteSpace(projectsFilePath)
                    ? projectsFilePath
                    : Path.Combine(SavePath, "Projects.json");

                // Serialize Storage on the UI thread. Storage and its object graph
                // are UI-owned — serializing them from a background thread while the
                // UI can still mutate them risks collection-modified exceptions or
                // an inconsistent snapshot.
                string currentJson;
                try
                {
                    currentJson = JsonHelper.Serialize(Storage);
                }
                catch (Exception ex)
                {
                    Utils.ErrorLogger.Log(ex, "IsStorageDifferentFromFileAsync: serialize");
                    return true;
                }

                // File read and normalization are pure I/O / CPU work with no
                // shared mutable state — safe to run off the UI thread.
                return await Task.Run(() =>
                {
                    try
                    {
                        if (!File.Exists(path))
                            return true;

                        string savedJson = File.ReadAllText(path);

                        // Fast path: byte-identical strings mean nothing changed.
                        if (string.Equals(savedJson, currentJson, StringComparison.Ordinal))
                            return false;

                        // Slow path: normalize both sides to strip transient fields
                        // before concluding something actually changed.
                        using var savedDoc = JsonDocument.Parse(savedJson);
                        using var currentDoc = JsonDocument.Parse(currentJson);

                        var savedNorm = NormalizeElement(savedDoc.RootElement);
                        var currentNorm = NormalizeElement(currentDoc.RootElement);

                        return savedNorm != currentNorm;
                    }
                    catch (Exception ex)
                    {
                        Utils.ErrorLogger.Log(ex, "IsStorageDifferentFromFileAsync: compare");
                        return true;
                    }
                });
            }

            // Remove transient UI fields that used to be stored in Projects.json but
            // are now part of UISettings.json / UI viewmodel. This prevents the
            // comparison from treating those legacy fields as meaningful differences.
            private static readonly HashSet<string> TransientPropertyNames =
            [
                "ThumbnailSource",
                "IsFileMissing",
                // Derived / UI-only properties that should not affect storage equality
                "HasNote",
                "HasBookmarks",
                "HasAppendedFiles",
                "FiletypesTree",
                // LastPushedUtc changes on every push and is not a user-facing edit,
                // so it shouldn't trigger an unsaved-changes prompt on its own.
                "LastPushedUtc",
            ];

            /// <summary>
            /// Produces a normalized JSON string from a <see cref="JsonElement"/>,
            /// pruning transient fields so comparison is stable across formats.
            /// </summary>
            private static string NormalizeElement(JsonElement element)
            {
                using var ms = new MemoryStream();
                using (var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
                {
                    WriteNormalized(writer, element);
                }
                return System.Text.Encoding.UTF8.GetString(ms.ToArray());
            }

            private static void WriteNormalized(Utf8JsonWriter writer, JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        writer.WriteStartObject();
                        foreach (var prop in element.EnumerateObject()
                            .Where(p => !TransientPropertyNames.Contains(p.Name))
                            .OrderBy(p => p.Name, StringComparer.Ordinal))
                        {
                            writer.WritePropertyName(prop.Name);
                            WriteNormalized(writer, prop.Value);
                        }
                        writer.WriteEndObject();
                        break;
                    case JsonValueKind.Array:
                        writer.WriteStartArray();
                        foreach (var item in element.EnumerateArray())
                            WriteNormalized(writer, item);
                        writer.WriteEndArray();
                        break;
                    default:
                        element.WriteTo(writer);
                        break;
                }
            }

            public void BackupSaveFile()
            {
                string backupDir = Path.Combine(SavePath, $"Backup_{DateTime.Today:d}");

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
