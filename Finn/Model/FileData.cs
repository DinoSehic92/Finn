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
    /// <summary>
    /// Represents a file and its associated metadata, status, and collections.
    /// </summary>
    public class FileData : INotifyPropertyChanged
    {
        #region Constants
        private const string PdfExtension = ".pdf";
        #endregion

        #region Construction
        public FileData()
        {
            // Ensure we react to collection changes so icon/name caches update when items are added/removed.
            _favPages.CollectionChanged += FavPages_CollectionChanged;
            _appendedFiles.CollectionChanged += AppendedFiles_CollectionChanged;
            _otherFiles.CollectionChanged += OtherFiles_CollectionChanged;
        }

        private void FavPages_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasBookmarks));
        }

        private void AppendedFiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasAppendedFiles));
        }

        private void OtherFiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasAppendedFiles));
        }
        #endregion

        #region Fields
        private string _namn = string.Empty;
        private bool _isFileMissing;
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
        private ObservableCollection<PageData> _favPages = new();
        private bool _isFromFolder;
        private string _fromFolder = string.Empty;
        private string? _syncFolder = string.Empty;
        private ObservableCollection<FileData> _appendedFiles = new();
        private ObservableCollection<OtherData> _otherFiles = new();
        private string _note = string.Empty;
        private bool _favorite;
        private List<string> _partOfCollections = new();
        private string _thumbnailSource = string.Empty;
        private bool _hasPlainText;

        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the file name.
        /// </summary>
        /// <summary>
        /// Gets or sets the file name.
        /// </summary>
        public string Namn
        {
            get => _namn;
            set { SetProperty(ref _namn, value); }
        }

        /// <summary>
        /// Gets or sets the file status.
        /// </summary>
        /// <summary>
        /// Gets or sets the file status.
        /// </summary>
        public bool IsFileMissing
        {
            get => _isFileMissing;
            set => SetProperty(ref _isFileMissing, value);
        }

        /// <summary>
        /// Gets or sets the file tag.
        /// </summary>
        /// <summary>
        /// Gets or sets the file tag.
        /// </summary>
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
            set
            {
                if (EqualityComparer<ObservableCollection<PageData>>.Default.Equals(_favPages, value))
                    return;

                if (_favPages != null)
                    _favPages.CollectionChanged -= FavPages_CollectionChanged;

                _favPages = value ?? new ObservableCollection<PageData>();
                _favPages.CollectionChanged += FavPages_CollectionChanged;

                OnPropertyChanged(nameof(FavPages));
                OnPropertyChanged(nameof(HasBookmarks));
            }
        }

        public bool IsFromFolder
        {
            get => _isFromFolder;
            set { SetProperty(ref _isFromFolder, value); }
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
                if (EqualityComparer<ObservableCollection<FileData>>.Default.Equals(_appendedFiles, value))
                    return;

                if (_appendedFiles != null)
                    _appendedFiles.CollectionChanged -= AppendedFiles_CollectionChanged;

                _appendedFiles = value ?? new ObservableCollection<FileData>();
                _appendedFiles.CollectionChanged += AppendedFiles_CollectionChanged;

                OnPropertyChanged(nameof(AppendedFiles));
                OnPropertyChanged(nameof(HasAppendedFiles));
            }
        }

        public ObservableCollection<OtherData> OtherFiles
        {
            get => _otherFiles;
            set
            {
                if (EqualityComparer<ObservableCollection<OtherData>>.Default.Equals(_otherFiles, value))
                    return;

                if (_otherFiles != null)
                    _otherFiles.CollectionChanged -= OtherFiles_CollectionChanged;

                _otherFiles = value ?? new ObservableCollection<OtherData>();
                _otherFiles.CollectionChanged += OtherFiles_CollectionChanged;

                OnPropertyChanged(nameof(OtherFiles));
                OnPropertyChanged(nameof(HasAppendedFiles));
            }
        }

        public bool HasAppendedFiles => _appendedFiles.Count > 0 || _otherFiles.Count > 0;

        public string Note
        {
            get => _note;
            set
            {
                SetProperty(ref _note, value);
                OnPropertyChanged(nameof(HasNote));
            }
        }

        public bool HasNote => !string.IsNullOrEmpty(_note);

        public bool Favorite
        {
            get => _favorite;
            set { SetProperty(ref _favorite, value); }
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
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }

        public bool HasThumbnail => !string.IsNullOrEmpty(_thumbnailSource);

        public bool HasPlainText
        {
            get => _hasPlainText;
            set { SetProperty(ref _hasPlainText, value); }
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
            if (!HasThumbnail)
                return;

            try
            {
                if (File.Exists(_thumbnailSource))
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