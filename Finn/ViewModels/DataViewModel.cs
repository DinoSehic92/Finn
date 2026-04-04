using Finn.Converters;
using Finn.Model;
using Finn.Storage;
using Finn.Utils;
using Microsoft.Extensions.Logging;
using MuPDFCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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

        /// <summary>
        /// Generates a stable, filesystem-safe thumbnail filename from the
        /// file's full path so that two files with the same display name
        /// (but different locations) never collide.
        /// </summary>
        private static string GetThumbnailFileName(FileData file)
        {
            // Use a deterministic hash of the full path for uniqueness, prefixed with
            // the display name for human readability when browsing the folder.
            string safe = string.Concat(file.Namn.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
            if (safe.Length > 60) safe = safe[..60];
            uint hash = StableHash(file.Sökväg);
            return $"{safe}_{hash:X8}.jpeg";
        }

        /// <summary>
        /// FNV-1a hash that is deterministic across process restarts
        /// (unlike string.GetHashCode which is randomized in .NET Core+).
        /// </summary>
        private static uint StableHash(string input)
        {
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash ^= char.ToUpperInvariant(c);
                hash *= 16777619;
            }
            return hash;
        }

        /// <summary>
        /// Generates thumbnails for all current files. Designed to be called
        /// from a background thread via <see cref="Task.Run"/>.
        /// </summary>
        public async Task GenerateThumbnailsAsync(
            IProgress<int>? progress = null,
            CancellationToken ct = default)
        {
            string thumbnailDir = Path.Combine(_savePath, "Thumbnails");
            Directory.CreateDirectory(thumbnailDir);

            // Snapshot the file list so the background work is safe.
            var files = CurrentFiles?.ToList() ?? [];
            int total = files.Count;

            await Task.Run(() =>
            {
                using var ctx = new MuPDFContext();
                for (int i = 0; i < total; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    GenerateSingleThumbnail(files[i], thumbnailDir, ctx);
                    progress?.Report((i + 1) * 100 / Math.Max(1, total));
                }
            }, ct);

            _markDirty?.Invoke();
        }

        /// <summary>
        /// Generates a thumbnail for a single file. Safe to call from any thread;
        /// the <see cref="FileData.ThumbnailSource"/> property is only set after
        /// the image file has been fully written (write-to-temp then rename).
        /// </summary>
        public void GenerateSingleThumbnail(FileData file, string thumbnailDir, MuPDFContext ctx)
        {
            try
            {
                if (!file.IsValidPdf())
                    return;

                string target = Path.Combine(thumbnailDir, GetThumbnailFileName(file));
                string temp = target + ".tmp";

                using (var doc = new MuPDFDocument(ctx, file.Sökväg))
                {
                    doc.SaveImageAsJPEG(0, 1, temp, 20);
                }

                // Evict the old cached bitmap so the UI picks up the new image.
                BitmapConverter.Evict(target);

                // Atomic-ish replace: delete old file, rename temp → target.
                // This prevents the UI from reading a partially-written JPEG.
                try { if (File.Exists(target)) File.Delete(target); } catch { /* best effort */ }
                File.Move(temp, target);

                // Only set the property after the file is complete on disk.
                file.ThumbnailSource = target;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Failed to generate thumbnail for {Path}", file?.Sökväg);
            }
        }

        public void ClearThumbnails()
        {
            BitmapConverter.ClearCache();
            foreach (FileData file in CurrentFiles)
            {
                file.RemoveThumbnail();
            }
            _markDirty?.Invoke();
        }

        /// <summary>
        /// Re-links existing thumbnail files on disk to their corresponding
        /// <see cref="FileData"/> objects. Uses the stable filename scheme.
        /// </summary>
        public void SyncThumbnails()
        {
            string thumbnailDir = Path.Combine(_savePath, "Thumbnails");

            foreach (var project in Storage.StoredProjects)
            {
                foreach (var file in project.StoredFiles)
                {
                    if (string.IsNullOrEmpty(file.Sökväg))
                    {
                        file.ThumbnailSource = string.Empty;
                        continue;
                    }

                    string expected = Path.Combine(thumbnailDir, GetThumbnailFileName(file));
                    file.ThumbnailSource = File.Exists(expected) ? expected : string.Empty;
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
                    var json = File.ReadAllText(indexPath);
                    var content = JsonHelper.Deserialize<ObservableCollection<ContentData>>(json);
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
                var json = File.ReadAllText(indexPath);
                return JsonHelper.Deserialize<ObservableCollection<ContentData>>(json);
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
            using var stream = new FileStream(indexPath, FileMode.Create, FileAccess.Write, FileShare.None);
            System.Text.Json.JsonSerializer.Serialize(stream, TextContent, JsonHelper.Options);
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
