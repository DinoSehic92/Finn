using Finn.Model;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            public void Search()
            {
                if (IndexedSearch)
                {
                    SearchIndex();
                }
                else
                {
                    SearchFiles();
                }

                OnPropertyChanged("UpdateColumns");
            }

            public void SearchIndex()
            {
                string indexPath = Path.Combine(SavePath, "Content.json");

                if (TextContent == null)
                {
                    if (System.IO.File.Exists(indexPath))
                    {
                        LoadIndexFile(indexPath);
                    }
                    else
                    {
                        TextContent = new ObservableCollection<ContentData>();
                    }
                }

                FilteredFiles.Clear();
                CurrentProject = new ProjectData() { Namn = SearchText, Category = SEARCH_CATEGORY };

                System.Collections.Generic.List<string> filepaths = new();

                foreach (ContentData content in TextContent)
                {
                    if (content.PlainText.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        filepaths.Add(content.Filepath);
                    }
                }

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        var paths = file.AllPdfPaths();
                        if (paths.Any(p => filepaths.Contains(p)))
                        {
                            FilteredFiles.Add(file);
                        }
                    }
                }

                OnPropertyChanged(nameof(NrFilteredFiles));
            }

            public void SearchFiles()
            {
                FilteredFiles.Clear();
                CurrentProject = new ProjectData() { Namn = SearchText, Category = "Search" };

                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        string?[] fields = [file.Namn, file.Beskrivning1, file.Beskrivning2, file.Beskrivning3, file.Tagg];

                        if (fields.Any(f => f?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true))
                            FilteredFiles.Add(file);
                    }
                }

                OnPropertyChanged(nameof(NrFilteredFiles));
            }
        }
    }
