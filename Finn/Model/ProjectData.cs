using Avalonia.Media;
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
        private ObservableCollection<FileData> storedFiles = new ObservableCollection<FileData>();
        /// <summary>
        /// Gets or sets the collection of stored files for the project.
        /// </summary>
        public ObservableCollection<FileData> StoredFiles { get; set; } = new ObservableCollection<FileData>();

        private string namn = string.Empty;
        /// <summary>
        /// Gets or sets the project name.
        /// </summary>
        public string Namn { get; set; } = string.Empty;

        private string category = "Project";
        /// <summary>
        /// Gets or sets the project category.
        /// </summary>
        public string Category { get; set; } = "Project";

        private string parent = null;
        /// <summary>
        /// Gets or sets the parent project name.
        /// </summary>
        public string? Parent { get; set; }

        private Color foreground = Color.Parse("#FFFFFFFF");
        /// <summary>
        /// Gets or sets the foreground color for the project.
        /// </summary>
        public Color Foreground { get; set; } = Color.Parse("#FFFFFFFF");

        private List<string> filetypes = new List<string>();
        /// <summary>
        /// Gets or sets the list of file types in the project.
        /// </summary>
        public List<string> Filetypes { get; set; } = new();

        private ObservableCollection<string> filetypesTree = new ObservableCollection<string>();
        /// <summary>
        /// Gets or sets the tree of file types in the project.
        /// </summary>
        public ObservableCollection<string> FiletypesTree { get; set; } = new();

        private ObservableCollection<FolderData> folders = new ObservableCollection<FolderData>();
        /// <summary>
        /// Gets or sets the collection of folders in the project.
        /// </summary>
        public ObservableCollection<FolderData> Folders { get; set; } = new();

        private bool meta_1 = true;
        private bool meta_2 = false;
        private bool meta_3 = true;
        private bool meta_4 = true;
        private bool meta_5 = true;
        private bool meta_6 = false;
        private bool meta_7 = false;
        private bool meta_8 = false;
        private bool meta_9 = true;
        private bool meta_10 = true;
        private bool meta_11 = true;
        private bool meta_12 = false;
        private bool meta_13 = false;
        private bool meta_14 = false;
        private bool meta_15 = false;
        private bool meta_16 = false;
        private bool meta_17 = false;

        /// <summary>
        /// Gets or sets Meta_1 property.
        /// </summary>
        public bool Meta_1 { get { return meta_1; } set { meta_1 = value; RaisePropertyChanged(nameof(Meta_1)); } }
        /// <summary>
        /// Gets or sets Meta_2 property.
        /// </summary>
        public bool Meta_2 { get { return meta_2; } set { meta_2 = value; RaisePropertyChanged(nameof(Meta_2)); } }
        /// <summary>
        /// Gets or sets Meta_3 property.
        /// </summary>
        public bool Meta_3 { get { return meta_3; } set { meta_3 = value; RaisePropertyChanged(nameof(Meta_3)); } }
        /// <summary>
        /// Gets or sets Meta_4 property.
        /// </summary>
        public bool Meta_4 { get { return meta_4; } set { meta_4 = value; RaisePropertyChanged(nameof(Meta_4)); } }
        /// <summary>
        /// Gets or sets Meta_5 property.
        /// </summary>
        public bool Meta_5 { get { return meta_5; } set { meta_5 = value; RaisePropertyChanged(nameof(Meta_5)); } }
        /// <summary>
        /// Gets or sets Meta_6 property.
        /// </summary>
        public bool Meta_6 { get { return meta_6; } set { meta_6 = value; RaisePropertyChanged(nameof(Meta_6)); } }
        /// <summary>
        /// Gets or sets Meta_7 property.
        /// </summary>
        public bool Meta_7 { get { return meta_7; } set { meta_7 = value; RaisePropertyChanged(nameof(Meta_7)); } }
        /// <summary>
        /// Gets or sets Meta_8 property.
        /// </summary>
        public bool Meta_8 { get { return meta_8; } set { meta_8 = value; RaisePropertyChanged(nameof(Meta_8)); } }
        /// <summary>
        /// Gets or sets Meta_9 property.
        /// </summary>
        public bool Meta_9 { get { return meta_9; } set { meta_9 = value; RaisePropertyChanged(nameof(Meta_9)); } }
        /// <summary>
        /// Gets or sets Meta_10 property.
        /// </summary>
        public bool Meta_10 { get { return meta_10; } set { meta_10 = value; RaisePropertyChanged(nameof(Meta_10)); } }
        /// <summary>
        /// Gets or sets Meta_11 property.
        /// </summary>
        public bool Meta_11 { get { return meta_11; } set { meta_11 = value; RaisePropertyChanged(nameof(Meta_11)); } }
        /// <summary>
        /// Gets or sets Meta_12 property.
        /// </summary>
        public bool Meta_12 { get { return meta_12; } set { meta_12 = value; RaisePropertyChanged(nameof(Meta_12)); } }
        /// <summary>
        /// Gets or sets Meta_13 property.
        /// </summary>
        public bool Meta_13 { get { return meta_13; } set { meta_13 = value; RaisePropertyChanged(nameof(Meta_13)); } }
        /// <summary>
        /// Gets or sets Meta_14 property.
        /// </summary>
        public bool Meta_14 { get { return meta_14; } set { meta_14 = value; RaisePropertyChanged(nameof(Meta_14)); } }
        /// <summary>
        /// Gets or sets Meta_15 property.
        /// </summary>
        public bool Meta_15 { get { return meta_15; } set { meta_15 = value; RaisePropertyChanged(nameof(Meta_15)); } }
        /// <summary>
        /// Gets or sets Meta_16 property.
        /// </summary>
        public bool Meta_16 { get { return meta_16; } set { meta_16 = value; RaisePropertyChanged(nameof(Meta_16)); } }
        /// <summary>
        /// Gets or sets Meta_17 property (Version column).
        /// </summary>
        public bool Meta_17 { get { return meta_17; } set { meta_17 = value; RaisePropertyChanged(nameof(Meta_17)); } }

        public bool[] MetaCheckDefault = { true, false, true, true, true, false, false, false, true, true, true, false, false, false, false, false, false };

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

            Meta_1 = MetaCheckDefault[0];
            Meta_2 = MetaCheckDefault[1];
            Meta_3 = MetaCheckDefault[2];
            Meta_4 = MetaCheckDefault[3];
            Meta_5 = MetaCheckDefault[4];
            Meta_6 = MetaCheckDefault[5];
            Meta_7 = MetaCheckDefault[6];
            Meta_8 = MetaCheckDefault[7];
            Meta_9 = MetaCheckDefault[8];
            Meta_10 = MetaCheckDefault[9];
            Meta_11 = MetaCheckDefault[10];
            Meta_12 = MetaCheckDefault[11];
            Meta_13 = MetaCheckDefault[12];
            Meta_14 = MetaCheckDefault[13];
            Meta_15 = MetaCheckDefault[14];
            Meta_16 = MetaCheckDefault[15];
            Meta_17 = MetaCheckDefault[16];
        }

        public void Newfile(string filepath, string type="New", bool fromFolder = false, string? syncFolder = null)
        {
            if (StoredFiles.Any(x => x.Sökväg == filepath))
                return;

            string fileName = System.IO.Path.GetFileNameWithoutExtension(filepath);
            var existing = StoredFiles.FirstOrDefault(x => x.Namn == fileName);
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

        public void RemoveFile(FileData file)
        {
            StoredFiles.Remove(file);
        }

        public void SetFiletypeList()
        {
            Filetypes.Clear();
            FiletypesTree.Clear();

            List<string> filetypes = StoredFiles.Select(x=>x.Filtyp).Distinct().ToList();

            filetypes.Sort();

            foreach (string filetype in filetypes)
            {
                Filetypes.Add(filetype);

                int nrFiles = StoredFiles.Where(x => x.Filtyp == filetype).Count();
                FiletypesTree.Add(filetype + "\t" + "(" + nrFiles + ")" + "\t\t\t\t\t\t\t\t\t" + Namn);
            }
        }


        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
