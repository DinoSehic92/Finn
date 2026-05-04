using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
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
            set { types = value; _cachedMode = null; OnPropertyChanged(nameof(Types)); }
        }

        private string? attachToFile;
        public string? AttachToFile
        {
            get { return attachToFile; }
            set { attachToFile = value; _cachedMode = null; OnPropertyChanged(nameof(AttachToFile)); }
        }

        private string? attachToFilePath = null;
        public string? AttachToFilePath
        {
            get { return attachToFilePath; }
            set { attachToFilePath = value; OnPropertyChanged(nameof(AttachToFilePath)); }
        }

        private int syncedFileCount;
        /// <summary>
        /// Number of files on disk recorded at the last sync.
        /// Kept for backward-compat deserialisation and as a fast pre-check
        /// for the filesystem watcher. Prefer <see cref="SyncedPaths"/> for
        /// change detection when it is populated.
        /// </summary>
        public int SyncedFileCount
        {
            get => syncedFileCount;
            set { syncedFileCount = value; OnPropertyChanged(nameof(SyncedFileCount)); OnPropertyChanged(nameof(DisplayFileCount)); }
        }

        private int trackedVersionCount;
        /// <summary>
        /// Number of actually imported versions for version delivery folders.
        /// Only meaningful when <see cref="Mode"/> is <see cref="SyncFolderMode.VersionDelivery"/>.
        /// Kept for backward-compat deserialisation.
        /// </summary>
        public int TrackedVersionCount
        {
            get => trackedVersionCount;
            set { trackedVersionCount = value; OnPropertyChanged(nameof(TrackedVersionCount)); OnPropertyChanged(nameof(DisplayFileCount)); }
        }

        /// <summary>
        /// App-state paths recorded at the last successful sync.
        /// For <see cref="SyncFolderMode.ProjectFiles"/> and
        /// <see cref="SyncFolderMode.AttachedFiles"/> these are file paths
        /// (<see cref="FileData.Sökväg"/>). For
        /// <see cref="SyncFolderMode.OtherFiles"/> they are the attachment
        /// file paths (<see cref="OtherData.Filepath"/>). For
        /// <see cref="SyncFolderMode.VersionDelivery"/> they are version paths
        /// (<see cref="FileVersionData.Sökväg"/>).
        /// When this list is non-empty it is the authoritative baseline;
        /// change detection compares current app state against it directly
        /// instead of using the count-based heuristic.
        /// </summary>
        public List<string> SyncedPaths { get; set; } = [];

        /// <summary>
        /// The file count displayed in the folder grid.
        /// Prefers <see cref="SyncedPaths"/> when populated;
        /// otherwise falls back to the legacy count fields.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int DisplayFileCount =>
            SyncedPaths.Count > 0
                ? SyncedPaths.Count
                : Mode == SyncFolderMode.VersionDelivery ? trackedVersionCount : syncedFileCount;

        /// <summary>
        /// UTC timestamp recorded after the last successful sync.
        /// Used by the startup check to detect disk changes that happened
        /// while the app was closed — avoids complex per-type comparisons.
        /// </summary>
        public DateTime? LastSyncedUtc { get; set; }

        /// <summary>
        /// True when this folder syncs to the project file grid rather than a specific file.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsProjectLevel =>
            AttachToFile is null or "PROJECT";

        /// <summary>
        /// Infers the sync mode from the existing <see cref="Types"/> and
        /// <see cref="AttachToFile"/> fields. Backward-compatible with data
        /// created before the enum existed. Cached to avoid re-evaluating
        /// the switch on every access; invalidated when <see cref="Types"/>
        /// or <see cref="AttachToFile"/> changes.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public SyncFolderMode Mode => _cachedMode ??= Types switch
        {
            "Versions" => SyncFolderMode.VersionDelivery,
            "Other Files" when !IsProjectLevel => SyncFolderMode.OtherFiles,
            _ when !IsProjectLevel => SyncFolderMode.AttachedFiles,
            _ => SyncFolderMode.ProjectFiles
        };
        private SyncFolderMode? _cachedMode;

        /// <summary>Short user-facing label for the folder's sync mode.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayType => Mode switch
        {
            SyncFolderMode.ProjectFiles => "Sync",
            SyncFolderMode.AttachedFiles => "Attached",
            SyncFolderMode.OtherFiles => "Other Files",
            SyncFolderMode.VersionDelivery => "Versions",
            _ => Types
        };

        /// <summary>Describes what this folder does, for tooltips or empty-state hints.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string Description => Mode switch
        {
            SyncFolderMode.ProjectFiles => "Watches for PDFs → adds to project file list",
            SyncFolderMode.AttachedFiles => $"Watches for PDFs → attaches to \"{AttachToFile}\"",
            SyncFolderMode.OtherFiles => $"Watches for files → links as attachments to \"{AttachToFile}\"",
            SyncFolderMode.VersionDelivery => "Watches subfolders → imports matching PDFs as versions",
            _ => ""
        };

        /// <summary>Display text for the "Target" column — project name or attached file.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayTarget => Mode switch
        {
            SyncFolderMode.ProjectFiles => "Project",
            SyncFolderMode.VersionDelivery => "All files",
            _ => AttachToFile ?? ""
        };

        /// <summary>Sort key that groups folders by mode in a natural order.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public int SortOrder => (int)Mode;

        /// <summary>
        /// File names (without extension) excluded from sync.
        /// For <see cref="SyncFolderMode.VersionDelivery"/> folders this stores
        /// full file paths instead of names, since the same name can appear in
        /// multiple subfolders.
        /// Persisted with the project so exclusions survive restarts.
        /// </summary>
        public List<string> ExcludedFiles { get; set; } = [];

        /// <summary>
        /// Returns <c>true</c> when <paramref name="fileName"/> (without extension)
        /// is in the exclusion list. Used for non-version folders.
        /// </summary>
        public bool IsExcluded(string fileName)
        {
            if (ExcludedFiles.Count == 0) return false;
            _excludedSet ??= new HashSet<string>(ExcludedFiles, StringComparer.OrdinalIgnoreCase);
            return _excludedSet.Contains(fileName);
        }

        /// <summary>
        /// Returns <c>true</c> when <paramref name="filePath"/> is in the
        /// exclusion list. Used for version delivery folders where exclusions
        /// are stored as full paths.
        /// </summary>
        public bool IsExcludedPath(string filePath)
        {
            if (ExcludedFiles.Count == 0) return false;
            _excludedSet ??= new HashSet<string>(ExcludedFiles, StringComparer.OrdinalIgnoreCase);
            return _excludedSet.Contains(filePath);
        }

        /// <summary>Lazily built O(1) lookup for <see cref="ExcludedFiles"/>.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        private HashSet<string>? _excludedSet;

        /// <summary>Invalidates the cached exclusion set. Call after modifying <see cref="ExcludedFiles"/>.</summary>
        public void InvalidateExclusionCache() => _excludedSet = null;

        /// <summary>
        /// Checks whether the folder exists on disk.
        /// WARNING: This performs I/O (<see cref="Directory.Exists"/>).
        /// Do not call on the UI thread for network paths.
        /// </summary>
        public bool ExistsOnDisk()
        {
            return Directory.Exists(Path);
        }
    }
}
