using Avalonia.Controls;
using Avalonia.Media;
using iText.Forms.Exceptions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace Finn.Model
{
    public class ProjectData : INotifyPropertyChanged
    {
        private ObservableCollection<FileData> storedFiles = new ObservableCollection<FileData>();
        public ObservableCollection<FileData> StoredFiles
        {
            get { return storedFiles; }
            set { storedFiles = value; RaisePropertyChanged("StoredFiles"); }
        }

        private string namn = string.Empty;
        public string Namn
        {
            get { return namn; }
            set { namn = value; RaisePropertyChanged("Namn"); }
        }

        private string category = "Project";
        public string Category
        {
            get { return category; }
            set { category = value; RaisePropertyChanged("Category"); }
        }

        private string parent = null;
        public string Parent
        {
            get { return parent; }
            set { parent = value; RaisePropertyChanged("Parent"); }
        }

        private Color foreground = Color.Parse("#FFFFFFFF");
        public Color Foreground
        {
            get { return foreground; }
            set { foreground = value; RaisePropertyChanged("Foreground"); }
        }

        private List<string> filetypes = new List<string>();
        public List<string> Filetypes
        {
            get { return filetypes; }
            set { filetypes = value; RaisePropertyChanged("Filetypes"); }
        }

        private ObservableCollection<string> filetypesTree = new ObservableCollection<string>();
        public ObservableCollection<string> FiletypesTree
        {
            get { return filetypesTree; }
            set { filetypesTree = value; RaisePropertyChanged("FiletypesTree"); }
        }

        private ObservableCollection<FolderData> folders = new ObservableCollection<FolderData>();
        public ObservableCollection<FolderData> Folders
        {
            get { return folders; }
            set { folders = value; RaisePropertyChanged("Folders"); }
        }

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

        public bool Meta_1
        {
            get { return meta_1; }
            set { meta_1 = value; RaisePropertyChanged("Meta_1"); }
        }
        public bool Meta_2
        {
            get { return meta_2; }
            set { meta_2 = value; RaisePropertyChanged("Meta_2"); }
        }
        public bool Meta_3
        {
            get { return meta_3; }
            set { meta_3 = value; RaisePropertyChanged("Meta_3"); }
        }
        public bool Meta_4
        {
            get { return meta_4; }
            set { meta_4 = value; RaisePropertyChanged("Meta_4"); }
        }
        public bool Meta_5
        {
            get { return meta_5; }
            set { meta_5 = value; RaisePropertyChanged("Meta_5"); }
        }
        public bool Meta_6
        {
            get { return meta_6; }
            set { meta_6 = value; RaisePropertyChanged("Meta_6"); }
        }
        public bool Meta_7
        {
            get { return meta_7; }
            set { meta_7 = value; RaisePropertyChanged("Meta_7"); }
        }
        public bool Meta_8
        {
            get { return meta_8; }
            set { meta_8 = value; RaisePropertyChanged("Meta_8"); }
        }
        public bool Meta_9
        {
            get { return meta_9; }
            set { meta_9 = value; RaisePropertyChanged("Meta_9"); }
        }
        public bool Meta_10
        {
            get { return meta_10; }
            set { meta_10 = value; RaisePropertyChanged("Meta_10"); }
        }
        public bool Meta_11
        {
            get { return meta_11; }
            set { meta_11 = value; RaisePropertyChanged("Meta_11"); }
        }
        public bool Meta_12
        {
            get { return meta_12; }
            set { meta_12 = value; RaisePropertyChanged("Meta_12"); }
        }
        public bool Meta_13
        {
            get { return meta_13; }
            set { meta_13 = value; RaisePropertyChanged("Meta_13"); }
        }
        public bool Meta_14
        {
            get { return meta_14; }
            set { meta_14 = value; RaisePropertyChanged("Meta_14"); }
        }
        public bool Meta_15
        {
            get { return meta_15; }
            set { meta_15 = value; RaisePropertyChanged("Meta_15"); }
        }
        public bool Meta_16
        {
            get { return meta_16; }
            set { meta_16 = value; RaisePropertyChanged("Meta_16"); }
        }

        public bool[] MetaCheckDefault = { true, false, true, true, true, false, false, false, true, true, true, false, false, false, false, false };

        public List<string> AllowedTypes
        {
            get
            {
                if (Category == "Project")
                    return new List<string>() {"Drawing", "Document", "Other"};

                if (Category == "Library")
                {
                    return new List<string>() { "General", "Loads", "Concrete", "Steel", "Timber", "FEM", "Mechanics", "Dynamics", "Geotechnics", "Other" };
                }
                if (Category == "Archive")
                {
                    return new List<string>() { "Portal Frame", "Slab", "Beam", "Composite", "Concrete deck", "Integral", "Steel", "Post tension", "Substructure", "Other" };
                }
                else
                {
                    return new List<string>() { "Error" };
                }
            }
        }

        public void SetDefaultMeta()
        {
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
        }

        public void Newfile(string filepath, string type="New", bool fromFolder = false, string? syncFolder = null)
        {
            if (!StoredFiles.Any(x => x.Sökväg == filepath))
            {
                StoredFiles.Add(new FileData
                {
                    Namn = System.IO.Path.GetFileNameWithoutExtension(filepath),
                    Filtyp = type,
                    Uppdrag = Namn,
                    IsFromFolder = fromFolder,
                    SyncFolder = syncFolder,
                    Sökväg = filepath
                });
                SetFiletypeList();
            }
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
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
