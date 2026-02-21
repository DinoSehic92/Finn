using Avalonia;
using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Styling;
using System.Collections.ObjectModel;

namespace Finn.Model
{
    /// <summary>
    /// Represents general application data and color settings.
    /// </summary>
    /// <summary>
    /// Represents general application data and color settings.
    /// </summary>
    public class GeneralData : INotifyPropertyChanged
    {

        private string savePath = "C:\\FIlePathManager";
        /// <summary>
        /// Gets or sets the save path.
        /// </summary>
        public string SavePath
        {
            get => savePath;
            set { savePath = value; RaisePropertyChanged(nameof(SavePath)); }
        }

        private ObservableCollection<string> collections = new ObservableCollection<string>();
        public ObservableCollection<string> Collections
        {
            get { return collections; }
            set { collections = value; RaisePropertyChanged("Collections"); }
        }

        // Calendar list and time projects moved to CalendarStore

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
