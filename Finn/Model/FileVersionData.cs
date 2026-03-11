using Newtonsoft.Json;
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
            new[] { "GRANSKNINGSHANDLING", "MOTTAGNINGSKONTROLL 1", "MOTTAGNINGSKONTROLL 2", "MOTTAGNINGSKONTROLL 3",
                    "BYGGHANDLING", "REV A", "REV B", "REV C", "REV D", "REV E", "RELATIONSHANDLING", "NEW"};

        /// <summary>
        /// Label → sort-index lookup (O(1)) built from <see cref="VersionLabels"/>.
        /// Labels not present in the list sort after all known labels.
        /// </summary>
        public static IReadOnlyDictionary<string, int> LabelOrder { get; } =
            VersionLabels.Select((l, i) => (l, i)).ToDictionary(x => x.l, x => x.i);
        private string _sökväg = string.Empty;
        private string _label = string.Empty;
        private string _addedDate = string.Empty;
        private bool _isActive;

        [JsonIgnore]
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(VersionOpacity));
            }
        }

        [JsonIgnore]
        public double VersionOpacity => _isActive ? 1.0 : 0.55;

        public string Sökväg
        {
            get => _sökväg;
            set
            {
                if (_sökväg == value) return;
                _sökväg = value;
                OnPropertyChanged(nameof(Sökväg));
                OnPropertyChanged(nameof(ShortName));
                OnPropertyChanged(nameof(DirectoryPath));
            }
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
            set
            {
                if (_addedDate == value) return;
                _addedDate = value;
                OnPropertyChanged(nameof(AddedDate));
            }
        }

        [JsonIgnore]
        public string ShortName => Path.GetFileNameWithoutExtension(_sökväg);

        [JsonIgnore]
        public string DirectoryPath => Path.GetDirectoryName(_sökväg) ?? string.Empty;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
