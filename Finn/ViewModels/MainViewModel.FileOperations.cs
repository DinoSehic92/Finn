using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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
                string defaultCategory = (Type != null && Type != ALL_TYPES) ? Type : null;

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
                    dialog.SetFiles(candidatePaths, skippedCount, CurrentProject.AllowedTypes, defaultCategory);
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
                                Uppdrag = CurrentProject.Namn,
                                Sökväg = path
                            });
                        }

                        if (newFiles.Count > 0)
                            CurrentProject.AddFiles(newFiles);
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

            public void SetGroup(string group)
            {
                SetGroups(group);
                GetGroups();
            }

            public void CopyFilenameToClipboard(Avalonia.Visual window)
            {
                var text = string.Join(Environment.NewLine, CurrentFiles.Select(f => f.Namn));
                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(text);
            }

            public void CopyFilepathToClipboard(Avalonia.Visual window)
            {
                var text = string.Join(Environment.NewLine, CurrentFiles.Select(f => f.Sökväg));
                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(text);
            }

            public void CopyListviewToClipboard(Avalonia.Visual window)
            {
                var sb = new StringBuilder();

                foreach (FileData file in CurrentFiles)
                {
                    if (CurrentProject.Meta_1 == true) { sb.Append(file.Namn).Append('\t'); }
                    if (CurrentProject.Meta_2 == true) { sb.Append(file.Filtyp).Append('\t'); }
                    if (CurrentProject.Meta_3 == true) { sb.Append(file.Uppdrag).Append('\t'); }
                    if (CurrentProject.Meta_4 == true) { sb.Append(file.Tagg).Append('\t'); }
                    if (CurrentProject.Meta_5 == true) { sb.Append(file.Färg).Append('\t'); }
                    if (CurrentProject.Meta_6 == true) { sb.Append(file.Handling).Append('\t'); }
                    if (CurrentProject.Meta_7 == true) { sb.Append(file.Status).Append('\t'); }
                    if (CurrentProject.Meta_8 == true) { sb.Append(file.Datum).Append('\t'); }
                    if (CurrentProject.Meta_9 == true) { sb.Append(file.Ritningstyp).Append('\t'); }
                    if (CurrentProject.Meta_10 == true) { sb.Append(file.Beskrivning1).Append('\t'); }
                    if (CurrentProject.Meta_11 == true) { sb.Append(file.Beskrivning2).Append('\t'); }
                    if (CurrentProject.Meta_12 == true) { sb.Append(file.Beskrivning3).Append('\t'); }
                    if (CurrentProject.Meta_13 == true) { sb.Append(file.Beskrivning4).Append('\t'); }
                    if (CurrentProject.Meta_14 == true) { sb.Append(file.Revidering).Append('\t'); }
                    if (CurrentProject.Meta_15 == true) { sb.Append(file.Sökväg).Append('\t'); }

                    sb.AppendLine();
                }
                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(sb.ToString());
            }

            public void CheckSingleFile()
            {
                if (CurrentFile != null)
                    CurrentFile.IsFileMissing = !CurrentFile.IsValidPdf();
            }

            public async Task CheckProjectFiles()
            {
                await Task.Run(() => CheckFileAsync());
            }

            public async Task CheckFileAsync()
            {
                ClearFileStatus();

                int n = CurrentProject.StoredFiles.Count;
                int i = 0;

                foreach (FileData file in CurrentProject.StoredFiles)
                {
                    i++;
                    file.IsFileMissing = !file.IsValidPdf();
                    PreviewVM.Progress = (int)(100 * ((float)i / (float)n));
                }
            }

            public void ClearFileStatus()
            {
                foreach (FileData file in CurrentProject.StoredFiles)
                    file.IsFileMissing = false;
            }

            public void OpenFile()
            {
                try
                {
                    foreach (FileData file in CurrentFiles)
                    {
                        ProcessStartInfo psi = new()
                        {
                            FileName = file.Sökväg,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                }
                catch (Exception e) { Debug.WriteLine(e); }
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
                catch (Exception e) { Debug.WriteLine(e); }
            }

            public void OpenMeta()
            {
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
                catch { }
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
                    string folderpath = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void OpenPathDirect(string filepath)
            {
                try
                {
                    string folderpath = System.IO.Path.GetDirectoryName(filepath);
                    Process process = Process.Start("explorer.exe", "\"" + folderpath + "\"");
                }
                catch (Exception) { }
            }

            public void AddColor(string color)
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = color;
                }
                MarkDirty();
            }

            public void ClearAll()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = "";
                    file.Tagg = "";
                }
                MarkDirty();
            }

            public void AddTag(string tag)
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = tag;
                }
                MarkDirty();
            }

            public void ClearTag()
            {
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
                if (CurrentFile != null && !CurrentProject.StoredFiles.Any(x =>
                    string.Equals(x.Sökväg, filepath, StringComparison.OrdinalIgnoreCase)))
                {
                    var appended = new FileData()
                    {
                        Namn = System.IO.Path.GetFileNameWithoutExtension(filepath),
                        Sökväg = filepath,
                        IsFromFolder = fromFolder,
                        Uppdrag = CurrentFile.Uppdrag,
                        Filtyp = CurrentFile.Filtyp,
                        ParentNamn = CurrentFile.Namn,
                        ParentFile = CurrentFile
                    };

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
                if (owner != null && !owner.OtherFiles.Any(x => x.Filepath == filepath))
                {
                    OtherData newFile = new() { Filepath = filepath };
                    newFile.SetFile();

                    owner.OtherFiles.Add(newFile);
                    SortOtherFiles();
                    MarkDirty();
                }
            }


            public void RemoveAttachedFile(IList<FileData> files)
            {
                foreach (FileData file in files)
                {
                    file.PartOfCollections.Clear();
                    file.ParentNamn = string.Empty;
                    file.ParentFile = null;
                    CurrentProject.StoredFiles.Remove(file);
                }

                CurrentProject.RefreshHasChildren();
                UpdateFilter();
                Collections.SetCollectionContent();
                MarkDirty();
            }

            public void RemoveOtherFile(OtherData file)
            {
                if (file != null)
                {
                    OtherFilesOwner?.OtherFiles.Remove(file);
                    SortOtherFiles();
                    MarkDirty();
                }
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
            private void UpdateFileLinks(string oldName, string newName, string? newPath)
            {
                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return;

                // Update children that point back to this file by name
                foreach (var child in CurrentProject.StoredFiles)
                {
                    if (string.Equals(child.ParentNamn, oldName, StringComparison.OrdinalIgnoreCase))
                        child.ParentNamn = newName;
                }

                // Update folder entries attached to this file
                foreach (var folder in CurrentProject.Folders)
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

                string oldName = file.Namn;
                if (string.Equals(oldName, newName, StringComparison.Ordinal))
                    return;

                file.Namn = newName;
                UpdateFileLinks(oldName, newName, file.Sökväg);
            }

            public void RenameOriginal(string newName)
            {
                string oldName = CurrentFile.Namn;
                string oldPath = CurrentFile.Sökväg;

                if (oldName != newName && newName.Length > 0 && CurrentFile.IsLocal())
                {
                    string newPath = CurrentFile.Sökväg.Replace(oldName, newName);
                    try
                    {
                        System.IO.File.Move(oldPath, newPath);
                    }
                    catch
                    {
                        return;
                    }

                    CurrentFile.Sökväg = newPath;
                    CurrentFile.Namn = newName;
                    UpdateFileLinks(oldName, newName, newPath);
                    MarkDirty();
                }
            }

            public void ReplaceFilePath(string newPath, bool fileExists)
            {
                if (CurrentFile == null || string.IsNullOrWhiteSpace(newPath))
                    return;

                string oldName = CurrentFile.Namn;
                string newName = System.IO.Path.GetFileNameWithoutExtension(newPath);
                CurrentFile.Sökväg = newPath;
                CurrentFile.Namn = newName;
                CurrentFile.IsFileMissing = !fileExists;
                UpdateFileLinks(oldName, newName, newPath);
                MarkDirty();
            }


            public void MoveSelectedFiles(ProjectData project)
            {
                if (project != null)
                {
                    foreach (FileData file in CurrentFiles.ToList())
                    {
                        // Skip appended files — they move with their parent
                        if (file.IsAppendedFile) continue;

                        if (!project.StoredFiles.Contains(file))
                        {
                            // Move children along with the parent
                            var children = CurrentProject.StoredFiles
                                .Where(x => x.ParentNamn == file.Namn).ToList();
                            foreach (var child in children)
                            {
                                CurrentProject.StoredFiles.Remove(child);
                                child.Uppdrag = project.Namn;
                                project.StoredFiles.Add(child);
                            }

                            CurrentProject.StoredFiles.Remove(file);
                            file.Filtyp = "New";
                            file.Uppdrag = project.Namn;
                            project.StoredFiles.Add(file);
                        }
                    }

                    CurrentProject.RefreshHasChildren();
                    project.RefreshHasChildren();
                    UpdateFilter();
                    MarkDirty();
                }
            }


            public void WatermarkFiles(string text = "Arbetskopia")
            {

                if (CurrentFile.IsValidPdf() == false)
                {
                    return;
                }

                string date = DateTime.Today.ToString("yyyy-MM-dd");
                string folder = System.IO.Path.GetDirectoryName(CurrentFile.Sökväg);
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
