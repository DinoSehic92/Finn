using Finn.Model;
using Finn.Storage;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Finn.ViewModels
{
    /// <summary>
    /// Manages collections, bookmarks, and favorites.
    /// Extracted from MainViewModel to reduce partial-class coupling.
    /// </summary>
    public class CollectionsViewModel : ViewModelBase
    {
        private readonly Func<ProjectStorage> _storageGetter;
        private readonly Func<PreviewViewModel> _previewGetter;
        private readonly Func<IList<FileData>> _currentFilesGetter;
        private readonly Func<FileData?> _currentFileGetter;
        private readonly Action? _markDirty;

        public CollectionsViewModel(
            Func<ProjectStorage> storageGetter,
            Func<PreviewViewModel> previewGetter,
            Func<IList<FileData>> currentFilesGetter,
            Func<FileData?> currentFileGetter,
            Action? markDirty = null)
            : base(logger: null)
        {
            _storageGetter = storageGetter;
            _previewGetter = previewGetter;
            _currentFilesGetter = currentFilesGetter;
            _currentFileGetter = currentFileGetter;
            _markDirty = markDirty;
        }

        private ProjectStorage Storage => _storageGetter();
        private PreviewViewModel PreviewVM => _previewGetter();
        private IList<FileData> CurrentFiles => _currentFilesGetter();
        private FileData? CurrentFile => _currentFileGetter();

        #region Collection State

        private string currentCollection = string.Empty;
        public string CurrentCollection
        {
            get => currentCollection;
            set { currentCollection = value; OnPropertyChanged(nameof(CurrentCollection)); SetCollectionContent(); }
        }

        private ObservableCollection<CollectionEntryViewModel> collectionContent = new();
        public ObservableCollection<CollectionEntryViewModel> CollectionContent
        {
            get => collectionContent;
            set { collectionContent = value; OnPropertyChanged(nameof(CollectionContent)); }
        }

        private CollectionEntryViewModel? selectedCollectionEntry;
        public CollectionEntryViewModel? SelectedCollectionEntry
        {
            get => selectedCollectionEntry;
            set
            {
                selectedCollectionEntry = value;
                OnPropertyChanged(nameof(SelectedCollectionEntry));
                OnPropertyChanged(nameof(CanMoveCollectionEntryUp));
                OnPropertyChanged(nameof(CanMoveCollectionEntryDown));
            }
        }

        public bool CanMoveCollectionEntryUp =>
            SelectedCollectionEntry != null && CollectionContent.IndexOf(SelectedCollectionEntry) > 0;

        public bool CanMoveCollectionEntryDown =>
            SelectedCollectionEntry != null
            && CollectionContent.IndexOf(SelectedCollectionEntry) >= 0
            && CollectionContent.IndexOf(SelectedCollectionEntry) < CollectionContent.Count - 1;

        public bool CanBindCollection =>
            !string.IsNullOrEmpty(CurrentCollection) && CollectionContent.Count >= 2;

        #endregion

        #region Bookmark State

        private PageData? favPage;
        public PageData? FavPage
        {
            get => favPage;
            set { favPage = value; OnPropertyChanged(nameof(FavPage)); TrySetPage(); }
        }

        #endregion

        #region Bookmarks

        public void SetBookmark(PageData page)
        {
            FavPage = page;
        }

        public void AddBookmark(string pageName)
        {
            int pageNr = PreviewVM.CurrentPage1;
            PageData page = new() { PageNr = pageNr, PageName = pageName };

            if (PreviewVM.CurrentFile != null)
            {
                PreviewVM.CurrentFile.FavPages.Add(page);
            }

            SortBookmarks();
            _markDirty?.Invoke();
        }

        public void ReplaceVersionWithFile(FileData owner, FileVersionData version, FileData replacement)
        {
            foreach (string collection in version.PartOfCollections)
            {
                if (!replacement.PartOfCollections.Contains(collection))
                    replacement.PartOfCollections.Add(collection);

                if (!Storage.CollectionOrders.TryGetValue(collection, out var order) || order == null)
                    continue;

                string versionId = CollectionEntryViewModel.GetVersionId(owner.Id, version.Id);
                int index = order.FindIndex(id => string.Equals(id, versionId, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    order[index] = replacement.Id;
                else if (!order.Contains(replacement.Id, StringComparer.OrdinalIgnoreCase))
                    order.Add(replacement.Id);
            }
        }

        public void RenameBookmark(string pageName)
        {
            if (FavPage != null)
            {
                FavPage.PageName = pageName;
                _markDirty?.Invoke();
            }
        }

        public void RemoveBookmark(PageData page)
        {
            if (PreviewVM.CurrentFile != null)
            {
                PreviewVM.CurrentFile.FavPages.Remove(page);
            }

            SortBookmarks();
            _markDirty?.Invoke();
        }

        /// <summary>
        /// Imports a list of (page, title) pairs from the PDF outline into the user bookmark tray.
        /// Entries whose page+title already exist as a bookmark are silently skipped.
        /// Returns the number of bookmarks actually added.
        /// </summary>
        public int ImportBookmarks(IEnumerable<(int Page, string Title)> items)
        {
            if (PreviewVM.CurrentFile == null) return 0;

            var existing = PreviewVM.CurrentFile.FavPages;
            int added = 0;

            foreach (var (page, title) in items)
            {
                bool duplicate = existing.Any(b => b.PageNr == page && b.PageName == title);
                if (duplicate) continue;

                existing.Add(new PageData { PageNr = page, PageName = title });
                added++;
            }

            if (added > 0)
            {
                SortBookmarks();
                _markDirty?.Invoke();
            }

            return added;
        }

        private void SortBookmarks()
        {
            if (PreviewVM.CurrentFile == null) return;
            List<PageData> tempList = PreviewVM.CurrentFile.FavPages.OrderBy(x => x.PageNr).ToList();
            PreviewVM.CurrentFile.FavPages.Clear();
            PreviewVM.CurrentFile.FavPages = new ObservableCollection<PageData>(tempList);
        }

        public void TrySetPage()
        {
            if (FavPage != null)
            {
                PreviewVM.RequestPage1 = FavPage.PageNr;
            }
        }

        #endregion

        #region Favorites

        public void MarkFavorite()
        {
            if (CurrentFiles == null) return;
            foreach (FileData file in CurrentFiles)
            {
                file.Favorite = !file.Favorite;
            }
            _markDirty?.Invoke();
        }

        #endregion

        #region Collections

        public void NewCollection(string name)
        {
            Storage.Collections.Add(name);
            Storage.CollectionOrders[name] = new List<string>();
            _markDirty?.Invoke();
        }

        public void RemoveCollection()
        {
            foreach (var entry in CollectionContent)
                entry.PartOfCollections.Remove(CurrentCollection);

            Storage.Collections.Remove(CurrentCollection);
            Storage.CollectionOrders.Remove(CurrentCollection);
            CurrentCollection = string.Empty;
            _markDirty?.Invoke();
        }

        public void AddFileToCollection(string collection)
        {
            AddEntriesToCollection(CurrentFiles.Select(file => file.PartOfCollections), collection);
        }

        public void AddFilesToCollection(IEnumerable<FileData> files, string collection)
        {
            AddEntriesToCollection(files.Select(file => file.PartOfCollections), collection);
        }

        public void AddVersionToCollection(FileData file, FileVersionData version, string collection)
        {
            if (!file.Versions.Contains(version))
                return;
            AddEntriesToCollection([version.PartOfCollections], collection);
        }

        private void AddEntriesToCollection(IEnumerable<ObservableCollection<string>> memberships, string collection)
        {
            foreach (var membership in memberships)
            {
                if (!membership.Contains(collection))
                    membership.Add(collection);
            }
            EnsureCollectionOrder(collection);
            CurrentCollection = collection;
            _markDirty?.Invoke();
        }

        public void RemoveEntryFromCollection()
        {
            var target = SelectedCollectionEntry
                ?? CollectionContent.FirstOrDefault(entry => entry.Version == null && entry.File == CurrentFile);
            if (target != null)
            {
                target.PartOfCollections.Remove(CurrentCollection);
                if (Storage.CollectionOrders.TryGetValue(CurrentCollection, out var order) && order != null)
                    order.RemoveAll(id => string.Equals(id, target.Id, StringComparison.OrdinalIgnoreCase));
            }
            SetCollectionContent();
            _markDirty?.Invoke();
        }

        public void SetCollectionContent()
        {
            var selectedId = SelectedCollectionEntry?.Id;
            ClearCollectionContent();
            SelectedCollectionEntry = null;

            if (string.IsNullOrEmpty(CurrentCollection))
            {
                OnPropertyChanged(nameof(CanMoveCollectionEntryUp));
                OnPropertyChanged(nameof(CanMoveCollectionEntryDown));
                OnPropertyChanged(nameof(CanBindCollection));
                return;
            }

            var members = CreateCollectionEntries(CurrentCollection);

            if (!Storage.CollectionOrders.TryGetValue(CurrentCollection, out var order) || order == null)
            {
                order = new List<string>();
                Storage.CollectionOrders[CurrentCollection] = order;
            }

            var membersById = members.ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
            var normalizedOrder = order
                .Where(id => membersById.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            normalizedOrder.AddRange(members
                .Where(entry => !normalizedOrder.Contains(entry.Id, StringComparer.OrdinalIgnoreCase))
                .Select(entry => entry.Id));

            order.Clear();
            order.AddRange(normalizedOrder);

            foreach (string id in normalizedOrder)
                CollectionContent.Add(membersById[id]);

            SelectedCollectionEntry = selectedId == null
                ? null
                : CollectionContent.FirstOrDefault(entry =>
                    string.Equals(entry.Id, selectedId, StringComparison.OrdinalIgnoreCase));

            OnPropertyChanged(nameof(CanMoveCollectionEntryUp));
            OnPropertyChanged(nameof(CanMoveCollectionEntryDown));
            OnPropertyChanged(nameof(CanBindCollection));
        }

        public void MoveSelectedEntryUp() => MoveSelectedEntry(-1);

        public void MoveSelectedEntryDown() => MoveSelectedEntry(1);

        private void MoveSelectedEntry(int offset)
        {
            if (SelectedCollectionEntry == null
                || !Storage.CollectionOrders.TryGetValue(CurrentCollection, out var order)
                || order == null)
                return;

            int index = order.FindIndex(id => string.Equals(id, SelectedCollectionEntry.Id, StringComparison.OrdinalIgnoreCase));
            int targetIndex = index + offset;
            if (index < 0 || targetIndex < 0 || targetIndex >= order.Count)
                return;

            string selectedId = SelectedCollectionEntry.Id;
            (order[index], order[targetIndex]) = (order[targetIndex], order[index]);
            SetCollectionContent();
            SelectedCollectionEntry = CollectionContent.FirstOrDefault(entry => string.Equals(entry.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            _markDirty?.Invoke();
        }

        private void EnsureCollectionOrder(string collection)
        {
            if (!Storage.CollectionOrders.TryGetValue(collection, out var order) || order == null)
            {
                Storage.CollectionOrders[collection] = new List<string>();
                order = Storage.CollectionOrders[collection];
            }
            var entries = CreateCollectionEntries(collection);
            foreach (var entry in entries)
            {
                if (!order.Contains(entry.Id, StringComparer.OrdinalIgnoreCase))
                    order.Add(entry.Id);
                entry.Detach();
            }
        }

        private List<CollectionEntryViewModel> CreateCollectionEntries(string collection)
        {
            var entries = new List<CollectionEntryViewModel>();
            foreach (var file in Storage.StoredProjects.SelectMany(project => project.AllFiles))
            {
                if (file.PartOfCollections.Contains(collection))
                    entries.Add(new CollectionEntryViewModel(file));

                foreach (var version in file.Versions)
                {
                    if (version.PartOfCollections.Contains(collection))
                        entries.Add(new CollectionEntryViewModel(file, version));
                }
            }
            return entries;
        }

        private void ClearCollectionContent()
        {
            foreach (var entry in CollectionContent)
                entry.Detach();
            CollectionContent.Clear();
            OnPropertyChanged(nameof(CanMoveCollectionEntryUp));
            OnPropertyChanged(nameof(CanMoveCollectionEntryDown));
        }

        public void RenameCollection(string newName)
        {
            if (!Storage.Collections.Contains(newName))
            {
                foreach (var entry in CollectionContent)
                {
                    entry.PartOfCollections.Remove(CurrentCollection);
                    entry.PartOfCollections.Add(newName);
                }

                int index = Storage.Collections.IndexOf(CurrentCollection);
                Storage.Collections[index] = newName;

                if (Storage.CollectionOrders.Remove(CurrentCollection, out var order))
                    Storage.CollectionOrders[newName] = order;

                CurrentCollection = newName;
                _markDirty?.Invoke();
            }
        }

        #endregion
    }
}
