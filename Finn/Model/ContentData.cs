using System.ComponentModel;

namespace Finn.Model
{
    /// <summary>
    /// Represents content data for a file.
    /// </summary>
    public class ContentData : INotifyPropertyChanged
    {
        private string name = string.Empty;
        /// <summary>
        /// Gets or sets the content name.
        /// </summary>
        public string Name
        {
            get => name;
            set { name = value; RaisePropertyChanged(nameof(Name)); }
        }

        private string filepath = string.Empty;
        /// <summary>
        /// Gets or sets the file path.
        /// </summary>
        public string Filepath
        {
            get => filepath;
            set { filepath = value; RaisePropertyChanged(nameof(Filepath)); }
        }

        private string plainText = string.Empty;
        /// <summary>
        /// Gets or sets the plain text content.
        /// </summary>
        public string PlainText
        {
            get => plainText;
            set { plainText = value; RaisePropertyChanged(nameof(PlainText)); }
        }

        private void RaisePropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
