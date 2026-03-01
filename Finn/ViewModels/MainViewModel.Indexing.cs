using Finn.Model;
using Microsoft.Extensions.Logging;
using MuPDFCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            public void GetThumbnails()
            {
                string thumbnailPath = $"{SavePath}\\Thumbnails\\";
                if (!Directory.Exists(thumbnailPath))
                {
                    Directory.CreateDirectory(thumbnailPath);
                }

                foreach (FileData file in CurrentFiles)
                {
                    if (file.IsValidPdf())
                    {
                        file.RemoveThumbnail();

                        byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);
                        MuPDFDocument fileDocument = new(new MuPDFContext(1), bytes, InputFileTypes.PDF);

                        file.ThumbnailSource = $"{thumbnailPath}{file.Namn}.jpeg";
                        fileDocument.SaveImageAsJPEG(0, 1, file.ThumbnailSource, 20);

                        fileDocument.Dispose();
                    }
                }
            }

            public async Task GetContentAsync(IProgress<int>? progress = null)
            {
                string indexPath = $"{SavePath}\\Content.json";

                if (System.IO.File.Exists(indexPath))
                {
                    LoadIndexFile(indexPath);
                }

                TextContent ??= new ObservableCollection<ContentData>();

                var files = CurrentFiles?.ToList() ?? new List<FileData>();
                var results = new List<ContentData>();

                await Task.Run(() =>
                {
                    int total = files.Count;
                    for (int i = 0; i < total; i++)
                    {
                        var file = files[i];
                        try
                        {
                            if (file.IsValidPdf())
                            {
                                byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);
                                using var fileDocument = new MuPDFDocument(new MuPDFContext(1), bytes, InputFileTypes.PDF);
                                var content = new ContentData
                                {
                                    Name = file.Namn,
                                    Filepath = file.Sökväg,
                                    PlainText = fileDocument.ExtractText()
                                };

                                lock (results)
                                {
                                    results.Add(content);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore individual failures and continue indexing other files
                        }

                        int percent = (i + 1) * 100 / Math.Max(1, total);
                        progress?.Report(percent);
                    }
                });

                // Update UI-bound collections on the calling (UI) thread after background processing
                foreach (ContentData content in results)
                {
                    if (content.PlainText != null && content.PlainText != string.Empty)
                    {
                        ContentData? existing = TextContent.FirstOrDefault(x => x.Filepath == content.Filepath);
                        if (existing != null)
                        {
                            TextContent.Remove(existing);
                        }
                        TextContent.Add(content);
                    }
                }

                // Remove any indexed entries that ended up with no extractable content
                foreach (ContentData empty in TextContent.Where(x => string.IsNullOrEmpty(x.PlainText)).ToList())
                {
                    TextContent.Remove(empty);
                }

                // Sync HasPlainText for every file: true only if content was actually extracted
                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {

                        file.HasPlainText = TextContent.Any(x => x.Filepath == file.Sökväg);
                    }
                }

                SaveIndexFile(indexPath);
            }

            public void ClearIndexedContent()
            {
                string indexPath = Path.Combine(SavePath, "Content.json");

                foreach (FileData file in CurrentFiles)
                {
                    file.HasPlainText = false;

                    if (TextContent?.Any(x => x.Filepath == file.Sökväg) == true)
                    {
                        ContentData contentToRemove = TextContent.FirstOrDefault(x => x.Filepath == file.Sökväg);
                        TextContent.Remove(contentToRemove);
                        SaveIndexFile(indexPath);
                    }
                }
            }

            private void LoadIndexFile(string indexPath)
            {
                using StreamReader streamReader = new(indexPath);
                string fileContent = streamReader.ReadToEnd();
                TextContent = JsonConvert.DeserializeObject<ObservableCollection<ContentData>>(fileContent);

                List<string> indexedFiles = new();

                foreach (ContentData content in TextContent)
                {
                    indexedFiles.Add(content.Filepath);
                }

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        file.HasPlainText = indexedFiles.Contains(file.Sökväg);
                    }
                }
            }

            private void SaveIndexFile(string indexPath)
            {
                if (!Directory.Exists(SavePath))
                {
                    Directory.CreateDirectory(SavePath);
                }

                using StreamWriter streamWriter = new(indexPath);
                var data = JsonConvert.SerializeObject(TextContent);
                streamWriter.WriteLine(data);
            }

            public void ClearThumbnails()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.RemoveThumbnail();
                }
            }

            /// <summary>
            /// Scans the Thumbnails folder and updates ThumbnailSource for every file across
            /// all projects: sets the path when a matching .jpeg exists, clears it when it doesn't.
            /// </summary>
            public void SyncThumbnails()
            {
                string thumbnailDir = Path.Combine(SavePath, "Thumbnails");

                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var file in project.StoredFiles)
                    {
                        string expected = Path.Combine(thumbnailDir, file.Namn + ".jpeg");
                        if (File.Exists(expected))
                            file.ThumbnailSource = expected;
                        else
                            file.ThumbnailSource = string.Empty;
                    }
                }
            }

            /// <summary>
            /// Reads IndexedContent.json and updates HasPlainText for every file across
            /// all projects: sets true when an entry with a matching filepath exists,
            /// false when it doesn't. Also refreshes the in-memory TextContent collection.
            /// </summary>
            public void SyncPlainText()
            {
                string indexPath = Path.Combine(SavePath, "Content.json");

                var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (File.Exists(indexPath))
                {
                    try
                    {
                        using StreamReader reader = new(indexPath);
                        string json = reader.ReadToEnd();
                        var content = JsonConvert.DeserializeObject<ObservableCollection<ContentData>>(json);
                        if (content != null)
                        {
                            TextContent = content;
                            foreach (ContentData entry in content)
                                indexedPaths.Add(entry.Filepath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Error reading index file during SyncPlainText");
                    }
                }

                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var file in project.StoredFiles)
                    {
                        file.HasPlainText = indexedPaths.Contains(file.Sökväg);
                    }
                }
            }

            /// <summary>
            /// Generate a thumbnail for a single file and save it to the thumbnail directory.
            /// This is extracted so callers can run per-file generation on background threads
            /// and report progress from the UI layer.
            /// </summary>
            public void GenerateThumbnail(FileData file, string thumbnailDir)
            {
                try
                {
                    if (!file.IsValidPdf())
                        return;

                    if (!Directory.Exists(thumbnailDir))
                        Directory.CreateDirectory(thumbnailDir);

                    file.RemoveThumbnail();

                    byte[] bytes = System.IO.File.ReadAllBytes(file.Sökväg);
                    using var doc = new MuPDFDocument(new MuPDFContext(1), bytes, InputFileTypes.PDF);

                    string target = Path.Combine(thumbnailDir, file.Namn + ".jpeg");
                    file.ThumbnailSource = target;
                    doc.SaveImageAsJPEG(0, 1, target, 20);
                }
                catch (Exception ex)
                {
                    // Log and continue — don't let one failure abort the whole batch
                    logger?.LogError(ex, "Failed to generate thumbnail for {Path}", file?.Sökväg);
                }
            }
        }
    }
