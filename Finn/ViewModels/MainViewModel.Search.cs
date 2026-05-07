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

                foreach (ContentData content in Data.TextContent ?? [])
                {
                    if (content.PlainText?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        filepaths.Add(content.Filepath);
                    }
                }

                var directMatches = new HashSet<FileData>(ReferenceEqualityComparer.Instance);
                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        var paths = file.AllPdfPaths(checkExists: false);
                        if (paths.Any(p => filepaths.Contains(p)))
                            directMatches.Add(file);
                    }
                }

                var results = ExpandWithHierarchy(directMatches, Storage.StoredProjects);
                // Content search matches document text — don't highlight file names.
                StampSearchMatchFlags(results, directMatches, searchTerm: string.Empty);
                filteredFiles.ReplaceAll(results);
                OnPropertyChanged(nameof(NrFilteredFiles));
            }

            public void SearchFiles()
            {
                // Use backing field to avoid triggering UpdateFilter from the
                // CurrentProject property setter — we populate FilteredFiles manually.
                currentProject = new ProjectData() { Namn = SearchText, Category = "Search" };
                OnPropertyChanged(nameof(CurrentProject));
                OnPropertyChanged(nameof(IsSearchResult));

                var directMatches = new HashSet<FileData>(ReferenceEqualityComparer.Instance);
                foreach (ProjectData project in Storage.StoredProjects)
                {
                    foreach (FileData file in project.StoredFiles)
                    {
                        string?[] fields = [file.Namn, file.Beskrivning1, file.Beskrivning2, file.Beskrivning3, file.Tagg];

                        if (fields.Any(f => f?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) == true))
                            directMatches.Add(file);
                    }
                }

                var results = ExpandWithHierarchy(directMatches, Storage.StoredProjects);
                StampSearchMatchFlags(results, directMatches, SearchText);
                filteredFiles.ReplaceAll(results);
                OnPropertyChanged(nameof(NrFilteredFiles));
            }

            /// <summary>
            /// Stamps <see cref="FileData.IsSearchMatch"/> and <see cref="FileData.SearchTerm"/>
            /// on every file in the result set. Files in <paramref name="directMatches"/> are
            /// marked as true matches; context files (pulled in via hierarchy) are marked false
            /// but still receive the search term so the converter can decide rendering.
            /// </summary>
            private static void StampSearchMatchFlags(
                IEnumerable<FileData> results,
                HashSet<FileData> directMatches,
                string searchTerm)
            {
                foreach (var file in results)
                {
                    file.IsSearchMatch = directMatches.Contains(file);
                    file.SearchTerm = searchTerm;
                }
            }

            /// <summary>
            /// Clears search match flags on all files across all projects.
            /// Called when the user navigates away from a search result view.
            /// </summary>
            public void ClearSearchMatchFlags()
            {
                foreach (var project in Storage.StoredProjects)
                {
                    foreach (var file in project.StoredFiles)
                    {
                        if (file.IsSearchMatch || !string.IsNullOrEmpty(file.SearchTerm))
                        {
                            file.IsSearchMatch = false;
                            file.SearchTerm = string.Empty;
                        }
                    }
                }
            }

            /// <summary>
            /// Expands a set of direct search matches to include full parent/child
            /// context using Option B rules:
            /// <list type="bullet">
            ///   <item>A matched parent pulls in all its children.</item>
            ///   <item>A matched child pulls in its parent (so it renders in context).</item>
            ///   <item>Duplicates are removed; original StoredFiles order is preserved.</item>
            /// </list>
            /// The result is always ordered parent-before-children so the DataGrid
            /// hierarchy rendering stays correct.
            /// </summary>
            private static List<FileData> ExpandWithHierarchy(
                HashSet<FileData> directMatches,
                IEnumerable<ProjectData> allProjects)
            {
                // Build a flat lookup of all files keyed by reference for fast parent look-up
                var allFiles = allProjects.SelectMany(p => p.StoredFiles).ToList();

                var included = new HashSet<FileData>(ReferenceEqualityComparer.Instance);

                foreach (var file in directMatches)
                {
                    if (file.IsChild && file.ParentFile != null)
                    {
                        // Matched child ? include its parent so it renders in proper context
                        included.Add(file.ParentFile);
                    }
                    included.Add(file);
                }

                // For every included parent, pull in ALL its children (not just matched ones)
                // so the group renders completely in the result set.
                var parents = included.Where(f => f.IsTopLevel && f.HasChildren).ToList();
                foreach (var parent in parents)
                {
                    foreach (var file in allFiles)
                    {
                        if (file.IsChild
                            && file.ParentFile != null
                            && ReferenceEquals(file.ParentFile, parent))
                        {
                            included.Add(file);
                        }
                    }
                }

                            // Rebuild in display order: per-project, top-level first then children immediately
                            // after their parent — mirroring what UpdateFilter does so hierarchy renders correctly.
                            var result = new List<FileData>();
                            foreach (var project in allProjects)
                            {
                                foreach (var file in project.StoredFiles)
                                {
                                    if (!file.IsTopLevel || !included.Contains(file))
                                        continue;

                                    result.Add(file);

                                    // Insert children that belong to this parent right after it
                                    foreach (var child in project.StoredFiles)
                                    {
                                        if (child.IsChild
                                            && child.ParentFile != null
                                            && ReferenceEquals(child.ParentFile, file)
                                            && included.Contains(child))
                                        {
                                            result.Add(child);
                                        }
                                    }
                                }
                            }

                            return result;
                        }
                    }
                }
