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
            set { selectedCollectionFile = value; OnPropertyChanged(nameof(SelectedCollectionFile)); }
        }

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
            _markDirty?.Invoke();
        }

        public void AddFileToCollection(string collection)
        {
            foreach (FileData file in CurrentFiles.Where(x => !x.PartOfCollections.Contains(collection)))
            {
                file.PartOfCollections.Add(collection);
            }
            CurrentCollection = collection;
            _markDirty?.Invoke();
        }

        public void AddFilesToCollection(IEnumerable<FileData> files, string collection)
        {
            foreach (FileData file in files.Where(x => !x.PartOfCollections.Contains(collection)))
            {
                file.PartOfCollections.Add(collection);
            }
            CurrentCollection = collection;
            _markDirty?.Invoke();
        }

        public void RemoveFileFromCollection()
        {
            var target = SelectedCollectionFile ?? CurrentFile;
            target?.PartOfCollections.Remove(CurrentCollection);
            SetCollectionContent();
            _markDirty?.Invoke();
        }

        public void SetCollectionContent()
        {
            CollectionContent.Clear();

            foreach (ProjectData project in Storage.StoredProjects)
            {
                foreach (FileData file in project.AllFiles.Where(x => x.PartOfCollections.Contains(CurrentCollection)))
                {
                    CollectionContent.Add(file);
                }
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

                CurrentCollection = newName;
                _markDirty?.Invoke();
            }
        }

        #endregion
    }
}
