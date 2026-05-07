using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Finn.Model;
using iText.IO.Font;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            #region Commands

            private ICommand? _checkSingleFileCommand;
            public ICommand CheckSingleFileCommand => _checkSingleFileCommand ??= new RelayCommand(CheckSingleFile);

            private ICommand? _checkProjectFilesCommand;
            public ICommand CheckProjectFilesCommand => _checkProjectFilesCommand ??= new AsyncRelayCommand(CheckProjectFiles);

            private ICommand? _toggleCacheFilesCommand;
            public ICommand ToggleCacheFilesCommand => _toggleCacheFilesCommand ??= new AsyncRelayCommand(ToggleCacheFiles);

            #endregion

            /// <summary>
            /// Toggles <see cref="FileData.IsCached"/> for all selected files.
            /// Files that are newly marked for caching are pre-cached in the
            /// background immediately so subsequent opens are instant.
            /// </summary>
            private async Task ToggleCacheFiles()
            {
                if (CurrentFiles == null || CurrentFiles.Count == 0) return;

                var files = CurrentFiles.ToList();
                // Determine new state: if any selected file is not cached, we cache all; otherwise uncache all.
                bool newState = files.Any(f => !f.IsCached);

                foreach (var file in files)
                    file.IsCached = newState;

                if (!newState) return;

                // Collect all paths to pre-cache: main file + all version paths
                var paths = new List<string>();
                foreach (var file in files)
                {
                    if (!string.IsNullOrEmpty(file.Sökväg))
                        paths.Add(file.Sökväg);
                    foreach (var ver in file.Versions)
                    {
                        if (!string.IsNullOrEmpty(ver.Sökväg))
                            paths.Add(ver.Sökväg);
                    }
                }

                // Pre-cache in the background with cancellation support
                int total = paths.Count;
                int done = 0;
                var cts = new CancellationTokenSource();
                PreviewVM.SetBackgroundTaskCts(cts);
                PreviewVM.BackgroundTaskActive = true;
                PreviewVM.BackgroundTaskMessage = $"Pre-caching 0/{total}…";
                PreviewVM.BackgroundTaskProgress = 0;

                try
                {
                    foreach (var path in paths)
                    {
                        if (cts.Token.IsCancellationRequested) break;
                        try
                        {
                            await PreviewVM.PreCacheFileAsync(path, cts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { break; }
                        catch { }
                        done++;
                        PreviewVM.BackgroundTaskMessage = $"Pre-caching {done}/{total}…";
                        PreviewVM.BackgroundTaskProgress = (int)(100.0 * done / total);
                    }

                    PreviewVM.BackgroundTaskMessage = cts.Token.IsCancellationRequested
                        ? $"Cancelled — cached {done}/{total} file(s)"
                        : $"Cached {done} file(s)";
                    PreviewVM.BackgroundTaskProgress = 100;

                    // Keep visible briefly so the user sees completion, then hide
                    await Task.Delay(2000).ConfigureAwait(false);
                }
                finally
                {
                    PreviewVM.SetBackgroundTaskCts(null);
                    PreviewVM.BackgroundTaskActive = false;
                    cts.Dispose();
                }
            }

            public async Task AddFile(Avalonia.Visual window)
            {
                if (CurrentProject == null) return;

                var topLevel = TopLevel.GetTopLevel(window);
                if (topLevel == null) return;
                var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Add File",
                    FileTypeFilter = new[] { FilePickerFileTypes.Pdf },
                    AllowMultiple = true
                });

                if (files.Count == 0) return;

                var mainWindow = topLevel as Window;
                await AddFilesWithVersionCheck(
                    files.Select(f => f.Path.LocalPath), mainWindow, "File Picker");
            }

            /// <summary>
            /// Adds files to the current project. Files that match an existing entry
            /// by name are collected and presented in a version-import dialog so the
            /// user can choose the label before they are registered as versions.
            /// Shows an import confirmation dialog so the user can review files,
            /// pick a category, and remove unwanted entries before importing.
            /// </summary>
            public async Task AddFilesWithVersionCheck(IEnumerable<string> paths, Window? mainWindow, string source = "Added")
            {
                // Build O(1) lookup structures to avoid linear scans per file
                var existingPaths = BuildKnownPathSet();
                var existingByName = BuildFileNameLookup();

                // When a specific type is selected, use it as the default category
                string? defaultCategory = (Type != null && Type != ALL_TYPES) ? Type : null;

                var candidatePaths = new List<(string Path, string Source)>();
                var versionCandidates = new List<VersionImportEntry>();
                int skippedCount = 0;

                foreach (string path in paths)
                {
                    if (existingPaths.Contains(path))
                    {
                        skippedCount++;
                        continue;
                    }

                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

                    if (existingByName.TryGetValue(fileName, out var existing))
                    {
                        versionCandidates.Add(new VersionImportEntry
                        {
                            ExistingFile = existing,
                            NewFilePath = path
                        });
                    }
                    else
                    {
                        candidatePaths.Add((path, source));
                        existingPaths.Add(path);
                        existingByName.TryAdd(fileName, null!);
                    }
                }

                // Show import dialog for new files
                if (candidatePaths.Count > 0 && mainWindow != null)
                {
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

                        var newFiles = new List<FileData>();
                        foreach (var (path, _) in candidatePaths)
                        {
                            if (!acceptedSet.Contains(path)) continue;
                            newFiles.Add(new FileData
                            {
                                Namn = System.IO.Path.GetFileNameWithoutExtension(path),
                                Filtyp = assignedType,
                                Uppdrag = CurrentProject!.Namn,
                                Sökväg = path
                            });
                        }

                        if (newFiles.Count > 0)
                            CurrentProject!.AddFiles(newFiles);
                    }
                    else
                    {
                        // User cancelled — skip version dialog too
                        return;
                    }
                }

                bool versionsAdded = false;
                if (versionCandidates.Count > 0 && mainWindow != null)
                {
                    bool confirmed = await ShowVersionImportDialogAsync(mainWindow, versionCandidates);
                    if (confirmed)
                    {
                        foreach (var entry in versionCandidates)
                            entry.ExistingFile.AddVersion(entry.NewFilePath, entry.SelectedLabel);
                        versionsAdded = true;
                    }
                }

                if (candidatePaths.Count > 0 || versionsAdded)
                {
                    UpdateFilter();
                    MarkDirty();
                }
            }

            // Theme/resource updates handled by UIService

            public void SetCategory(string category)
            {
                SetProjecCategory(category);
            }

            public void SetGroup(string? group)
            {
                CurrentProject!.Parent = string.IsNullOrWhiteSpace(group) ? null : group;
                MarkDirty();
            }

            public void CopyFilenameToClipboard(Avalonia.Visual window)
            {
                if (CurrentFiles == null) return;
                var text = string.Join(Environment.NewLine, CurrentFiles.Select(f => f.Namn));
                var clipboard2 = TopLevel.GetTopLevel(window)?.Clipboard;
                if (clipboard2 != null)
                    _ = clipboard2.SetTextAsync(text)
                        .ContinueWith(t => Utils.ErrorLogger.Log(t.Exception!, "CopyFilenameToClipboard"),
                            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            public void CopyFilepathToClipboard(Avalonia.Visual window)
            {
                if (CurrentFiles == null) return;
                var text = string.Join(Environment.NewLine, CurrentFiles.Select(f => f.Sökväg));
                var clipboard3 = TopLevel.GetTopLevel(window)?.Clipboard;
                if (clipboard3 != null)
                    _ = clipboard3.SetTextAsync(text)
                        .ContinueWith(t => Utils.ErrorLogger.Log(t.Exception!, "CopyFilepathToClipboard"),
                            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            public void CopyListviewToClipboard(Avalonia.Visual window)
            {
                if (CurrentFiles == null) return;
                var sb = new StringBuilder();

                foreach (FileData file in CurrentFiles)
                {
                    if (CurrentProject!.Meta_1 == true) { sb.Append(file.Namn).Append('\t'); }
                    if (CurrentProject!.Meta_2 == true) { sb.Append(file.Filtyp).Append('\t'); }
                    if (CurrentProject!.Meta_3 == true) { sb.Append(file.Uppdrag).Append('\t'); }
                    if (CurrentProject!.Meta_4 == true) { sb.Append(file.Tagg).Append('\t'); }
                    if (CurrentProject!.Meta_5 == true) { sb.Append(file.Färg).Append('\t'); }
                    if (CurrentProject!.Meta_6 == true) { sb.Append(file.Handling).Append('\t'); }
                    if (CurrentProject!.Meta_7 == true) { sb.Append(file.Status).Append('\t'); }
                    if (CurrentProject!.Meta_8 == true) { sb.Append(file.Datum).Append('\t'); }
                    if (CurrentProject!.Meta_9 == true) { sb.Append(file.Ritningstyp).Append('\t'); }
                    if (CurrentProject!.Meta_10 == true) { sb.Append(file.Beskrivning1).Append('\t'); }
                    if (CurrentProject!.Meta_11 == true) { sb.Append(file.Beskrivning2).Append('\t'); }
                    if (CurrentProject!.Meta_12 == true) { sb.Append(file.Beskrivning3).Append('\t'); }
                    if (CurrentProject!.Meta_13 == true) { sb.Append(file.Beskrivning4).Append('\t'); }
                    if (CurrentProject!.Meta_14 == true) { sb.Append(file.Revidering).Append('\t'); }
                    if (CurrentProject!.Meta_15 == true) { sb.Append(file.Sökväg).Append('\t'); }

                    sb.AppendLine();
                }
                var clipboard = TopLevel.GetTopLevel(window)?.Clipboard;
                if (clipboard != null)
                    _ = clipboard.SetTextAsync(sb.ToString())
                        .ContinueWith(t => Utils.ErrorLogger.Log(t.Exception!, "CopyListviewToClipboard"),
                            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
            }

            public void CheckSingleFile()
            {
                if (CurrentFile != null && !CurrentFile.IsGroup)
                    CurrentFile.IsFileMissing = !CurrentFile.IsValidPdf();
            }

            public async Task CheckProjectFiles()
            {
                await Task.Run(() => CheckFileAsync());
            }

            public async Task CheckFileAsync()
            {
                if (CurrentProject == null) return;

                // Capture the file list on the UI thread before entering
                // the background task, so we never iterate an ObservableCollection
                // from a thread-pool thread (which can corrupt it or throw).
                var files = CurrentProject.StoredFiles.ToList();

                // Clear status on the UI thread.
                await Dispatcher.UIThread.InvokeAsync(ClearFileStatus);

                int n = files.Count;
                int i = 0;
                var results = new List<(FileData File, bool Missing, int Progress)>(files.Count);

                foreach (FileData file in files)
                {
                    i++;
                    bool missing = !file.IsGroup && !file.IsValidPdf();

                    results.Add((
                        file,
                        missing,
                        n == 0 ? 0 : (int)(100 * ((float)i / n))));
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var result in results)
                    {
                        result.File.IsFileMissing = result.Missing;
                        PreviewVM.Progress = result.Progress;
                    }
                });
            }

            public void ClearFileStatus()
            {
                if (CurrentProject == null) return;
                foreach (FileData file in CurrentProject.StoredFiles)
                    file.IsFileMissing = false;
            }

            public void OpenFile()
            {
                if (CurrentFiles == null) return;
                try
                {
                    foreach (FileData file in CurrentFiles)
                    {
                        if (string.IsNullOrEmpty(file.Sökväg)) continue;
                        ProcessStartInfo psi = new()
                        {
                            FileName = file.Sökväg,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception e)
                {
                    Utils.ErrorLogger.Log(e, "OpenFile");
                    PreviewVM.StatusMessage = $"Could not open file: {e.Message}";
                }
            }

            public void OpenFileDirect(string path)
            {
                try
                {
                    ProcessStartInfo psi = new()
                    {
                        FileName = path,
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                }
                catch (Exception e)
                {
                    Utils.ErrorLogger.Log(e, "OpenFileDirect");
                    PreviewVM.StatusMessage = $"Could not open file: {e.Message}";
                }
            }

            public void OpenMeta()
            {
                if (CurrentFiles == null) return;
                try
                {
                    foreach (FileData file in CurrentFiles)
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = file.Sökväg + ".md",
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception e)
                {
                    Utils.ErrorLogger.Log(e, "OpenMeta");
                    PreviewVM.StatusMessage = $"Could not open notes file: {e.Message}";
                }
            }

            public void OpenDwg()
            {
                if (CurrentFile?.Filtyp == DRAWING_TYPE)
                {
                    string dwgPathOld = CurrentFile.Sökväg.Replace("Ritning", "Ritdef").Replace("pdf", "dwg");
                    string dwgPathNew = CurrentFile.Sökväg.Replace("Drawing", "Drawing Definition").Replace("pdf", "dwg");

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = dwgPathOld,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = dwgPathNew,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                }
            }

            public void OpenDoc()
            {
                if (CurrentFile?.Filtyp == DOCUMENT_TYPE)
                {
                    string docPath = CurrentFile.Sökväg.Replace("pdf", "docx");

                    try
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = docPath,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception) { }
                }
            }

            public void OpenPath()
            {
                try
                {
                    if (string.IsNullOrEmpty(CurrentFile?.Sökväg)) return;
                    string? folderpath = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void OpenPathDirect(string filepath)
            {
                try
                {
                    string? folderpath = System.IO.Path.GetDirectoryName(filepath);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void AddColor(string color)
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = color;
                }
                MarkDirty();
            }

            public void ClearAll()
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = "";
                    file.Tagg = "";
                }
                MarkDirty();
            }

            public void AddTag(string tag)
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = tag;
                }
                MarkDirty();
            }

            public void ClearTag()
            {
                if (CurrentFiles == null) return;
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = "";
                }
                MarkDirty();
            }

            public void EditType(string type)
            {
                SetTypeSelected(type);
            }

            public void AddAppendedFile(string filepath, bool fromFolder = false)
            {
                if (CurrentFile != null && !CurrentProject!.StoredFiles.Any(x =>
                    string.Equals(x.Sökväg, filepath, StringComparison.OrdinalIgnoreCase)))
                {
                    var appended = new FileData()
                    {
                        Namn = System.IO.Path.GetFileNameWithoutExtension(filepath),
                        Sökväg = filepath,
                        IsFromFolder = fromFolder,
                        Uppdrag = CurrentFile.Uppdrag,
                        Filtyp = CurrentFile.Filtyp
                    };

                    appended.SetParent(CurrentFile);

                    CurrentProject.StoredFiles.Add(appended);
                    CurrentFile.HasChildren = true;
                    if (!CurrentFile.IsExpanded)
                    {
                        CurrentFile.IsExpanded = true;
                        UpdateFilter();
                    }
                    MarkDirty();
                }
            }

            public void AddOtherFile(string filepath)
            {
                var owner = OtherFilesOwner;
                string candidatePath = NormalizePathForComparison(filepath);
                if (owner != null && !owner.OtherFiles.Any(x =>
                    string.Equals(NormalizePathForComparison(x.Filepath), candidatePath, GetPathComparison())))
                {
                    OtherData newFile = new() { Filepath = filepath };
                    newFile.SetFile();

                    owner.OtherFiles.Add(newFile);
                    SortOtherFiles();
                    MarkDirty();
                }
            }

            private static StringComparison GetPathComparison() =>
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            private static string NormalizePathForComparison(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return string.Empty;

                try
                {
                    return Path.GetFullPath(path)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    return path;
                }
            }

            public void RemoveOtherFile(OtherData file)
            {
                if (file != null)
                {
                    bool wasSynced = file.IsFromFolder && !string.IsNullOrEmpty(file.SyncFolder);
                    string? syncFolder = file.SyncFolder;

                    OtherFilesOwner?.OtherFiles.Remove(file);
                    SortOtherFiles();
                    MarkDirty();

                    if (wasSynced && syncFolder != null)
                        FlagSyncFoldersAsPending(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { syncFolder });
                }
            }

            public void RemoveOtherFiles(IEnumerable<OtherData> files)
            {
                var owner = OtherFilesOwner;
                if (owner == null) return;

                var pendingSyncFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in files)
                {
                    if (file.IsFromFolder && !string.IsNullOrEmpty(file.SyncFolder))
                        pendingSyncFolders.Add(file.SyncFolder!);
                    owner.OtherFiles.Remove(file);
                }
                SortOtherFiles();
                MarkDirty();

                if (pendingSyncFolders.Count > 0)
                    FlagSyncFoldersAsPending(pendingSyncFolders);
            }

            private void SortOtherFiles()
            {
                var owner = OtherFilesOwner;
                if (owner != null)
                {
                    SortOtherFilesDirect(owner);
                }
            }

            private void SortOtherFilesDirect(FileData file)
            {
                file.OtherFiles.ReplaceAll(file.OtherFiles.OrderBy(x => x.Name));
            }

            /// <summary>
            /// Updates all name-based links that reference a file after it is renamed.
            /// Fixes child file <see cref="FileData.ParentNamn"/> and folder
            /// <see cref="FolderData.AttachToFile"/> so the links don't break.
            /// </summary>
            internal void UpdateFileLinks(string oldName, string newName, string? newPath)
            {
                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return;

                // Update children that point back to this file by name
                foreach (var child in CurrentProject!.StoredFiles)
                {
                    if (string.Equals(child.ParentNamn, oldName, StringComparison.OrdinalIgnoreCase))
                        child.ParentNamn = newName;
                }

                // Update folder entries attached to this file
                foreach (var folder in CurrentProject!.Folders)
                {
                    if (string.Equals(folder.AttachToFile, oldName, StringComparison.OrdinalIgnoreCase))
                    {
                        folder.AttachToFile = newName;
                        if (newPath != null)
                            folder.AttachToFilePath = newPath;
                    }
                }
            }

            /// <summary>
            /// Renames a file's display name and updates all child/folder links.
            /// Use this instead of setting <see cref="FileData.Namn"/> directly
            /// to keep name-based references consistent.
            /// </summary>
            public void RenameFile(FileData file, string newName)
            {
                if (file == null || string.IsNullOrEmpty(newName))
                    return;

                if (file.IsTopLevel)
                    newName = EnsureUniqueName(newName, file);

                string oldName = file.Namn;
                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return;

                file.Namn = newName;
                UpdateFileLinks(oldName, newName, file.Sökväg);
            }

            public (bool Success, string Message) RenameOriginal(string newName)
            {
                if (CurrentFile == null)
                    return (false, "No file is selected.");

                if (string.IsNullOrWhiteSpace(newName))
                    return (false, "Please enter a file name.");

                if (!CurrentFile.IsLocal())
                    return (false, "Only available for files stored on C:\\");

                if (CurrentFile.IsTopLevel)
                    newName = EnsureUniqueName(newName, CurrentFile);

                string oldName = CurrentFile.Namn;
                string oldPath = CurrentFile.Sökväg;

                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return (true, string.Empty);

                string extension = System.IO.Path.GetExtension(oldPath);
                string? directory = System.IO.Path.GetDirectoryName(oldPath);
                if (string.IsNullOrEmpty(directory))
                    return (false, "Could not determine the file directory for rename.");

                string newPath = System.IO.Path.Combine(directory, newName + extension);

                try
                {
                    System.IO.File.Move(oldPath, newPath);
                }
                catch (Exception ex)
                {
                    Utils.ErrorLogger.Log(ex, nameof(RenameOriginal));
                    return (false, $"Could not rename file. {ex.Message}");
                }

                CurrentFile.Sökväg = newPath;
                CurrentFile.Namn = newName;
                UpdateFileLinks(oldName, newName, newPath);
                MarkDirty();
                return (true, string.Empty);
            }

            public void ReplaceFilePath(string newPath, bool fileExists)
            {
                if (CurrentFile == null || string.IsNullOrWhiteSpace(newPath))
                    return;

                bool wasGroup = CurrentFile.IsGroup;
                string oldName = CurrentFile.Namn;
                string newName = System.IO.Path.GetFileNameWithoutExtension(newPath);
                if (CurrentFile.IsTopLevel)
                    newName = EnsureUniqueName(newName, CurrentFile);
                CurrentFile.Sökväg = newPath;
                CurrentFile.Namn = newName;
                CurrentFile.IsFileMissing = !fileExists;

                UpdateFileLinks(oldName, newName, newPath);

                // Clear group status — the file now has a real path
                if (wasGroup)
                {
                    CurrentFile.IsGroup = false;
                    RefreshChildrenFromParent(CurrentFile, syncCategory: true);
                    RefreshHierarchyState();
                    NotifyCurrentSelectionStructureChanged();
                }

                // Clear any stale sync-folder metadata. A file with a newly
                // assigned path is no longer managed by the sync system.
                if (CurrentFile.IsFromFolder)
                {
                    CurrentFile.IsFromFolder = false;
                    CurrentFile.SyncFolder = null;
                }

                MarkDirty();
            }


            public void MoveSelectedFiles(ProjectData project)
            {
                if (project == null) return;

                foreach (FileData file in CurrentFiles.ToList())
                {
                    // Skip appended files — they move with their parent
                    if (file.IsChild) continue;

                    // Groups and synced files are not allowed to move between projects.
                    // CanMoveSelectedFiles already enforces this, but guard here as well
                    // so a direct call can't bypass the check.
                    if (file.IsGroup || file.IsFromFolder) continue;

                    var children = CurrentProject!.GetChildren(file);

                    // If any child is from a sync folder, skip the whole parent —
                    // moving it would silently orphan the source folder's baseline.
                    if (children.Any(c => c.IsFromFolder)) continue;

                    if (!project.StoredFiles.Contains(file))
                    {
                        // Move children along with the parent
                        foreach (var child in children)
                        {
                            CurrentProject.StoredFiles.Remove(child);
                            if (string.IsNullOrEmpty(child.Filtyp)) child.Filtyp = NEW_TYPE;
                            child.Uppdrag = project.Namn;
                            project.StoredFiles.Add(child);
                        }

                        CurrentProject.StoredFiles.Remove(file);
                        if (string.IsNullOrEmpty(file.Filtyp)) file.Filtyp = NEW_TYPE;
                        file.Uppdrag = project.Namn;
                        project.StoredFiles.Add(file);
                    }
                }

                CurrentProject!.WireParentReferences();
                project.WireParentReferences();
                CurrentProject!.RefreshHasChildren();
                CurrentProject!.SetFiletypeList();
                project.RefreshHasChildren();
                project.SetFiletypeList();
                UpdateFilter();
                SignalTreeViewUpdate();
                MarkDirty();
            }


            public void WatermarkFiles(string text = "Arbetskopia")
            {
                if (CurrentFile == null || !CurrentFile.IsValidPdf())
                {
                    return;
                }

                string date = DateTime.Today.ToString("yyyy-MM-dd");
                string? folder = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                if (string.IsNullOrEmpty(folder)) return;
                string outputPath = folder + "\\" + text + " " + date;

                System.IO.Directory.CreateDirectory(outputPath);

                foreach (FileData file in CurrentFiles)
                {

                    if (file.IsValidPdf())
                    {
                        string outputFilePath = outputPath + "\\" + file.Namn + "_" + text + "_" + date + ".pdf";

                        if (!IsFileInUse(outputFilePath))
                        {
                            PdfDocument pdfDoc = new PdfDocument(new PdfReader(file.Sökväg), new PdfWriter(outputFilePath));

                            PdfFont font = PdfFontFactory.CreateFont(FontProgramFactory.CreateFont(StandardFonts.HELVETICA));
                            Document document = new Document(pdfDoc);
                            iText.Kernel.Geom.Rectangle pageSize;

                            PdfCanvas canvas;
                            int n = pdfDoc.GetNumberOfPages();
                            for (int i = 1; i <= n; i++)
                            {
                                PdfPage page = pdfDoc.GetPage(i);
                                page.NewContentStreamBefore();
                                pageSize = page.GetPageSize();
                                float fontSize = pageSize.GetWidth() / 10;

                                canvas = new PdfCanvas(page);

                                Paragraph paragraph = new Paragraph(text).SetFont(font).SetFontSize(fontSize).SetFontColor(ColorConstants.GRAY).SetOpacity(0.5f);
                                paragraph.SetMultipliedLeading(0.5f);
                                paragraph.Add(Environment.NewLine);
                                paragraph.Add(new Paragraph(date).SetFont(font).SetFontSize(fontSize / 2).SetFontColor(ColorConstants.GRAY).SetOpacity(0.5f));

                                iText.Layout.Canvas canvasWatermark2 = new iText.Layout.Canvas(canvas, pdfDoc.GetDefaultPageSize()).ShowTextAligned(paragraph, pageSize.GetWidth() / 2, pageSize.GetHeight() / 2, 1, TextAlignment.CENTER, VerticalAlignment.MIDDLE, 120);
                            }
                            pdfDoc.Close();
                        }
                    }
                }
            }

            public static bool IsFileInUse(string filePath)
            {
                if (System.IO.File.Exists(filePath) == false)
                {
                    return false;
                }
                else
                {
                    try
                    {
                        // Try opening the file with read-write access and an exclusive lock
                        using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            // If we can open it, the file isn't in use
                        }
                    }
                    catch (IOException)
                    {
                        // IOException indicates the file is in use
                        return true;
                    }

                    // If no exception was thrown, the file is not in use
                    return false;
                }
            }
        }
    }
