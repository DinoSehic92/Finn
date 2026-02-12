using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace Finn.Model
{
    public class FileData : INotifyPropertyChanged
    {
        #region Constants
        private const string FavoriteIcon = "⭐ ";
        private const string Separator = "⠀";
        private const string AttachmentIcon = "📎";
        private const string BookmarkIcon = "🔖";
        private const string NoteIcon = " 📝";
        private const string ThumbnailIcon = " ؞";
        private const string PlaintextIcon = "⠀🌐";
        private const string FolderIcon = "⠀🗀";
        private const string PdfExtension = ".pdf";
        #endregion

        #region Fields
        private string _namn = string.Empty;
        private string _fileStatus = string.Empty;
        private string _tagg = string.Empty;
        private string _färg = string.Empty;
        private string _handling = string.Empty;
        private string _status = string.Empty;
        private string _datum = string.Empty;
        private string _ritningstyp = string.Empty;
        private string _beskrivning1 = string.Empty;
        private string _beskrivning2 = string.Empty;
        private string _beskrivning3 = string.Empty;
        private string _beskrivning4 = string.Empty;
        private string _uppdrag = string.Empty;
        private string _filtyp = string.Empty;
        private string _revidering = string.Empty;
        private string _sökväg = string.Empty;
        private int _defaultPage;
        private ObservableCollection<PageData> _favPages = [];
        private bool _isFromFolder;
        private string _fromFolder = string.Empty;
        private string? _syncFolder = string.Empty;
        private ObservableCollection<FileData> _appendedFiles = [];
        private ObservableCollection<OtherData> _otherFiles = [];
        private string _note = string.Empty;
        private bool _favorite;
        private List<string> _partOfCollections = [];
        private string _thumbnailSource = string.Empty;
        private bool _hasPlainText;
        private string? _cachedNameWithAttributes;
        #endregion

        #region Properties
        public string Namn
        {
            get => _namn;
            set { SetProperty(ref _namn, value); InvalidateNameWithAttributes(); }
        }

        public string FileStatus
        {
            get => _fileStatus;
            set => SetProperty(ref _fileStatus, value);
        }

        public string Tagg
        {
            get => _tagg;
            set => SetProperty(ref _tagg, value);
        }

        public string Färg
        {
            get => _färg;
            set => SetProperty(ref _färg, value);
        }

        public string Handling
        {
            get => _handling;
            set => SetProperty(ref _handling, value);
        }

        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string Datum
        {
            get => _datum;
            set => SetProperty(ref _datum, value);
        }

        public string Ritningstyp
        {
            get => _ritningstyp;
            set => SetProperty(ref _ritningstyp, value);
        }

        public string Beskrivning1
        {
            get => _beskrivning1;
            set => SetProperty(ref _beskrivning1, value);
        }

        public string Beskrivning2
        {
            get => _beskrivning2;
            set => SetProperty(ref _beskrivning2, value);
        }

        public string Beskrivning3
        {
            get => _beskrivning3;
            set => SetProperty(ref _beskrivning3, value);
        }

        public string Beskrivning4
        {
            get => _beskrivning4;
            set => SetProperty(ref _beskrivning4, value);
        }

        public string Uppdrag
        {
            get => _uppdrag;
            set => SetProperty(ref _uppdrag, value);
        }

        public string Filtyp
        {
            get => _filtyp;
            set => SetProperty(ref _filtyp, value);
        }

        public string Revidering
        {
            get => _revidering;
            set => SetProperty(ref _revidering, value);
        }

        public string Sökväg
        {
            get => _sökväg;
            set => SetProperty(ref _sökväg, value);
        }

        public int DefaultPage
        {
            get => _defaultPage;
            set => SetProperty(ref _defaultPage, value);
        }

        public ObservableCollection<PageData> FavPages
        {
            get => _favPages;
            set { SetProperty(ref _favPages, value); InvalidateNameWithAttributes(); }
        }

        public bool IsFromFolder
        {
            get => _isFromFolder;
            set { SetProperty(ref _isFromFolder, value); InvalidateNameWithAttributes(); }
        }

        public string FromFolder
        {
            get => _fromFolder;
            set => SetProperty(ref _fromFolder, value);
        }

        public string? SyncFolder
        {
            get => _syncFolder;
            set => SetProperty(ref _syncFolder, value);
        }

        public bool HasBookmarks => _favPages.Count > 0;

        public ObservableCollection<FileData> AppendedFiles
        {
            get => _appendedFiles;
            set
            {
                SetProperty(ref _appendedFiles, value);
                InvalidateNameWithAttributes();
                OnPropertyChanged(nameof(HasAppendedFiles));
            }
        }

        public ObservableCollection<OtherData> OtherFiles
        {
            get => _otherFiles;
            set
            {
                SetProperty(ref _otherFiles, value);
                InvalidateNameWithAttributes();
                OnPropertyChanged(nameof(HasAppendedFiles));
            }
        }

        public bool HasAppendedFiles => _appendedFiles.Count > 0 || _otherFiles.Count > 0;

        public string NameWithAttributes
        {
            get
            {
                if (_cachedNameWithAttributes != null)
                    return _cachedNameWithAttributes;

                var sb = new StringBuilder(_namn.Length + 32);

                if (_favorite)
                    sb.Append(FavoriteIcon);

                sb.Append(_namn);

                bool hasDecorations = HasAppendedFiles || HasBookmarks || HasNote || HasThumbnail;
                if (hasDecorations)
                    sb.Append(Separator);

                if (HasAppendedFiles) sb.Append(AttachmentIcon);
                if (HasBookmarks) sb.Append(BookmarkIcon);
                if (HasNote) sb.Append(NoteIcon);
                if (HasThumbnail) sb.Append(ThumbnailIcon);
                if (_hasPlainText) sb.Append(PlaintextIcon);
                if (_isFromFolder) sb.Append(FolderIcon);

                _cachedNameWithAttributes = sb.ToString();
                return _cachedNameWithAttributes;
            }
        }

        public string Note
        {
            get => _note;
            set
            {
                SetProperty(ref _note, value);
                InvalidateNameWithAttributes();
                OnPropertyChanged(nameof(HasNote));
            }
        }

        public bool HasNote => !string.IsNullOrEmpty(_note);

        public bool Favorite
        {
            get => _favorite;
            set { SetProperty(ref _favorite, value); InvalidateNameWithAttributes(); }
        }

        public List<string> PartOfCollections
        {
            get => _partOfCollections;
            set => SetProperty(ref _partOfCollections, value);
        }

        public string ThumbnailSource
        {
            get => _thumbnailSource;
            set
            {
                SetProperty(ref _thumbnailSource, value);
                InvalidateNameWithAttributes();
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }

        public bool HasThumbnail => !string.IsNullOrEmpty(_thumbnailSource);

        public bool HasPlainText
        {
            get => _hasPlainText;
            set { SetProperty(ref _hasPlainText, value); InvalidateNameWithAttributes(); }
        }
        #endregion

        #region Methods
        public bool IsValidPdf()
        {
            return !string.IsNullOrEmpty(_sökväg)
                && _sökväg.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase)
                && File.Exists(_sökväg);
        }

        public void RemoveThumbnail()
        {
            if (!HasThumbnail || !File.Exists(_thumbnailSource))
                return;

            try
            {
                File.Delete(_thumbnailSource);
                ThumbnailSource = string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing thumbnail: {ex.Message}");
            }
        }

        public bool IsLocal()
        {
            return !string.IsNullOrEmpty(_sökväg)
                && _sökväg.StartsWith("C:", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region Property Changed Implementation
        /// <summary>
        /// Invalidates the cached display name and raises PropertyChanged.
        /// </summary>
        private void InvalidateNameWithAttributes()
        {
            _cachedNameWithAttributes = null;
            OnPropertyChanged(nameof(NameWithAttributes));
        }

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