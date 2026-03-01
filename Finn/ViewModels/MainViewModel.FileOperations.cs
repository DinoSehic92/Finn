using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
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

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            public async Task AddFile(Avalonia.Visual window)
            {
                if (CurrentProject != null)
                {
                    var topLevel = TopLevel.GetTopLevel(window);
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Add File",
                        FileTypeFilter = new[] { FilePickerFileTypes.Pdf },
                        AllowMultiple = true
                    });

                    foreach (var file in files)
                    {
                        string path = file.Path.LocalPath;
                        CurrentProject.Newfile(path);
                        SetDefaultType();
                    }
                }
            }

            // Theme/resource updates handled by UIService

            public void AddFilesDrag(string path)
            {
                CurrentProject.Newfile(path);
                SetDefaultType();
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
                string store = string.Empty;

                foreach (FileData file in CurrentFiles)
                {
                    store += file.Namn + Environment.NewLine;
                }

                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(store);
            }

            public void CopyFilepathToClipboard(Avalonia.Visual window)
            {
                string store = string.Empty;

                foreach (FileData file in CurrentFiles)
                {
                    store += file.Sökväg + Environment.NewLine;
                }

                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(store);
            }

            public void CopyListviewToClipboard(Avalonia.Visual window)
            {
                string store = string.Empty;

                foreach (FileData file in CurrentFiles)
                {
                    if (CurrentProject.Meta_1 == true) { store += file.Namn + "\t"; }
                    if (CurrentProject.Meta_2 == true) { store += file.Filtyp + "\t"; }
                    if (CurrentProject.Meta_3 == true) { store += file.Uppdrag + "\t"; }
                    if (CurrentProject.Meta_4 == true) { store += file.Tagg + "\t"; }
                    if (CurrentProject.Meta_5 == true) { store += file.Färg + "\t"; }
                    if (CurrentProject.Meta_6 == true) { store += file.Handling + "\t"; }
                    if (CurrentProject.Meta_7 == true) { store += file.Status + "\t"; }
                    if (CurrentProject.Meta_8 == true) { store += file.Datum + "\t"; }
                    if (CurrentProject.Meta_9 == true) { store += file.Ritningstyp + "\t"; }
                    if (CurrentProject.Meta_10 == true) { store += file.Beskrivning1 + "\t"; }
                    if (CurrentProject.Meta_11 == true) { store += file.Beskrivning2 + "\t"; }
                    if (CurrentProject.Meta_12 == true) { store += file.Beskrivning3 + "\t"; }
                    if (CurrentProject.Meta_13 == true) { store += file.Beskrivning4 + "\t"; }
                    if (CurrentProject.Meta_14 == true) { store += file.Revidering + "\t"; }
                    if (CurrentProject.Meta_15 == true) { store += file.Sökväg + "\t"; }

                    store += Environment.NewLine;
                }
                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(store);
            }

            public void SelectFilesForMetaworker(bool singleMode)
            {
                MetaStore.Clear();
                PathStore.Clear();

                if (singleMode == true)
                {
                    foreach (FileData file in CurrentFiles) { PathStore.Add((file.Sökväg)); }
                }
                if (singleMode == false)
                {
                    foreach (FileData file in FilteredFiles) { PathStore.Add((file.Sökväg)); }
                }
            }

            public int GetNrSelectedFiles()
            {
                return PathStore.Count;
            }

            public void SetMeta()
            {
                int i = 0;
                foreach (string path in PathStore)
                {
                    FileData file = FilteredFiles.FirstOrDefault(x => x.Sökväg == path);

                    string[] md = MetaStore[i];

                    file.Handling = md[0];
                    file.Status = md[1];
                    file.Datum = md[2];
                    file.Ritningstyp = md[3];
                    file.Beskrivning1 = md[4];
                    file.Beskrivning2 = md[5];
                    file.Beskrivning3 = md[6];
                    file.Beskrivning4 = md[7];
                    file.Revidering = md[8];
                    file.Sökväg = path;

                    i++;
                }
            }

            public void GetMetadata(int k)
            {
                string[] tags = ["Handlingstyp = ", "Granskningsstatus = ", "Datum = ", "Ritningstyp = ", "Beskrivning1 = ", "Beskrivning2 = ", "Beskrivning3 = ", "Beskrivning4 = ", "Revidering = "];
                int ntags = tags.Length;

                string path = PathStore[k];
                string[] description = new string[ntags];
                try
                {
                    string[] lines = System.IO.File.ReadAllLines(path + ".md", Encoding.GetEncoding("ISO-8859-1"));

                    int iter = 1;
                    int start = 100;
                    int end = 0;
                    foreach (string line in lines)
                    {
                        if (line == "[Metadata]") { start = iter; }
                        if (line.Trim().Length == 0 || iter > start) { end = iter; }
                        iter++;
                    }

                    for (int i = start; i < end; i++)
                    {
                        string line = lines[i];
                        for (int j = 0; j < ntags; j++)
                        {
                            string tag = tags[j];
                            if (line.StartsWith(tag))
                            {
                                description[j] = line.Replace(tag, "");
                            }
                            if (line.StartsWith(tag.ToUpper()))
                            {
                                description[j] = line.Replace(tag.ToUpper(), "");
                            }
                        }
                    }
                    MetaStore.Add(description);
                }
                catch (Exception)
                {
                    MetaStore.Add(["", "", "", "", "", "", "", "", ""]);
                }
            }

            public void ClearMeta()
            {
                ClearSelectedMetadata();
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
            }

            public void ClearAll()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Färg = "";
                    file.Tagg = "";
                }
            }

            public void AddTag(string tag)
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = tag;
                }
            }

            public void ClearTag()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Tagg = "";
                }
            }

            public void EditType(string type)
            {
                SetTypeSelected(type);
            }

            public void ClearSelectedMetadata()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Handling = "";
                    file.Status = "";
                    file.Datum = "";
                    file.Ritningstyp = "";
                    file.Beskrivning1 = "";
                    file.Beskrivning2 = "";
                    file.Beskrivning3 = "";
                    file.Beskrivning4 = "";
                    file.Revidering = "";
                }
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
                }
            }


            public void RemoveAttachedFile(IList<FileData> files)
            {
                foreach (FileData file in files)
                {
                    CurrentFile.AppendedFiles.Remove(file);
                }

                SortAttachedFiles();
            }

            public void RemoveOtherFile(OtherData file)
            {
                if (file != null)
                {
                    CurrentFile.OtherFiles.Remove(file);
                    SortOtherFiles();
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
