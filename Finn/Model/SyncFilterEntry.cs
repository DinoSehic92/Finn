using System.ComponentModel;

namespace Finn.Model
{
    /// <summary>
    /// Represents a single file in the sync-filter dialog.
    /// Checked = synced, unchecked = excluded from sync.
    /// </summary>
    public class SyncFilterEntry : INotifyPropertyChanged
    {
        private bool _isIncluded = true;

        /// <summary>File name without extension.</summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>Full disk path, shown as a tooltip or secondary column.</summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>Whether this file is included in sync (checked).</summary>
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded == value) return;
                _isIncluded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIncluded)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
