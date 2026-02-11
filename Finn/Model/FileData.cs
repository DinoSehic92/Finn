using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Finn.Model
{
    public class FileData : INotifyPropertyChanged
    {
        #region Constants
        private const string FAVORITE_ICON = "⭐ ";
        private const string SEPARATOR = "⠀";
        private const string ATTACHMENT_ICON = "📎";
        private const string BOOKMARK_ICON = "🔖";
        private const string NOTE_ICON = " 📝";
        private const string THUMBNAIL_ICON = " ؞";
        private const string PLAINTEXT_ICON = "⠀🌐";
        private const string FOLDER_ICON = "⠀🗀";
        private const string PDF_EXTENSION = ".pdf";
        #endregion

        #region Fields
        private string namn = string.Empty;
        private string fileStatus = string.Empty;
        private string tagg = string.Empty;
        private string färg = string.Empty;
        private string handling = string.Empty;
        private string status = string.Empty;
        private string datum = string.Empty;
        private string ritningstyp = string.Empty;
        private string beskrivning1 = string.Empty;
        private string beskrivning2 = string.Empty;
        private string beskrivning3 = string.Empty;
        private string beskrivning4 = string.Empty;
        private string uppdrag = string.Empty;
        private string filtyp = string.Empty;
        private string revidering = string.Empty;
        private string sökväg = string.Empty;
        private int defaultPage = 0;
        private ObservableCollection<PageData> favPages = new();
        private bool isFromFolder = false;
        private string fromFolder = string.Empty;
        private string? syncFolder = string.Empty;
        private ObservableCollection<FileData> appendedFiles = new();
        private ObservableCollection<OtherData> otherFiles = new();
        private string note = string.Empty;
        private bool favorite = false;
        private List<string> partOfCollections = new();
        private string thumbnailSource = string.Empty;
        private bool hasPlainText = false;
        #endregion

        #region Properties
        public string Namn
        {
            get => namn;
            set { SetProperty(ref namn, value); OnPropertyChanged(nameof(NameWithAttributes)); }
        }

        public string FileStatus
        {
            get => fileStatus;
            set => SetProperty(ref fileStatus, value);
        }

        public string Tagg
        {
            get => tagg;
            set => SetProperty(ref tagg, value);
        }

        public string Färg
        {
            get => färg;
            set => SetProperty(ref färg, value);
        }

        public string Handling
        {
            get => handling;
            set => SetProperty(ref handling, value);
        }

        public string Status
        {
            get => status;
            set => SetProperty(ref status, value);
        }

        public string Datum
        {
            get => datum;
            set => SetProperty(ref datum, value);
        }

        public string Ritningstyp
        {
            get => ritningstyp;
            set => SetProperty(ref ritningstyp, value);
        }

        public string Beskrivning1
        {
            get => beskrivning1;
            set => SetProperty(ref beskrivning1, value);
        }

        public string Beskrivning2
        {
            get => beskrivning2;
            set => SetProperty(ref beskrivning2, value);
        }

        public string Beskrivning3
        {
            get => beskrivning3;
            set => SetProperty(ref beskrivning3, value);
        }

        public string Beskrivning4
        {
            get => beskrivning4;
            set => SetProperty(ref beskrivning4, value);
        }

        public string Uppdrag
        {
            get => uppdrag;
            set => SetProperty(ref uppdrag, value);
        }

        public string Filtyp
        {
            get => filtyp;
            set => SetProperty(ref filtyp, value);
        }

        public string Revidering
        {
            get => revidering;
            set => SetProperty(ref revidering, value);
        }

        public string Sökväg
        {
            get => sökväg;
            set => SetProperty(ref sökväg, value);
        }

        public int DefaultPage
        {
            get => defaultPage;
            set => SetProperty(ref defaultPage, value);
        }

        public ObservableCollection<PageData> FavPages
        {
            get => favPages;
            set
            {
                SetProperty(ref favPages, value);
                OnPropertyChanged(nameof(NameWithAttributes));
            }
        }

        public bool IsFromFolder
        {
            get => isFromFolder;
            set
            {
                SetProperty(ref isFromFolder, value);
                OnPropertyChanged(nameof(NameWithAttributes));
            }
        }

        public string FromFolder
        {
            get => fromFolder;
            set => SetProperty(ref fromFolder, value);
        }

        public string? SyncFolder
        {
            get => syncFolder;
            set => SetProperty(ref syncFolder, value);
        }

        public bool HasBookmarks => FavPages.Count > 0;

        public ObservableCollection<FileData> AppendedFiles
        {
            get => appendedFiles;
            set
            {
                SetProperty(ref appendedFiles, value);
                OnPropertyChanged(nameof(NameWithAttributes));
                OnPropertyChanged(nameof(HasAppendedFiles));
            }
        }

        public ObservableCollection<OtherData> OtherFiles
        {
            get => otherFiles;
            set
            {
                SetProperty(ref otherFiles, value);
                OnPropertyChanged(nameof(NameWithAttributes));
                OnPropertyChanged(nameof(HasAppendedFiles));
            }
        }

        public bool HasAppendedFiles => AppendedFiles.Count > 0 || OtherFiles.Count > 0;

        public string NameWithAttributes
        {
            get
            {
                var nameBuilder = new System.Text.StringBuilder(Namn);

                if (Favorite)
                    nameBuilder.Insert(0, FAVORITE_ICON);

                if (HasAppendedFiles || HasBookmarks || HasNote || HasThumbnail)
                    nameBuilder.Append(SEPARATOR);

                if (HasAppendedFiles) nameBuilder.Append(ATTACHMENT_ICON);
                if (HasBookmarks) nameBuilder.Append(BOOKMARK_ICON);
                if (HasNote) nameBuilder.Append(NOTE_ICON);
                if (HasThumbnail) nameBuilder.Append(THUMBNAIL_ICON);
                if (HasPlainText) nameBuilder.Append(PLAINTEXT_ICON);
                if (IsFromFolder) nameBuilder.Append(FOLDER_ICON);

                return nameBuilder.ToString();
            }
        }

        public string Note
        {
            get => note;
            set
            {
                SetProperty(ref note, value);
                OnPropertyChanged(nameof(NameWithAttributes));
                OnPropertyChanged(nameof(HasNote));
            }
        }

        public bool HasNote => !string.IsNullOrEmpty(Note);

        public bool Favorite
        {
            get => favorite;
            set
            {
                SetProperty(ref favorite, value);
                OnPropertyChanged(nameof(NameWithAttributes));
            }
        }

        public List<string> PartOfCollections
        {
            get => partOfCollections;
            set => SetProperty(ref partOfCollections, value);
        }

        public string ThumbnailSource
        {
            get => thumbnailSource;
            set
            {
                SetProperty(ref thumbnailSource, value);
                OnPropertyChanged(nameof(NameWithAttributes));
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }

        public bool HasThumbnail => !string.IsNullOrEmpty(ThumbnailSource);

        public bool HasPlainText
        {
            get => hasPlainText;
            set
            {
                SetProperty(ref hasPlainText, value);
                OnPropertyChanged(nameof(NameWithAttributes));
            }
        }
        #endregion

        #region Methods
        public bool IsValidPdf()
        {
            return File.Exists(Sökväg) &&
                   Path.GetExtension(Sökväg).Equals(PDF_EXTENSION, StringComparison.OrdinalIgnoreCase);
        }

        public void RemoveThumbnail()
        {
            if (HasThumbnail && File.Exists(thumbnailSource))
            {
                try
                {
                    File.Delete(thumbnailSource);
                    ThumbnailSource = string.Empty;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error removing thumbnail: {ex.Message}");
                }
            }
        }

        public bool IsLocal()
        {
            return !string.IsNullOrEmpty(Sökväg) &&
                   Sökväg.StartsWith("C:", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region Property Changed Implementation
        private bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, newValue))
                return false;

            field = newValue;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        #endregion
    }
}