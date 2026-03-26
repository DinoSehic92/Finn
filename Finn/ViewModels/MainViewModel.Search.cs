using Finn.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            public async Task Search()
            {
                if (IndexedSearch)
                {
                    await SearchIndexAsync();
                }
                else
                {
                    SearchFiles();
                }

                SignalColumnsChanged();
            }

            public async Task SearchIndexAsync()
            {
                string indexPath = Path.Combine(SavePath, "Content.json");

                if (Data.TextContent == null)
                {
                    if (System.IO.File.Exists(indexPath))
                    {
                        await Data.LoadIndexFileAsync(indexPath);
                    }
                    else
                    {
                        Data.TextContent = new ObservableCollection<ContentData>();
                    }
                }

                // Use backing field to avoid triggering UpdateFilter from the
                // CurrentProject property setter — we populate FilteredFiles manually.
                currentProject = new ProjectData() { Namn = SearchText, Category = SEARCH_CATEGORY };
                OnPropertyChanged(nameof(CurrentProject));
                OnPropertyChanged(nameof(IsSearchResult));

                // HashSet for O(1) path lookups instead of O(n) List.Contains
                HashSet<string> filepaths = new(StringComparer.OrdinalIgnoreCase);

                foreach (ContentData content in Data.TextContent)
                {
                    if (content.PlainText.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        filepaths.Add(content.Filepath);
                    }
                }

                var matches = new List<FileData>();
                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles.Where(f => !f.IsAppendedFile))
                    {
                        var paths = file.AllPdfPaths(checkExists: false);
                        if (paths.Any(p => filepaths.Contains(p)))
                        {
                            matches.Add(file);
                        }
                    }
                }

                filteredFiles.ReplaceAll(matches);
                OnPropertyChanged(nameof(NrFilteredFiles));
            }

            public void SearchFiles()
            {
                // Use backing field to avoid triggering UpdateFilter from the
                // CurrentProject property setter — we populate FilteredFiles manually.
                currentProject = new ProjectData() { Namn = SearchText, Category = "Search" };
                OnPropertyChanged(nameof(CurrentProject));
                OnPropertyChanged(nameof(IsSearchResult));

                var matches = new List<FileData>();
                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles.Where(f => !f.IsAppendedFile))
                    {
                        string?[] fields = [file.Namn, file.Beskrivning1, file.Beskrivning2, file.Beskrivning3, file.Tagg];

                        if (fields.Any(f => f?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true))
                            matches.Add(file);
                    }
                }

                filteredFiles.ReplaceAll(matches);
                OnPropertyChanged(nameof(NrFilteredFiles));
            }
        }
    }
