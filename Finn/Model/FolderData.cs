using System.ComponentModel;
using System.IO;

namespace Finn.Model
{
    /// <summary>
    /// Represents a folder and its metadata.
    /// </summary>
    /// <summary>
    /// Represents a folder and its metadata.
    /// </summary>
    public class FolderData : INotifyPropertyChanged
    {
        private string name = string.Empty;
        /// <summary>
        /// Gets or sets the folder name.
        /// </summary>
        public string Name
        {
            get => name;
            set { name = value; RaisePropertyChanged(nameof(Name)); RaisePropertyChanged(nameof(NameWithAttributes)); }
        }

        /// <summary>
        /// Gets the folder name with attributes (valid/invalid).
        /// </summary>
        /// <summary>
        /// Gets the folder name with attributes (valid/invalid).
        /// </summary>
        public string NameWithAttributes
        {
            get
            {
                string nameWithAttributes = Name;
                if (IsValid())
                    nameWithAttributes += "⠀✓";
                else
                    nameWithAttributes += "⠀✗";
                return nameWithAttributes;
            }
        }

        private string path = string.Empty;
        /// <summary>
        /// Gets or sets the folder path.
        /// </summary>
        public string Path
        {
            get => path;
            set { path = value; RaisePropertyChanged(nameof(Path)); RaisePropertyChanged(nameof(NameWithAttributes)); }
        }

        private string types = string.Empty;
        /// <summary>
        /// Gets or sets the folder types.
        /// </summary>
        public string Types
        {
            get => types;
            set { types = value; RaisePropertyChanged(nameof(Types)); }
        }

        private string? attachToFile;
        public string? AttachToFile
        {
            get { return attachToFile; }
            set { attachToFile = value; RaisePropertyChanged("AttachToFile"); }
        }

        private string? attachToFilePath = null;
        public string? AttachToFilePath
        {
            get { return attachToFilePath; }
            set { attachToFilePath = value; RaisePropertyChanged("AttachToFilePath"); }
        }

        public bool IsValid()
        {
            return Directory.Exists(Path);
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
