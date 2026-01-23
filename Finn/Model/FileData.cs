using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Finn.Model
{
    public class FileData : INotifyPropertyChanged
    {

        private string namn = string.Empty;
        public string Namn
        {
            get { return namn; }
            set { namn = value; RaisePropertyChanged("Namn"); RaisePropertyChanged("NameWithAttributes"); }
        }

        private string fileStatus = string.Empty;
        public string FileStatus
        {
            get { return fileStatus; }
            set { fileStatus = value; RaisePropertyChanged("FileStatus"); }
        }

        private string tagg = string.Empty;
        public string Tagg
        {
            get { return tagg; }
            set { tagg = value; RaisePropertyChanged("Tagg"); }
        }

        private string färg = string.Empty;
        public string Färg
        {
            get { return färg; }
            set { färg = value; RaisePropertyChanged("Färg"); }
        }

        private string handling = string.Empty;
        public string Handling
        {
            get { return handling; }
            set { handling = value; RaisePropertyChanged("Handling"); }
        }

        private string status = string.Empty;
        public string Status
        {
            get { return status; }
            set { status = value; RaisePropertyChanged("Status"); }
        }

        private string datum = string.Empty;
        public string Datum
        {
            get { return datum; }
            set { datum = value; RaisePropertyChanged("Datum"); }
        }

        private string ritningstyp = string.Empty;
        public string Ritningstyp
        {
            get { return ritningstyp; }
            set { ritningstyp = value; RaisePropertyChanged("Ritningstyp"); }
        }

        private string beskrivning1 = string.Empty;
        public string Beskrivning1
        {
            get { return beskrivning1; }
            set { beskrivning1 = value; RaisePropertyChanged("Beskrivning1"); }
        }

        private string beskrivning2 = string.Empty;
        public string Beskrivning2
        {
            get { return beskrivning2; }
            set { beskrivning2 = value; RaisePropertyChanged("Beskrivning2"); }
        }

        private string beskrivning3 = string.Empty;
        public string Beskrivning3
        {
            get { return beskrivning3; }
            set { beskrivning3 = value; RaisePropertyChanged("Beskrivning3"); }
        }

        private string beskrivning4 = string.Empty;
        public string Beskrivning4
        {
            get { return beskrivning4; }
            set { beskrivning4 = value; RaisePropertyChanged("Beskrivning4"); }
        }

        private string uppdrag = string.Empty;
        public string Uppdrag
        {
            get { return uppdrag; }
            set { uppdrag = value; RaisePropertyChanged("Uppdrag"); }
        }

        private string filtyp = string.Empty;
        public string Filtyp
        {
            get { return filtyp; }
            set { filtyp = value; RaisePropertyChanged("Filtyp"); }
        }

        private string revidering = string.Empty;
        public string Revidering
        {
            get { return revidering; }
            set { revidering = value; RaisePropertyChanged("Revidering"); }
        }

        private string sökväg = string.Empty;
        public string Sökväg
        {
            get { return sökväg; }
            set { sökväg = value; RaisePropertyChanged("Sökväg"); }
        }

        public bool IsLocal
        {
            get 
            {
                if (Sökväg[0].ToString() == "C")
                {
                    return true;
                }

                else
                {
                    return false;
                }
            }
        }

        private int defaultPage;
        public int DefaultPage
        {
            get { return defaultPage; }
            set { defaultPage = value; RaisePropertyChanged("DefaultPage"); }
        }

        private ObservableCollection<PageData> favPages = new ObservableCollection<PageData>();
        public ObservableCollection<PageData> FavPages
        {
            get { return favPages; }
            set { favPages = value; RaisePropertyChanged("FavPages"); RaisePropertyChanged("NameWithAttributes"); }
        }

        private bool isFromFolder = false;
        public bool IsFromFolder
        {
            get { return isFromFolder; }
            set { isFromFolder = value; RaisePropertyChanged("IsFromFolder"); RaisePropertyChanged("NameWithAttributes"); }
        }

        public string FromFolder
        {
            get {
                return Path.GetFileName(Path.GetDirectoryName(Sökväg));
                }
            
        }

        public bool HasBookmarks
        {
            get
            {
                if (FavPages.Count > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        private ObservableCollection<FileData> appendedFiles = new ObservableCollection<FileData>();
        public ObservableCollection<FileData> AppendedFiles
        {
            get { return appendedFiles; }
            set { appendedFiles = value; RaisePropertyChanged("AppendedFiles"); RaisePropertyChanged("NameWithAttributes"); }
        }

        private ObservableCollection<FolderData> appendedFolders = new ObservableCollection<FolderData>();
        public ObservableCollection<FolderData> AppendedFolders
        {
            get { return appendedFolders; }
            set { appendedFolders = value; RaisePropertyChanged("AppendedFolders"); }
        }


        private ObservableCollection<OtherData> otherFiles = new ObservableCollection<OtherData>();
        public ObservableCollection<OtherData> OtherFiles
        {
            get { return otherFiles; }
            set { otherFiles = value; RaisePropertyChanged("OtherFiles"); RaisePropertyChanged("NameWithAttributes"); }
        }

        public bool HasAppendedFiles
        {
            get
            {
                if (AppendedFiles.Count > 0 || OtherFiles.Count > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public string NameWithAttributes
        {
            get
            {
                string nameWithAttributes = Namn;

                if (Favorite)
                {
                    nameWithAttributes = "⭐ " + nameWithAttributes;
                }

                if (HasAppendedFiles || HasBookmarks || HasNote || HasThumbnail)
                {
                    nameWithAttributes = nameWithAttributes + "⠀";
                }

                if (HasAppendedFiles)
                {
                    nameWithAttributes = nameWithAttributes + "📎";
                }

                if (HasBookmarks)
                {
                    nameWithAttributes = nameWithAttributes + "🔖";
                }

                if (HasNote)
                {
                    nameWithAttributes = nameWithAttributes + " 📝";
                }

                if (HasThumbnail)
                {
                    nameWithAttributes = nameWithAttributes + " ؞";
                }

                if (HasPlainText)
                {
                    nameWithAttributes = nameWithAttributes + "⠀🌐";
                }

                if (IsFromFolder)
                {
                    nameWithAttributes = nameWithAttributes + "⠀🗁";
                }

                return nameWithAttributes;
            }
        }

        private string note = string.Empty;
        public string Note
        {
            get { return note; }
            set { note = value; RaisePropertyChanged("Note"); RaisePropertyChanged("NameWithAttributes"); }
        }

        public bool HasNote
        {
            get
            {
                if (Note.Length > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        private bool favorite = false;
        public bool Favorite
        {
            get { return favorite; }
            set { favorite = value; RaisePropertyChanged("Favorite"); RaisePropertyChanged("NameWithAttributes"); }
        }


        private List<string> partOfCollections = new List<string>();
        public List<string> PartOfCollections
        {
            get { return partOfCollections; }
            set { partOfCollections = value; RaisePropertyChanged("PartOfCollections"); }
        }


        private string thumbnailSource = string.Empty;
        public string ThumbnailSource
        {
            get { return thumbnailSource; }
            set { thumbnailSource = value; RaisePropertyChanged("ThumbnailSource"); RaisePropertyChanged("NameWithAttributes"); }
        }

        public bool HasThumbnail
        {
            get
            {
                if (ThumbnailSource.Length > 0)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        private bool hasPlainText = false;
        public bool HasPlainText
        {
            get { return hasPlainText; }
            set { hasPlainText = value; RaisePropertyChanged("HasPlainText"); RaisePropertyChanged("NameWithAttributes"); }
        }

        public void RemoveThumbnail()
        {
            if (thumbnailSource != string.Empty)
            {
                System.IO.File.Delete(thumbnailSource);
                ThumbnailSource = string.Empty;
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
