using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Finn.Model
{
    public class ContentData : INotifyPropertyChanged
    {

        private string name = string.Empty;
        public string Name
        {
            get { return name; }
            set { name = value; RaisePropertyChanged("Name"); }
        }

        private string filepath = string.Empty;
        public string Filepath
        {
            get { return filepath; }
            set { filepath = value; RaisePropertyChanged("Filepath"); }
        }

        private string plainText = string.Empty;
        public string PlainText
        {
            get { return plainText; }
            set { plainText = value; RaisePropertyChanged("PlainText"); }
        }

        private void RaisePropertyChanged(string propName)
        {
            if (PropertyChanged != null)
                PropertyChanged(this, new PropertyChangedEventArgs(propName));
        }
        public event PropertyChangedEventHandler PropertyChanged;
    }
}
