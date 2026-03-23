using Finn.Model;
using Finn.Storage;
using Microsoft.Extensions.Logging;
using MuPDFCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Finn.ViewModels
{
    /// <summary>
    /// Manages file data operations: content indexing, thumbnails, and metadata.
    /// Extracted from MainViewModel to reduce partial-class coupling.
    /// </summary>
    public class DataViewModel : ViewModelBase
    {
        private readonly Func<ProjectStorage> _storageGetter;
        private readonly Func<IList<FileData>> _currentFilesGetter;
        private readonly Func<ObservableCollection<FileData>> _filteredFilesGetter;
        private readonly Action? _markDirty;
        private readonly string _savePath;

        public DataViewModel(
            Func<ProjectStorage> storageGetter,
            Func<IList<FileData>> currentFilesGetter,
            Func<ObservableCollection<FileData>> filteredFilesGetter,
            string savePath,
            Action? markDirty = null,
            ILogger? logger = null)
            : base(logger)
        {
            _storageGetter = storageGetter;
            _currentFilesGetter = currentFilesGetter;
            _filteredFilesGetter = filteredFilesGetter;
            _savePath = savePath;
            _markDirty = markDirty;
        }

        private ProjectStorage Storage => _storageGetter();
        private IList<FileData> CurrentFiles => _currentFilesGetter();
        private ObservableCollection<FileData> FilteredFiles => _filteredFilesGetter();

        #region State

        private ObservableCollection<ContentData>? textContent;
        public ObservableCollection<ContentData>? TextContent
        {
            get => textContent;
            set { textContent = value; OnPropertyChanged(nameof(TextContent)); }
        }

        private ContentData? selectedTextContent;
        public ContentData? SelectedTextContent
        {
            get => selectedTextContent;
            set { selectedTextContent = value; OnPropertyChanged(nameof(SelectedTextContent)); }
        }

        public List<string[]> MetaStore { get; } = new();
        public List<string> PathStore { get; } = new();

        #endregion

        #region Thumbnails

        public void GetThumbnails()
        {
            string thumbnailPath = Path.Combine(_savePath, "Thumbnails") + Path.DirectorySeparatorChar;
            if (!Directory.Exists(thumbnailPath))
            {
                Directory.CreateDirectory(thumbnailPath);
            }

            foreach (FileData file in CurrentFiles)
            {
                if (file.IsValidPdf())
                {
                    file.RemoveThumbnail();

                    byte[] bytes = File.ReadAllBytes(file.Sökväg);
                    using var ctx = new MuPDFContext(1);
                    MuPDFDocument fileDocument = new(ctx, bytes, InputFileTypes.PDF);

                    file.ThumbnailSource = $"{thumbnailPath}{file.Namn}.jpeg";
                    fileDocument.SaveImageAsJPEG(0, 1, file.ThumbnailSource, 20);

                    fileDocument.Dispose();
                }
            }
            _markDirty?.Invoke();
        }

        public void GenerateThumbnail(FileData file, string thumbnailDir)
        {
            try
            {
                if (!file.IsValidPdf())
                    return;

                if (!Directory.Exists(thumbnailDir))
                    Directory.CreateDirectory(thumbnailDir);

                file.RemoveThumbnail();

                byte[] bytes = File.ReadAllBytes(file.Sökväg);
                using var ctx = new MuPDFContext(1);
                using var doc = new MuPDFDocument(ctx, bytes, InputFileTypes.PDF);

                string target = Path.Combine(thumbnailDir, file.Namn + ".jpeg");
                file.ThumbnailSource = target;
                doc.SaveImageAsJPEG(0, 1, target, 20);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to generate thumbnail for {Path}", file?.Sökväg);
            }
        }

        public void ClearThumbnails()
        {
            foreach (FileData file in CurrentFiles)
            {
                file.RemoveThumbnail();
            }
            _markDirty?.Invoke();
        }

        public void SyncThumbnails()
        {
            string thumbnailDir = Path.Combine(_savePath, "Thumbnails");

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

        #endregion

        #region Content Indexing

        public async Task GetContentAsync(IProgress<int>? progress = null, IProgress<string>? statusProgress = null, CancellationToken cancellationToken = default)
        {
            string indexPath = Path.Combine(_savePath, "Content.json");

            statusProgress?.Report("Loading index…");

            if (File.Exists(indexPath))
            {
                await LoadIndexFileAsync(indexPath);
            }

            TextContent ??= new ObservableCollection<ContentData>();

            // Build a set of already-indexed paths so we can skip them
            var alreadyIndexed = new HashSet<string>(
                TextContent.Where(c => !string.IsNullOrEmpty(c.PlainText)).Select(c => c.Filepath),
                StringComparer.OrdinalIgnoreCase);

            var files = CurrentFiles?.ToList() ?? new List<FileData>();
            var results = new List<ContentData>();

            // ── Main extraction loop (background thread) ──
            await Task.Run(() =>
            {
                int total = files.Count;

                // Use the default store size (256 MiB) so MuPDF can cache fonts
                // and CMaps across documents. The previous store of 1 byte meant
                // every cache operation triggered a full eviction scan of all
                // tracked objects — by file 8-9 this scan dominated runtime.
                using var ctx = new MuPDFContext();

                for (int i = 0; i < total; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var file = files[i];
                    statusProgress?.Report($"{file.Namn}  ({i + 1}/{total})");

                    // Use checkExists: false to avoid costly File.Exists() calls
                    // on network paths (SMB timeout can block 30s per unreachable path).
                    // The alreadyIndexed check and the catch block handle missing files.
                    foreach (var path in file.AllPdfPaths(checkExists: false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Skip files that are already indexed
                        if (alreadyIndexed.Contains(path))
                            continue;

                        try
                        {
                            // Open the file by path so MuPDF uses native file I/O
                            // (memory-mapping) instead of reading the entire PDF
                            // into a managed byte[] and pinning it.
                            using var fileDocument = new MuPDFDocument(ctx, path);
                            var content = new ContentData
                            {
                                Name = file.Namn,
                                Filepath = path,
                                // includeAnnotations: false — we only need document
                                // text, not annotation text. Skipping annotations
                                // avoids building annotation display lists which are
                                // very expensive for reviewed engineering PDFs.
                                PlainText = fileDocument.ExtractText(includeAnnotations: false)
                            };

                            results.Add(content);

                            // Free display lists for all pages now that text has
                            // been extracted. Without this, every page's display
                            // list stays alive until Dispose and the context store
                            // grows unboundedly across documents.
                            fileDocument.ClearCache();
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception)
                        {
                            // Ignore individual failures (missing files, corrupt PDFs, etc.)
                        }
                    }

                    int percent = (i + 1) * 100 / Math.Max(1, total);
                    progress?.Report(percent);
                }
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            statusProgress?.Report("Saving index…");

            // ── Post-processing (background thread to avoid blocking UI) ──
            await Task.Run(() =>
            {
                // Build the final content set in a dictionary keyed by path.
                // This replaces individual Remove(O(n)) + Add + notification
                // calls on the ObservableCollection with a single bulk swap.
                var merged = new Dictionary<string, ContentData>(StringComparer.OrdinalIgnoreCase);

                foreach (var c in TextContent)
                {
                    if (!string.IsNullOrEmpty(c.PlainText))
                        merged[c.Filepath] = c;
                }

                foreach (var content in results)
                {
                    if (!string.IsNullOrEmpty(content.PlainText))
                        merged[content.Filepath] = content;
                }

                // Single assignment – one CollectionChanged notification
                TextContent = new ObservableCollection<ContentData>(merged.Values);

                // Sync HasPlainText flag on every file
                var indexedPaths = new HashSet<string>(merged.Keys, StringComparer.OrdinalIgnoreCase);

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        var paths = file.AllPdfPaths(checkExists: false);
                        file.HasPlainText = paths.Any(indexedPaths.Contains);
                    }
                }

                SaveIndexFile(indexPath);
            }, cancellationToken);

            _markDirty?.Invoke();
        }

        public void ClearIndexedContent()
        {
            string indexPath = Path.Combine(_savePath, "Content.json");

            foreach (FileData file in CurrentFiles)
            {
                file.HasPlainText = false;

                var paths = file.AllPdfPaths();
                var toRemove = TextContent?.Where(x => paths.Contains(x.Filepath)).ToList();
                if (toRemove != null)
                {
                    foreach (var content in toRemove)
                        TextContent!.Remove(content);
                }
            }

            SaveIndexFile(indexPath);
            _markDirty?.Invoke();
        }

        public void SyncPlainText()
        {
            string indexPath = Path.Combine(_savePath, "Content.json");

            var indexedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (File.Exists(indexPath))
            {
                try
                {
                    using var stream = File.OpenRead(indexPath);
                    using var reader = new StreamReader(stream);
                    using var jsonReader = new JsonTextReader(reader);
                    var serializer = JsonSerializer.CreateDefault();
                    var content = serializer.Deserialize<ObservableCollection<ContentData>>(jsonReader);
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

        public async Task LoadIndexFileAsync(string indexPath)
        {
            // Stream-deserialize so the raw JSON string is never held in memory.
            var content = await Task.Run(() =>
            {
                using var stream = File.OpenRead(indexPath);
                using var reader = new StreamReader(stream);
                using var jsonReader = new JsonTextReader(reader);
                var serializer = JsonSerializer.CreateDefault();
                return serializer.Deserialize<ObservableCollection<ContentData>>(jsonReader);
            });

            TextContent = content;

            // Sync HasPlainText flags on a background thread to avoid blocking UI
            // with File.Exists calls inside AllPdfPaths
            var indexedFiles = new HashSet<string>(
                TextContent!.Select(c => c.Filepath),
                StringComparer.OrdinalIgnoreCase);

            var storage = Storage;
            await Task.Run(() =>
            {
                foreach (ProjectData project in storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        var paths = file.AllPdfPaths(checkExists: false);
                        file.HasPlainText = paths.Any(indexedFiles.Contains);
                    }
                }
            });
        }

        private void SaveIndexFile(string indexPath)
        {
            if (!Directory.Exists(_savePath))
            {
                Directory.CreateDirectory(_savePath);
            }

            // Stream-serialize directly to disk so the entire JSON is never
            // materialised as a single managed string (can be 100s of MB).
            using var streamWriter = new StreamWriter(indexPath);
            using var jsonWriter = new JsonTextWriter(streamWriter);
            var serializer = JsonSerializer.CreateDefault();
            serializer.Serialize(jsonWriter, TextContent);
        }

        #endregion

        #region Metadata

        public void SelectFilesForMetaworker(bool singleMode)
        {
            MetaStore.Clear();
            PathStore.Clear();

            if (singleMode)
            {
                foreach (FileData file in CurrentFiles) { PathStore.Add(file.Sökväg); }
            }
            else
            {
                foreach (FileData file in FilteredFiles) { PathStore.Add(file.Sökväg); }
            }
        }

        public int GetNrSelectedFiles()
        {
            return PathStore.Count;
        }

        public void GetMetadata(int k)
        {
            string[] tags = ["Handlingstyp = ", "Granskningsstatus = ", "Datum = ", "Ritningstyp = ", "Beskrivning1 = ", "Beskrivning2 = ", "Beskrivning3 = ", "Beskrivning4 = ", "Revidering = "];
            int ntags = tags.Length;

            string path = PathStore[k];
            string[] description = new string[ntags];
            try
            {
                string[] lines = File.ReadAllLines(path + ".md", Encoding.GetEncoding("ISO-8859-1"));

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

        public void SetMeta()
        {
            int i = 0;
            foreach (string path in PathStore)
            {
                FileData? file = FilteredFiles.FirstOrDefault(x => x.Sökväg == path);
                if (file == null) { i++; continue; }

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
            _markDirty?.Invoke();
        }

        public void ClearMeta()
        {
            ClearSelectedMetadata();
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
            _markDirty?.Invoke();
        }

        #endregion
    }
}
