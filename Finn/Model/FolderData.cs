using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace Finn.Model
{
    /// <summary>Classifies what a sync folder does.</summary>
    public enum SyncFolderMode
    {
        /// <summary>Scans for PDFs and adds them to the project file grid.</summary>
        ProjectFiles,
        /// <summary>Scans for PDFs and appends them as children of a specific file.</summary>
        AttachedFiles,
        /// <summary>Scans for non-PDF files and links them as attachments to a file.</summary>
        OtherFiles,
        /// <summary>Scans subfolders for PDFs that match existing files by name and imports them as versions.</summary>
        VersionDelivery
    }

    /// <summary>
    /// Represents a folder and its metadata.
    /// </summary>
    public class FolderData : ObservableObject
    {
        private string name = string.Empty;
        /// <summary>
        /// Gets or sets the folder name.
        /// </summary>
        public string Name
        {
            get => name;
            set { name = value; OnPropertyChanged(nameof(Name)); }
        }

        private string path = string.Empty;
        /// <summary>
        /// Gets or sets the folder path.
        /// </summary>
        public string Path
        {
            get => path;
            set { path = value; OnPropertyChanged(nameof(Path)); }
        }

        private string types = string.Empty;
        /// <summary>
        /// Gets or sets the folder types.
        /// </summary>
        public string Types
        {
            get => types;
            set { types = value; OnPropertyChanged(nameof(Types)); }
        }

        private string? attachToFile;
        public string? AttachToFile
        {
            get { return attachToFile; }
            set { attachToFile = value; OnPropertyChanged(nameof(AttachToFile)); }
        }

        private string? attachToFilePath = null;
        public string? AttachToFilePath
        {
            get { return attachToFilePath; }
            set { attachToFilePath = value; OnPropertyChanged(nameof(AttachToFilePath)); }
        }

        private int syncedFileCount;
        /// <summary>
        /// Number of files synced from this folder during the last sync.
        /// </summary>
        public int SyncedFileCount
        {
            get => syncedFileCount;
            set { syncedFileCount = value; OnPropertyChanged(nameof(SyncedFileCount)); }
        }

        /// <summary>
        /// True when this folder syncs to the project file grid rather than a specific file.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool IsProjectLevel =>
            AttachToFile is null or "PROJECT";

        /// <summary>
        /// Infers the sync mode from the existing <see cref="Types"/> and
        /// <see cref="AttachToFile"/> fields. Backward-compatible with data
        /// created before the enum existed.
        /// </summary>
        [Newtonsoft.Json.JsonIgnore]
        public SyncFolderMode Mode => Types switch
        {
            "Versions" => SyncFolderMode.VersionDelivery,
            "Other Files" when !IsProjectLevel => SyncFolderMode.OtherFiles,
            _ when !IsProjectLevel => SyncFolderMode.AttachedFiles,
            _ => SyncFolderMode.ProjectFiles
        };

        /// <summary>Short user-facing label for the folder's sync mode.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayType => Mode switch
        {
            SyncFolderMode.ProjectFiles => "Sync",
            SyncFolderMode.AttachedFiles => "Attached",
            SyncFolderMode.OtherFiles => "Other Files",
            SyncFolderMode.VersionDelivery => "Versions",
            _ => Types
        };

        /// <summary>Describes what this folder does, for tooltips or empty-state hints.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string Description => Mode switch
        {
            SyncFolderMode.ProjectFiles => "Watches for PDFs → adds to project file list",
            SyncFolderMode.AttachedFiles => $"Watches for PDFs → attaches to \"{AttachToFile}\"",
            SyncFolderMode.OtherFiles => $"Watches for files → links as attachments to \"{AttachToFile}\"",
            SyncFolderMode.VersionDelivery => "Watches subfolders → imports matching PDFs as versions",
            _ => ""
        };

        /// <summary>Display text for the "Target" column — project name or attached file.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string DisplayTarget => Mode switch
        {
            SyncFolderMode.ProjectFiles => "Project",
            SyncFolderMode.VersionDelivery => "All files",
            _ => AttachToFile ?? ""
        };

        /// <summary>Sort key that groups folders by mode in a natural order.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public int SortOrder => (int)Mode;

        public bool IsValid()
        {
            return Directory.Exists(Path);
        }
    }
}
