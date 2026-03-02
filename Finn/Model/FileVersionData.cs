using System.ComponentModel;
using System.IO;

namespace Finn.Model
{
    /// <summary>
    /// Represents a single version entry for a file that has been revised.
    /// </summary>
    public class FileVersionData : INotifyPropertyChanged
    {
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
