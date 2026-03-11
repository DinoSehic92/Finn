using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Finn.Model
{
    /// <summary>
    /// Represents a single delivery subfolder during version folder sync.
    /// All matched files in this folder share the same version label.
    /// </summary>
    public class DeliveryFolderEntry : INotifyPropertyChanged
    {
        private string _selectedLabel = "NEW";
        private List<string>? _availableLabels;

        /// <summary>The full path of the delivery subfolder.</summary>
        public string FolderPath { get; init; } = string.Empty;

        /// <summary>Display name of the folder.</summary>
        public string FolderName => Path.GetFileName(FolderPath);

        /// <summary>Matched files in this delivery: existing file → PDF path.</summary>
        public List<(FileData File, string PdfPath)> MatchedFiles { get; init; } = [];

        /// <summary>Number of project files matched in this folder.</summary>
        public int MatchedCount => MatchedFiles.Count;

        /// <summary>Total project files for context (e.g. "42 of 120").</summary>
        public int TotalProjectFiles { get; init; }

        /// <summary>Display string: "42 of 120".</summary>
        public string MatchSummary => $"{MatchedCount} of {TotalProjectFiles}";

        /// <summary>
        /// Available labels for the ComboBox: the auto-detected label (date or letter)
        /// placed first, followed by the predefined version labels.
        /// </summary>
        public List<string> AvailableLabels =>
            _availableLabels ??= BuildAvailableLabels();

        private List<string> BuildAvailableLabels()
        {
            var labels = new List<string>();
            // Put the auto-detected label first if it's not already in the predefined list
            if (!string.IsNullOrEmpty(_selectedLabel)
                && !FileVersionData.VersionLabels.Contains(_selectedLabel))
            {
                labels.Add(_selectedLabel);
            }
            labels.AddRange(FileVersionData.VersionLabels);
            return labels;
        }

        /// <summary>The label the user chose for this delivery. Guards against null/empty.</summary>
        public string SelectedLabel
        {
            get => _selectedLabel;
            set
            {
                // Guard: never accept null or empty — keep current value
                if (string.IsNullOrWhiteSpace(value)) return;
                if (_selectedLabel == value) return;
                _selectedLabel = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedLabel)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
