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

            #endregion

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
                    files.Select(f => f.Path.LocalPath), mainWindow);
            }

            /// <summary>
            /// Adds files to the current project. Files that match an existing entry
            /// by name are collected and presented in a version-import dialog so the
            /// user can choose the label before they are registered as versions.
            /// </summary>
            public async Task AddFilesWithVersionCheck(IEnumerable<string> paths, Window? mainWindow)
            {
                var newPaths = new List<string>();
                var versionCandidates = new List<VersionImportEntry>();

                foreach (string path in paths)
                {
                    string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

                    if (CurrentProject.StoredFiles.Any(x => x.Sökväg == path))
                        continue;

                    var existing = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == fileName);
                    if (existing != null)
                    {
                        versionCandidates.Add(new VersionImportEntry
                        {
                            ExistingFile = existing,
                            NewFilePath = path
                        });
                    }
                    else
                    {
                        newPaths.Add(path);
                    }
                }

                foreach (var path in newPaths)
                    CurrentProject.Newfile(path);

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

                if (newPaths.Count > 0 || versionsAdded)
                {
                    SetDefaultType();
                    MarkDirty();
                }
            }

            // Theme/resource updates handled by UIService

            public void AddFilesDrag(string path)
            {
                CurrentProject.Newfile(path);
                SetDefaultType();
                MarkDirty();
            }

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
                if (CurrentFile != null && !CurrentFile.AppendedFiles.Any(x => x.Sökväg == filepath))
                {
                    CurrentFile.AppendedFiles.Add(new FileData()
                    {
                        Namn = System.IO.Path.GetFileNameWithoutExtension(filepath),
                        Sökväg = filepath,
                        IsFromFolder = fromFolder
                    });

                    SortAttachedFiles();
                    MarkDirty();
                }
            }

            public void AddOtherFile(string filepath)
            {
                if (CurrentFile != null && !CurrentFile.OtherFiles.Any(x => x.Filepath == filepath))
                {
                    OtherData newFile = new() { Filepath = filepath };
                    newFile.SetFile();

                    CurrentFile.OtherFiles.Add(newFile);
                    SortOtherFiles();
                    MarkDirty();
                }
            }


            public void RemoveAttachedFile(IList<FileData> files)
            {
                foreach (FileData file in files)
                {
                    CurrentFile.AppendedFiles.Remove(file);
                }

                SortAttachedFiles();
                MarkDirty();
            }

            public void RemoveOtherFile(OtherData file)
            {
                if (file != null)
                {
                    CurrentFile.OtherFiles.Remove(file);
                    SortOtherFiles();
                    MarkDirty();
                }
            }

            private void SortAttachedFiles()
            {
                if (CurrentFile != null)
                {
                    SortAttachedFilesDirect(CurrentFile);
                }
            }

            private void SortAttachedFilesDirect(FileData file)
            {
                List<FileData> tempList = file.AppendedFiles.OrderBy(x => x.Namn).ToList();
                file.AppendedFiles.Clear();
                file.AppendedFiles = new ObservableCollection<FileData>(tempList);
            }

            private void SortOtherFiles()
            {
                if (CurrentFile != null)
                {
                    SortOtherFilesDirect(CurrentFile);
                }
            }

            private void SortOtherFilesDirect(FileData file)
            {
                List<OtherData> tempList = file.OtherFiles.OrderBy(x => x.Name).ToList();
                file.OtherFiles.Clear();
                file.OtherFiles = new ObservableCollection<OtherData>(tempList);
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
                    MarkDirty();
                }
            }


            public void MoveSelectedFiles(ProjectData project)
            {
                if (project != null)
                {
                    foreach (FileData file in CurrentFiles.ToList())
                    {

                        if (!project.StoredFiles.Contains(file))
                        {
                            CurrentProject.StoredFiles.Remove(file);
                            file.Filtyp = "New";
                            file.Uppdrag = project.Namn;
                            project.StoredFiles.Add(file);
                        }
                    }

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
