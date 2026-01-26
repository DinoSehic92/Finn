using System.ComponentModel;
using System.IO;

namespace Finn.Model
{
    public class FolderData : INotifyPropertyChanged
    {
        private string name;
        public string Name
        {
            get { return name; }
            set { name = value; RaisePropertyChanged("Name"); RaisePropertyChanged("NameWithAttributes"); }
        }

        public string NameWithAttributes
        {
            get
            {
                string nameWithAttributes = Name;


                if (IsValid)
                {
                    nameWithAttributes = nameWithAttributes + "⠀✓";
                }
                else
                {
                    nameWithAttributes = nameWithAttributes + "⠀✗";
                }

                    return nameWithAttributes;
            }
        }

        private string path;
        public string Path
        {
            get { return path; }
            set { path = value; RaisePropertyChanged("Path"); RaisePropertyChanged("NameWithAttributes"); }
        }

        private string filetype;
        public string Filetype
        {
            get { return filetype; }
            set { filetype = value; RaisePropertyChanged("Filetype"); }
        }

        private FileData? attachToFile = null;
        public FileData? AttachToFile
        {
            get { return attachToFile; }
            set { attachToFile = value; RaisePropertyChanged("AttachToFile"); }
        }

        public bool IsValid
        {
            get { return Directory.Exists(Path); }
        }

        


        private void RaisePropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
