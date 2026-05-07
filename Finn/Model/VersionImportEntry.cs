using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Finn.Model
{
    /// <summary>
    /// Represents a single file that will be added as a new version of an
    /// existing file. Used by the version-import dialog to let the user
    /// choose the label before the version is registered.
    /// </summary>
    public class VersionImportEntry : INotifyPropertyChanged
    {
        private string _selectedLabel = "NEW";

        /// <summary>The existing file this new path matched by name.</summary>
        public FileData ExistingFile { get; init; } = null!;

        /// <summary>The full path of the file being imported.</summary>
        public string NewFilePath { get; init; } = string.Empty;

        /// <summary>Display name of the existing file.</summary>
        public string FileName => ExistingFile?.Namn ?? string.Empty;

        /// <summary>Just the file name of the import path (no directory).</summary>
        public string NewFileName => Path.GetFileName(NewFilePath);

        /// <summary>Comma-separated list of labels already present on the existing file.</summary>
        public string ExistingVersions => ExistingFile?.HasVersions == true
            ? string.Join(", ", ExistingFile.Versions.Select(v => v.Label))
            : "—";

        /// <summary>The label the user chose for this import.</summary>
        public string SelectedLabel
        {
            get => _selectedLabel;
            set
            {
                if (_selectedLabel == value) return;
                _selectedLabel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLabel)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
