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

        private ObservableCollection<FileData> collectionContent = new();
        public ObservableCollection<FileData> CollectionContent
        {
            get => collectionContent;
            set { collectionContent = value; OnPropertyChanged(nameof(CollectionContent)); }
        }

        private FileData? selectedCollectionFile;
        public FileData? SelectedCollectionFile
        {
            get => selectedCollectionFile;
            set
            {
                selectedCollectionFile = value;
                OnPropertyChanged(nameof(SelectedCollectionFile));
                OnPropertyChanged(nameof(CanMoveCollectionFileUp));
                OnPropertyChanged(nameof(CanMoveCollectionFileDown));
            }
        }

        public bool CanMoveCollectionFileUp =>
            SelectedCollectionFile != null && CollectionContent.IndexOf(SelectedCollectionFile) > 0;

        public bool CanMoveCollectionFileDown =>
            SelectedCollectionFile != null
            && CollectionContent.IndexOf(SelectedCollectionFile) >= 0
            && CollectionContent.IndexOf(SelectedCollectionFile) < CollectionContent.Count - 1;

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
            if (CollectionContent.Count > 0)
            {
                foreach (FileData file in CollectionContent)
                {
                    file.PartOfCollections.Remove(CurrentCollection);
                }
            }
            Storage.Collections.Remove(CurrentCollection);
            Storage.CollectionOrders.Remove(CurrentCollection);
            _markDirty?.Invoke();
        }

        public void AddFileToCollection(string collection)
        {
            foreach (FileData file in CurrentFiles.Where(x => !x.PartOfCollections.Contains(collection)))
            {
                file.PartOfCollections.Add(collection);
            }
            EnsureCollectionOrder(collection);
            CurrentCollection = collection;
            _markDirty?.Invoke();
        }

        public void AddFilesToCollection(IEnumerable<FileData> files, string collection)
        {
            foreach (FileData file in files.Where(x => !x.PartOfCollections.Contains(collection)))
            {
                file.PartOfCollections.Add(collection);
            }
            EnsureCollectionOrder(collection);
            CurrentCollection = collection;
            _markDirty?.Invoke();
        }

        public void RemoveFileFromCollection()
        {
            var target = SelectedCollectionFile ?? CurrentFile;
            if (target != null)
            {
                target.PartOfCollections.Remove(CurrentCollection);
                if (Storage.CollectionOrders.TryGetValue(CurrentCollection, out var order) && order != null)
                    order.Remove(target.Id);
            }
            SetCollectionContent();
            _markDirty?.Invoke();
        }

        public void SetCollectionContent()
        {
            CollectionContent.Clear();
            SelectedCollectionFile = null;

            var members = Storage.StoredProjects
                .SelectMany(project => project.AllFiles)
                .Where(file => file.PartOfCollections.Contains(CurrentCollection))
                .ToList();

            if (string.IsNullOrEmpty(CurrentCollection))
            {
                OnPropertyChanged(nameof(CanMoveCollectionFileUp));
                OnPropertyChanged(nameof(CanMoveCollectionFileDown));
                OnPropertyChanged(nameof(CanBindCollection));
                return;
            }

            if (!Storage.CollectionOrders.TryGetValue(CurrentCollection, out var order) || order == null)
            {
                order = new List<string>();
                Storage.CollectionOrders[CurrentCollection] = order;
            }

            var membersById = members.ToDictionary(file => file.Id, StringComparer.OrdinalIgnoreCase);
            var normalizedOrder = order
                .Where(id => membersById.ContainsKey(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            normalizedOrder.AddRange(members
                .Where(file => !normalizedOrder.Contains(file.Id, StringComparer.OrdinalIgnoreCase))
                .Select(file => file.Id));

            order.Clear();
            order.AddRange(normalizedOrder);

            foreach (string id in normalizedOrder)
                CollectionContent.Add(membersById[id]);

            OnPropertyChanged(nameof(CanMoveCollectionFileUp));
            OnPropertyChanged(nameof(CanMoveCollectionFileDown));
            OnPropertyChanged(nameof(CanBindCollection));
        }

        public void MoveSelectedFileUp() => MoveSelectedFile(-1);

        public void MoveSelectedFileDown() => MoveSelectedFile(1);

        private void MoveSelectedFile(int offset)
        {
            if (SelectedCollectionFile == null
                || !Storage.CollectionOrders.TryGetValue(CurrentCollection, out var order)
                || order == null)
                return;

            int index = order.IndexOf(SelectedCollectionFile.Id);
            int targetIndex = index + offset;
            if (index < 0 || targetIndex < 0 || targetIndex >= order.Count)
                return;

            string selectedId = SelectedCollectionFile.Id;
            (order[index], order[targetIndex]) = (order[targetIndex], order[index]);
            SetCollectionContent();
            SelectedCollectionFile = CollectionContent.FirstOrDefault(file => file.Id == selectedId);
            _markDirty?.Invoke();
        }

        private void EnsureCollectionOrder(string collection)
        {
            if (!Storage.CollectionOrders.TryGetValue(collection, out var order) || order == null)
            {
                Storage.CollectionOrders[collection] = new List<string>();
                order = Storage.CollectionOrders[collection];
            }
            foreach (var file in Storage.StoredProjects
                .SelectMany(project => project.AllFiles)
                .Where(file => file.PartOfCollections.Contains(collection)))
            {
                if (!order.Contains(file.Id, StringComparer.OrdinalIgnoreCase))
                    order.Add(file.Id);
            }
        }

        public void RenameCollection(string newName)
        {
            if (!Storage.Collections.Contains(newName))
            {
                foreach (FileData file in CollectionContent)
                {
                    file.PartOfCollections.Remove(CurrentCollection);
                    file.PartOfCollections.Add(newName);
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
