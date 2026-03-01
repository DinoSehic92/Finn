using Finn.Model;
using System.Collections.ObjectModel;
using System.Linq;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
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
            }

            public void RenameBookmark(string pageName)
            {
                if (FavPage != null)
                {
                    FavPage.PageName = pageName;
                }
            }

            public void RemoveBookmark(PageData page)
            {
                if (PreviewVM.CurrentFile != null)
                {
                    PreviewVM.CurrentFile.FavPages.Remove(page);
                }

                SortBookmarks();
            }

            private void SortBookmarks()
            {
                System.Collections.Generic.List<PageData> tempList = PreviewVM.CurrentFile.FavPages.OrderBy(x => x.PageNr).ToList();
                PreviewVM.CurrentFile.FavPages.Clear();
                PreviewVM.CurrentFile.FavPages = new ObservableCollection<PageData>(tempList);
            }

            public void MarkFavorite()
            {
                foreach (FileData file in CurrentFiles)
                {
                    file.Favorite = !file.Favorite;
                }
            }

            public void NewCollection(string name)
            {
                Storage.Collections.Add(name);
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
            }

            public void AddFileToCollection(string collection)
            {
                foreach (FileData file in CurrentFiles.Where(x => !x.PartOfCollections.Contains(collection)))
                {
                    file.PartOfCollections.Add(collection);
                }
                CurrentCollection = collection;
            }

            public void RemoveFileFromCollection()
            {
                CurrentFile.PartOfCollections.Remove(CurrentCollection);
                SetCollectionContent();
            }

            public void SetCollectionContent()
            {
                CollectionContent.Clear();

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles.Where(x => x.PartOfCollections.Contains(CurrentCollection)))
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
                }
            }
        }
    }
