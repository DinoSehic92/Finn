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
                        if (filepaths.Contains(file.Sökväg))
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
                        bool finished = false;

                        string b0 = file.Namn;
                        string b1 = file.Beskrivning1;
                        string b2 = file.Beskrivning2;
                        string b3 = file.Beskrivning3;
                        string b4 = file.Tagg;

                        if (b0 != null && !finished) { if (b0.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b1 != null && !finished) { if (b1.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b2 != null && !finished) { if (b2.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b3 != null && !finished) { if (b3.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                        if (b4 != null && !finished) { if (b4.ToLower().Contains(SearchText.ToLower())) { FilteredFiles.Add(file); finished = true; } }
                    }
                }

                OnPropertyChanged("NrFilteredFiles");

            }
        }
    }
