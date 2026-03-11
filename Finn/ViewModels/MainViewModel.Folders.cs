using Avalonia.Controls;
using Finn.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Finn.ViewModels
    {
        public partial class MainViewModel
        {
            public void NewFolder()
            {
                CurrentProject.Folders.Add(new FolderData() { Name = "New Folder" });
            }

            public void NewFileFolder()
            {
                if (CurrentFile != null)
                {
                    CurrentProject.Folders.Add(new FolderData() { Name = "New file folder", AttachToFile = CurrentFile.Namn, AttachToFilePath = CurrentFile.Sökväg });
                }
            }

            public void RemoveFolders(List<FolderData> folders)
            {
                foreach (var folder in folders)
                {
                    if (folder.IsProjectLevel)
                    {
                        CurrentProject.StoredFiles.RemoveAll(x => x.IsFromFolder && x.SyncFolder == folder.Path);
                        UpdateFilter();
                        CurrentProject.Folders.Remove(folder);
                        OnPropertyChanged("TreeViewUpdate");
                    }
                    else
                    {
                        FileData file = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == folder.AttachToFile);

                        if (file != null)
                        {
                            if (folder.Types == PDF_TYPE)
                            {
                                string folderPath = folder.Path;
                                var removed = file.AppendedFiles.Where(x => x.SyncFolder == folderPath).ToList();
                                foreach (var r in removed)
                                {
                                    r.PartOfCollections.Clear();
                                    r.ParentFile = null;
                                }
                                file.AppendedFiles.ReplaceAll(
                                    file.AppendedFiles.Where(x => x.SyncFolder != folderPath).OrderBy(x => x.Namn));
                            }

                            if (folder.Types == OTHER_FILES_TYPE)
                            {
                                string folderPath = folder.Path;
                                file.OtherFiles.ReplaceAll(
                                    file.OtherFiles.Where(x => x.SyncFolder != folderPath).OrderBy(x => x.Name));
                            }
                        }

                        Collections.SetCollectionContent();
                        CurrentProject.Folders.Remove(folder);
                    }
                }
            }

            public async Task SyncFoldersAsync(List<FolderData> folders, Window? mainWindow = null)
            {
                foreach (var folder in folders)
                {
                    await SyncFolderAsync(folder, mainWindow);
                }
            }

            public async Task SyncFileAsync()
            {
                if (CurrentFile != null)
                {
                    foreach (FolderData folder in CurrentProject.Folders.Where(x => x.AttachToFilePath == CurrentFile.Sökväg))
                    {
                        await SyncFolderAsync(folder);
                    }
                }
            }

            public async Task SyncFolderAsync(FolderData folder, Window? mainWindow = null)
            {
                if (folder?.IsValid() != true || folder.Path == null)
                {
                    return;
                }

                if (!folder.IsProjectLevel)
                {
                    FileData file = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == folder.AttachToFile);

                    if (file != null)
                    {
                        int count = 0;

                        if (folder.Types == PDF_TYPE)
                        {
                            var remaining = file.AppendedFiles.Where(x => x.SyncFolder != folder.Path);
                            var newFiles = GetFilesFromFolder(folder);
                            foreach (var f in newFiles)
                            {
                                f.ParentFile = file;
                                f.Uppdrag = file.Uppdrag;
                                f.Filtyp = file.Filtyp;
                            }
                            count = newFiles.Count;
                            file.AppendedFiles.ReplaceAll(remaining.Concat(newFiles).OrderBy(x => x.Namn));
                        }

                        if (folder.Types == OTHER_FILES_TYPE)
                        {
                            var remaining = file.OtherFiles.Where(x => x.SyncFolder != folder.Path);
                            var newFiles = GetOtherFilesFromFolder(folder);
                            count = newFiles.Count;
                            file.OtherFiles.ReplaceAll(remaining.Concat(newFiles).OrderBy(x => x.Name));
                        }

                        folder.SyncedFileCount = count;
                    }
                }
                else
                {
                    List<FileData> files = GetFilesFromFolder(folder);

                    // Use HashSets for O(1) path lookups instead of nested Any() which is O(n×m)
                    var newPaths = new HashSet<string>(files.Select(f => f.Sökväg), StringComparer.OrdinalIgnoreCase);
                    List<FileData> existingFiles = CurrentProject.StoredFiles.Where(x => x.IsFromFolder).Where(x => x.SyncFolder == folder.Path).ToList();
                    var existingPaths = new HashSet<string>(existingFiles.Select(f => f.Sökväg), StringComparer.OrdinalIgnoreCase);

                    List<FileData> filesToRemove = existingFiles.Where(p => !newPaths.Contains(p.Sökväg)).ToList();
                    List<FileData> filesToAdd = files.Where(p => !existingPaths.Contains(p.Sökväg)).ToList();

                    var removeSet = new HashSet<FileData>(filesToRemove);
                    if (removeSet.Count > 0)
                        CurrentProject.StoredFiles.RemoveAll(f => removeSet.Contains(f));

                    var actualAdds = new List<FileData>();
                    var versionCandidates = new List<VersionImportEntry>();

                    foreach (FileData file in filesToAdd)
                    {
                        if (CurrentProject.StoredFiles.Any(x => x.Sökväg == file.Sökväg))
                            continue;

                        var existing = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == file.Namn);
                        if (existing != null)
                        {
                            versionCandidates.Add(new VersionImportEntry
                            {
                                ExistingFile = existing,
                                NewFilePath = file.Sökväg
                            });
                        }
                        else
                        {
                            actualAdds.Add(file);
                        }
                    }

                    if (actualAdds.Count > 0)
                        CurrentProject.StoredFiles.AddRange(actualAdds);

                    if (versionCandidates.Count > 0 && mainWindow != null)
                    {
                        bool confirmed = await ShowVersionImportDialogAsync(mainWindow, versionCandidates);
                        if (confirmed)
                        {
                            foreach (var entry in versionCandidates)
                                entry.ExistingFile.AddVersion(entry.NewFilePath, entry.SelectedLabel);
                        }
                    }

                    folder.SyncedFileCount = CurrentProject.StoredFiles.Count(x => x.IsFromFolder && x.SyncFolder == folder.Path);

                    SetDefaultType();
                    OnPropertyChanged("TreeViewUpdate");
                }
            }

            private List<FileData> GetFilesFromFolder(FolderData folder)
            {
                List<FileData> files = new();

                if (folder.IsValid())
                {
                    foreach (string path in Directory.GetFiles(folder.Path))
                    {
                        if (Path.GetExtension(path) == ".pdf")
                        {
                            files.Add(new FileData()
                            {
                                Namn = System.IO.Path.GetFileNameWithoutExtension(path),
                                Sökväg = path,
                                Uppdrag = CurrentProject.Namn,
                                Filtyp = NEW_TYPE,
                                SyncFolder = folder.Path,
                                IsFromFolder = true
                            });
                        }
                    }
                }

                return files;
            }

            private List<OtherData> GetOtherFilesFromFolder(FolderData folder)
            {
                List<OtherData> files = new();

                if (folder.IsValid())
                {
                    foreach (string path in Directory.GetFiles(folder.Path))
                    {
                        OtherData newFile = new()
                        {
                            Name = System.IO.Path.GetFileNameWithoutExtension(path),
                            Filepath = path,
                            SyncFolder = folder.Path,
                            IsFromFolder = true
                        };

                        newFile.SetFile();
                        files.Add(newFile);
                    }
                }

                return files;
            }
        }
    }
