using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace Finn.Model
{
    /// <summary>
    /// Represents a single version entry for a file that has been revised.
    /// </summary>
    public class FileVersionData : INotifyPropertyChanged
    {
        /// <summary>
        /// Predefined revision labels in display/sort order.
        /// </summary>
        public static IReadOnlyList<string> VersionLabels { get; } =
            new[] { "Orginal", "Mottagningskontroll 1", "Mottagningskontroll 2", "Mottagningskontroll 3",
                    "Bygghandling", "Rev A", "Rev B", "Rev C", "Rev D", "Rev E", "Relation" };

        /// <summary>
        /// Label → sort-index lookup (O(1)) built from <see cref="VersionLabels"/>.
        /// Labels not present in the list sort after all known labels.
        /// </summary>
        public static IReadOnlyDictionary<string, int> LabelOrder { get; } =
            VersionLabels.Select((l, i) => (l, i)).ToDictionary(x => x.l, x => x.i);
        private string _sökväg = string.Empty;
        private string _label = string.Empty;
        private string _addedDate = string.Empty;

        public string Sökväg
        {
            get => _sökväg;
            set { _sökväg = value; OnPropertyChanged(nameof(Sökväg)); OnPropertyChanged(nameof(ShortName)); }
        }

        public string Label
        {
            get => _label;
            set
            {
                if (_label == value) return;
                _label = value;
                OnPropertyChanged(nameof(Label));
            }
        }

        public string AddedDate
        {
            get => _addedDate;
            set { _addedDate = value; OnPropertyChanged(nameof(AddedDate)); }
        }

        public string ShortName => Path.GetFileNameWithoutExtension(_sökväg);

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
