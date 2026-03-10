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
                    MuPDFDocument fileDocument = new(new MuPDFContext(1), bytes, InputFileTypes.PDF);

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
                using var doc = new MuPDFDocument(new MuPDFContext(1), bytes, InputFileTypes.PDF);

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

        public async Task GetContentAsync(IProgress<int>? progress = null)
        {
            string indexPath = Path.Combine(_savePath, "Content.json");

            if (File.Exists(indexPath))
            {
                await LoadIndexFileAsync(indexPath);
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
                    foreach (var path in file.AllPdfPaths())
                    {
                        try
                        {
                            byte[] bytes = File.ReadAllBytes(path);
                            using var fileDocument = new MuPDFDocument(new MuPDFContext(1), bytes, InputFileTypes.PDF);
                            var content = new ContentData
                            {
                                Name = file.Namn,
                                Filepath = path,
                                PlainText = fileDocument.ExtractText()
                            };

                            lock (results)
                            {
                                results.Add(content);
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore individual failures and continue indexing other files
                        }
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

            // Sync HasPlainText for every file: true if content was extracted for any version
            foreach (ProjectData project in Storage.StoredProjects)
            {
                foreach (FileData file in project.StoredFiles)
                {
                    var paths = file.AllPdfPaths();
                    file.HasPlainText = TextContent.Any(x => paths.Contains(x.Filepath));
                }
            }

            SaveIndexFile(indexPath);
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

        public async Task LoadIndexFileAsync(string indexPath)
        {
            string fileContent = await File.ReadAllTextAsync(indexPath);
            var content = await Task.Run(() =>
                JsonConvert.DeserializeObject<ObservableCollection<ContentData>>(fileContent));
            TextContent = content;

            var indexedFiles = new HashSet<string>(
                TextContent!.Select(c => c.Filepath),
                StringComparer.OrdinalIgnoreCase);

            foreach (ProjectData project in Storage.StoredProjects)
            {
                foreach (FileData file in project.StoredFiles)
                {
                    var paths = file.AllPdfPaths(checkExists: false);
                    file.HasPlainText = paths.Any(indexedFiles.Contains);
                }
            }
        }

        private void SaveIndexFile(string indexPath)
        {
            if (!Directory.Exists(_savePath))
            {
                Directory.CreateDirectory(_savePath);
            }

            using StreamWriter streamWriter = new(indexPath);
            var data = JsonConvert.SerializeObject(TextContent);
            streamWriter.WriteLine(data);
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
