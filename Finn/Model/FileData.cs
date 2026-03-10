using Finn.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            _partOfCollections.CollectionChanged += PartOfCollections_CollectionChanged;
            _versions.CollectionChanged += Versions_CollectionChanged;
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

        private void PartOfCollections_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(IsPartOfCollection));
        }

        private void Versions_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (FileVersionData item in e.OldItems)
                    item.PropertyChanged -= Version_PropertyChanged;
            }

            if (e.NewItems != null)
            {
                foreach (FileVersionData item in e.NewItems)
                    item.PropertyChanged += Version_PropertyChanged;
            }

            // If the active version was removed, fall back to the last remaining version
            // or clear CurrentVersion when no versions are left.
            if (!string.IsNullOrEmpty(_currentVersion)
                && !_versions.Any(v => v.Label == _currentVersion))
            {
                CurrentVersion = _versions.Count > 0
                    ? _versions[^1].Label
                    : string.Empty;
            }

            UpdateVersionActiveStates();
            OnPropertyChanged(nameof(HasVersions));
        }

        private void Version_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileVersionData.Label))
            {
                if (sender is FileVersionData version
                    && !string.IsNullOrEmpty(_currentVersion)
                    && !_versions.Any(v => v.Label == _currentVersion))
                {
                    CurrentVersion = version.Label;
                }
                SortVersions();
                UpdateVersionActiveStates();
            }
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
        private BulkObservableCollection<FileData> _appendedFiles = new();
        private BulkObservableCollection<OtherData> _otherFiles = new();
        private string _note = string.Empty;
        private bool _favorite;
        private ObservableCollection<string> _partOfCollections = new();
        private string _thumbnailSource = string.Empty;
        private bool _hasPlainText;
        private ObservableCollection<FileVersionData> _versions = new();
        private string _currentVersion = string.Empty;

        #endregion

        #region Properties
        /// <summary>
        /// Gets or sets the file name.
        /// </summary>
        public string Namn
        {
            get => _namn;
            set { SetProperty(ref _namn, value); }
        }

        /// <summary>
        /// Gets or sets whether the file is missing from disk.
        /// </summary>
        public bool IsFileMissing
        {
            get => _isFileMissing;
            set => SetProperty(ref _isFileMissing, value);
        }

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

        public BulkObservableCollection<FileData> AppendedFiles
        {
            get => _appendedFiles;
            set
            {
                if (EqualityComparer<BulkObservableCollection<FileData>>.Default.Equals(_appendedFiles, value))
                    return;

                if (_appendedFiles != null)
                    _appendedFiles.CollectionChanged -= AppendedFiles_CollectionChanged;

                _appendedFiles = value ?? new BulkObservableCollection<FileData>();
                _appendedFiles.CollectionChanged += AppendedFiles_CollectionChanged;

                OnPropertyChanged(nameof(AppendedFiles));
                OnPropertyChanged(nameof(HasAppendedFiles));
            }
        }

        public BulkObservableCollection<OtherData> OtherFiles
        {
            get => _otherFiles;
            set
            {
                if (EqualityComparer<BulkObservableCollection<OtherData>>.Default.Equals(_otherFiles, value))
                    return;

                if (_otherFiles != null)
                    _otherFiles.CollectionChanged -= OtherFiles_CollectionChanged;

                _otherFiles = value ?? new BulkObservableCollection<OtherData>();
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

        public ObservableCollection<string> PartOfCollections
        {
            get => _partOfCollections;
            set
            {
                if (_partOfCollections != null)
                    _partOfCollections.CollectionChanged -= PartOfCollections_CollectionChanged;

                _partOfCollections = value ?? new ObservableCollection<string>();
                _partOfCollections.CollectionChanged += PartOfCollections_CollectionChanged;

                OnPropertyChanged(nameof(PartOfCollections));
                OnPropertyChanged(nameof(IsPartOfCollection));
            }
        }

        public bool IsPartOfCollection => _partOfCollections.Count > 0;

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

        public ObservableCollection<FileVersionData> Versions
        {
            get => _versions;
            set
            {
                if (EqualityComparer<ObservableCollection<FileVersionData>>.Default.Equals(_versions, value))
                    return;

                if (_versions != null)
                {
                    _versions.CollectionChanged -= Versions_CollectionChanged;
                    foreach (var item in _versions)
                        item.PropertyChanged -= Version_PropertyChanged;
                }

                _versions = value ?? new ObservableCollection<FileVersionData>();
                _versions.CollectionChanged += Versions_CollectionChanged;
                foreach (var item in _versions)
                    item.PropertyChanged += Version_PropertyChanged;

                OnPropertyChanged(nameof(Versions));
                OnPropertyChanged(nameof(HasVersions));
                UpdateVersionActiveStates();
            }
        }

        public bool HasVersions => _versions.Count > 0;

        public string CurrentVersion
        {
            get => _currentVersion;
            set
            {
                if (SetProperty(ref _currentVersion, value))
                {
                    if (!string.IsNullOrEmpty(value))
                    {
                        var version = _versions.FirstOrDefault(v => v.Label == value);
                        if (version != null)
                            Sökväg = version.Sökväg;
                    }
                    UpdateVersionActiveStates();
                }
            }
        }
        #endregion

        #region Methods
        /// <summary>
        /// Refreshes the <see cref="FileVersionData.IsActive"/> flag on every
        /// version so that only the current version appears highlighted.
        /// </summary>
        private void UpdateVersionActiveStates()
        {
            foreach (var v in _versions)
                v.IsActive = !string.IsNullOrEmpty(_currentVersion) && v.Label == _currentVersion;
        }

        /// <summary>
        /// Removes a version entry. When only one version remains after the
        /// removal, version tracking is cleared entirely so the file behaves
        /// as a non-versioned file.
        /// </summary>
        public void RemoveVersion(FileVersionData version)
        {
            _versions.Remove(version);

            if (_versions.Count == 1)
            {
                var last = _versions[0];
                last.PropertyChanged -= Version_PropertyChanged;
                Sökväg = last.Sökväg;
                _currentVersion = string.Empty;

                _versions.CollectionChanged -= Versions_CollectionChanged;
                _versions.Clear();
                _versions.CollectionChanged += Versions_CollectionChanged;

                OnPropertyChanged(nameof(CurrentVersion));
                OnPropertyChanged(nameof(HasVersions));
            }
        }

        public bool IsValidPdf()
        {
            return !string.IsNullOrEmpty(_sökväg)
                && _sökväg.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase)
                && File.Exists(_sökväg);
        }

        /// <summary>
        /// Returns all unique PDF paths for this file, including every version.
        /// When no versions exist the current Sökväg is returned if it is a valid PDF.
        /// </summary>
        public List<string> AllPdfPaths(bool checkExists = true)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var v in _versions)
            {
                if (!string.IsNullOrEmpty(v.Sökväg)
                    && v.Sökväg.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase)
                    && (!checkExists || File.Exists(v.Sökväg)))
                {
                    paths.Add(v.Sökväg);
                }
            }

            if (paths.Count == 0
                && !string.IsNullOrEmpty(_sökväg)
                && _sökväg.EndsWith(PdfExtension, StringComparison.OrdinalIgnoreCase)
                && (!checkExists || File.Exists(_sökväg)))
                paths.Add(_sökväg);

            return paths.ToList();
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

        /// <summary>
        /// Registers a new version path for this file. On the first call the existing
        /// Sökväg is also recorded as the original so the history is complete.
        /// </summary>
        public void AddVersion(string filepath, string label)
        {
            // Skip if this path is already the current file or already registered as a version
            if (filepath == _sökväg || _versions.Any(v => v.Sökväg == filepath))
                return;

            if (_versions.Count == 0)
            {
                _versions.Add(new FileVersionData
                {
                    Sökväg = _sökväg,
                    Label = "ORIGINAL",
                    AddedDate = DateTime.Now.ToString("yyyy-MM-dd")
                });
            }

            _versions.Add(new FileVersionData
            {
                Sökväg = filepath,
                Label = ResolveUniqueLabel(label),
                AddedDate = DateTime.Now.ToString("yyyy-MM-dd")
            });
            SortVersions();
            CurrentVersion = _versions[^1].Label;
        }

        /// <summary>
        /// Assigns <paramref name="label"/> to <paramref name="version"/>, resolving any
        /// conflict with sibling versions so no two versions share the same label.
        /// </summary>
        public void SetVersionLabel(FileVersionData version, string label)
        {
            if (_versions.Contains(version))
                version.Label = ResolveUniqueLabel(label, exclude: version);
        }

        /// <summary>
        /// Returns <paramref name="label"/> if unused (ignoring <paramref name="exclude"/>),
        /// otherwise finds the next unused label from <see cref="FileVersionData.VersionLabels"/>,
        /// or appends a numeric suffix as a last resort (e.g. "NEW 2", "NEW 3").
        /// </summary>
        private string ResolveUniqueLabel(string label, FileVersionData? exclude = null)
        {
            if (!_versions.Any(v => v != exclude && v.Label == label))
                return label;

            foreach (string candidate in FileVersionData.VersionLabels)
            {
                if (!_versions.Any(v => v != exclude && v.Label == candidate))
                    return candidate;
            }

            int n = 2;
            string unique;
            do { unique = $"{label} {n++}"; }
            while (_versions.Any(v => v != exclude && v.Label == unique));
            return unique;
        }

        private bool _sortingVersions;

        /// <summary>
        /// Sorts <see cref="Versions"/> in-place according to the predefined label order.
        /// Versions with unrecognised labels are placed after the known ones.
        /// </summary>
        public void SortVersions()
        {
            if (_sortingVersions || _versions.Count <= 1) return;
            _sortingVersions = true;
            try
            {
                var order = FileVersionData.LabelOrder;
                var sorted = _versions
                    .OrderBy(v => order.TryGetValue(v.Label, out int idx) ? idx : int.MaxValue)
                    .ToList();
                for (int i = 0; i < sorted.Count; i++)
                {
                    int from = _versions.IndexOf(sorted[i]);
                    if (from != i) _versions.Move(from, i);
                }
            }
            finally
            {
                _sortingVersions = false;
            }
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