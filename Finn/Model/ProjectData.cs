using Avalonia.Media;
using Finn.Utils;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Finn.Model
{
    /// <summary>
    /// Represents a project and its associated metadata, files, and folders.
    /// </summary>
    public class ProjectData : INotifyPropertyChanged
    {
        /// <summary>
        /// Gets or sets the collection of stored files for the project.
        /// In the flat model this includes both top-level files and appended files.
        /// </summary>
        public BulkObservableCollection<FileData> StoredFiles { get; set; } = new BulkObservableCollection<FileData>();

        /// <summary>
        /// Returns all files in the project.
        /// Equivalent to <see cref="StoredFiles"/> after migration.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<FileData> AllFiles => StoredFiles;

        /// <summary>
        /// Migrates legacy nested <see cref="FileData.AppendedFiles"/> into
        /// the flat <see cref="StoredFiles"/> list with <see cref="FileData.ParentNamn"/>
        /// set. Call once after deserialization. Safe to call multiple times.
        /// </summary>
        public void FlattenAppendedFiles()
        {
            var toAdd = new List<FileData>();

            foreach (var parent in StoredFiles.ToList())
            {
                if (parent.AppendedFiles.Count == 0) continue;

                foreach (var child in parent.AppendedFiles)
                {
                    child.ParentNamn = parent.Namn;
                    child.ParentFile = parent;
                    toAdd.Add(child);
                }

                parent.AppendedFiles.Clear();
            }

            if (toAdd.Count > 0)
                StoredFiles.AddRange(toAdd);
        }

        /// <summary>
        /// Resolves <see cref="FileData.ParentFile"/> back-references from
        /// <see cref="FileData.ParentNamn"/> after loading from JSON.
        /// </summary>
        public void WireParentReferences()
        {
            var byName = new Dictionary<string, FileData>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var file in StoredFiles)
                byName.TryAdd(file.Namn, file);

            foreach (var file in StoredFiles)
            {
                if (!string.IsNullOrEmpty(file.ParentNamn)
                    && byName.TryGetValue(file.ParentNamn, out var parent))
                {
                    file.ParentFile = parent;
                }
            }
        }

        private string namn = string.Empty;
        /// <summary>
        /// Gets or sets the project name.
        /// </summary>
        public string Namn
        {
            get => namn;
            set { namn = value; RaisePropertyChanged(nameof(Namn)); }
        }

        private string category = "Project";
        /// <summary>
        /// Gets or sets the project category.
        /// </summary>
        public string Category
        {
            get => category;
            set { category = value; RaisePropertyChanged(nameof(Category)); }
        }

        private string? parent;
        /// <summary>
        /// Gets or sets the parent project name.
        /// </summary>
        public string? Parent
        {
            get => parent;
            set { parent = value; RaisePropertyChanged(nameof(Parent)); }
        }

        private Color foreground = Color.Parse("#FFFFFFFF");
        /// <summary>
        /// Gets or sets the foreground color for the project.
        /// </summary>
        public Color Foreground
        {
            get => foreground;
            set { foreground = value; RaisePropertyChanged(nameof(Foreground)); }
        }

        private List<string> filetypes = new List<string>();
        /// <summary>
        /// Gets or sets the list of file types in the project.
        /// </summary>
        public List<string> Filetypes
        {
            get => filetypes;
            set { filetypes = value; RaisePropertyChanged(nameof(Filetypes)); }
        }

        private ObservableCollection<string> filetypesTree = new ObservableCollection<string>();
        /// <summary>
        /// Gets or sets the tree of file types in the project.
        /// </summary>
        public ObservableCollection<string> FiletypesTree
        {
            get => filetypesTree;
            set { filetypesTree = value; RaisePropertyChanged(nameof(FiletypesTree)); }
        }

        private ObservableCollection<FolderData> folders = new ObservableCollection<FolderData>();
        /// <summary>
        /// Gets or sets the collection of folders in the project.
        /// </summary>
        public ObservableCollection<FolderData> Folders
        {
            get => folders;
            set { folders = value; RaisePropertyChanged(nameof(Folders)); }
        }

        private static readonly bool[] DefaultMetaValues = [true, false, true, true, true, false, false, false, true, true, true, false, false, false, false, false, false];
        private bool[] metaValues = (bool[])DefaultMetaValues.Clone();

        private bool GetMeta(int index) => metaValues[index];
        private void SetMeta(int index, bool value, [System.Runtime.CompilerServices.CallerMemberName] string? propName = null)
        {
            metaValues[index] = value;
            RaisePropertyChanged(propName!);
        }

        /// <summary>Column visibility: Name (Namn).</summary>
        public bool Meta_1 { get => GetMeta(0); set => SetMeta(0, value); }
        /// <summary>Column visibility: (reserved).</summary>
        public bool Meta_2 { get => GetMeta(1); set => SetMeta(1, value); }
        /// <summary>Column visibility: Filtyp (file type).</summary>
        public bool Meta_3 { get => GetMeta(2); set => SetMeta(2, value); }
        /// <summary>Column visibility: Uppdrag (project/assignment).</summary>
        public bool Meta_4 { get => GetMeta(3); set => SetMeta(3, value); }
        /// <summary>Column visibility: Tagg (tag).</summary>
        public bool Meta_5 { get => GetMeta(4); set => SetMeta(4, value); }
        /// <summary>Column visibility: Färg (color).</summary>
        public bool Meta_6 { get => GetMeta(5); set => SetMeta(5, value); }
        /// <summary>Column visibility: Handling (action).</summary>
        public bool Meta_7 { get => GetMeta(6); set => SetMeta(6, value); }
        /// <summary>Column visibility: Status.</summary>
        public bool Meta_8 { get => GetMeta(7); set => SetMeta(7, value); }
        /// <summary>Column visibility: Datum (date).</summary>
        public bool Meta_9 { get => GetMeta(8); set => SetMeta(8, value); }
        /// <summary>Column visibility: Ritningstyp (drawing type).</summary>
        public bool Meta_10 { get => GetMeta(9); set => SetMeta(9, value); }
        /// <summary>Column visibility: Beskrivning 1 (description 1).</summary>
        public bool Meta_11 { get => GetMeta(10); set => SetMeta(10, value); }
        /// <summary>Column visibility: Beskrivning 2 (description 2).</summary>
        public bool Meta_12 { get => GetMeta(11); set => SetMeta(11, value); }
        /// <summary>Column visibility: Beskrivning 3 (description 3).</summary>
        public bool Meta_13 { get => GetMeta(12); set => SetMeta(12, value); }
        /// <summary>Column visibility: Beskrivning 4 (description 4).</summary>
        public bool Meta_14 { get => GetMeta(13); set => SetMeta(13, value); }
        /// <summary>Column visibility: Revidering (revision).</summary>
        public bool Meta_15 { get => GetMeta(14); set => SetMeta(14, value); }
        /// <summary>Column visibility: Sökväg (file path).</summary>
        public bool Meta_16 { get => GetMeta(15); set => SetMeta(15, value); }
        /// <summary>Column visibility: Active version.</summary>
        public bool Meta_17 { get => GetMeta(16); set => SetMeta(16, value); }

        public bool[] MetaCheckDefault = [true, false, true, true, true, false, false, false, true, true, true, false, false, false, false, false, false];

        public List<string>? AllowedTypes
        {
            get
            {
                if (Category == "Project")
                    return new List<string>() {"Drawing", "Document", "Other"};

                if (Category == "Library")
                {
                    return new List<string>() { "Drawing", "Document", "General", "Loads", "Concrete", "Steel", "Timber", "FEM", "Mechanics", "Dynamics", "Geotechnics", "Other" };
                }
                if (Category == "Archive")
                {
                    return new List<string>() { "Drawing", "Document", "Portal Frame", "Slab", "Beam", "Composite", "Concrete deck", "Integral", "Steel", "Post tension", "Substructure", "Other" };
                }
                else
                {
                    return null;
                }
            }
        }

        public void SetDefaultMeta()
        {
            if (MetaCheckDefault.Length < 17)
                MetaCheckDefault = [true, false, true, true, true, false, false, false, true, true, true, false, false, false, false, false, false];

            for (int i = 0; i < 17; i++)
                metaValues[i] = MetaCheckDefault[i];

            for (int i = 1; i <= 17; i++)
                RaisePropertyChanged($"Meta_{i}");
        }

        public void Newfile(string filepath, string type="New", bool fromFolder = false, string? syncFolder = null)
        {
            if (StoredFiles.Any(x => x.Sökväg == filepath))
                return;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(filepath);
            var existing = StoredFiles.FirstOrDefault(x => !x.IsAppendedFile && x.Namn == fileName);
            if (existing != null)
            {
                existing.AddVersion(filepath, "NEW");
                return;
            }

            StoredFiles.Add(new FileData
            {
                Namn = fileName,
                Filtyp = type,
                Uppdrag = Namn,
                IsFromFolder = fromFolder,
                SyncFolder = syncFolder,
                Sökväg = filepath
            });
            SetFiletypeList();
        }

        /// <summary>
        /// Adds multiple files in a single batch, firing only one collection-change
        /// notification and one <see cref="SetFiletypeList"/> call.
        /// Callers are responsible for duplicate/version checking before calling this.
        /// </summary>
        public void AddFiles(IEnumerable<FileData> files)
        {
            StoredFiles.AddRange(files);
            SetFiletypeList();
        }

        public void RemoveFile(FileData file)
        {
            StoredFiles.Remove(file);
        }

        public void SetFiletypeList()
        {
            Filetypes.Clear();
            FiletypesTree.Clear();

            var topLevel = StoredFiles.Where(x => !x.IsAppendedFile);
            List<string> filetypes = topLevel.Select(x=>x.Filtyp).Distinct().ToList();

            filetypes.Sort();

            foreach (string filetype in filetypes)
            {
                Filetypes.Add(filetype);

                int nrFiles = topLevel.Where(x => x.Filtyp == filetype).Count();
                FiletypesTree.Add(filetype + "\t" + "(" + nrFiles + ")" + "\t\t\t\t\t\t\t\t\t" + Namn);
            }
        }


        /// <summary>
        /// Refreshes <see cref="FileData.HasChildren"/> on every top-level file
        /// by checking whether any child file references it via <see cref="FileData.ParentNamn"/>.
        /// </summary>
        public void RefreshHasChildren()
        {
            var parentNames = new HashSet<string>(
                StoredFiles.Where(f => f.IsAppendedFile).Select(f => f.ParentNamn),
                System.StringComparer.OrdinalIgnoreCase);

            foreach (var file in StoredFiles)
            {
                if (!file.IsAppendedFile)
                    file.HasChildren = parentNames.Contains(file.Namn);
            }
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
