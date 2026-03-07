using Finn.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            public void RemoveFolder()
            {
                if (CurrentFolder != null)
                {
                    if (CurrentFolder.IsProjectLevel)
                        {
                            foreach (FileData file in CurrentProject.StoredFiles.Where(x => x.IsFromFolder).Where(x => x.SyncFolder == CurrentFolder.Path).ToList())
                            {
                                CurrentProject.StoredFiles.Remove(file);
                            }

                            UpdateFilter();
                            CurrentProject.Folders.Remove(CurrentFolder);
                            OnPropertyChanged("TreeViewUpdate");
                        }
                        else
                    {
                        FileData file = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == CurrentFolder.AttachToFile);

                        if (file != null)
                        {
                            if (CurrentFolder.Types == PDF_TYPE)
                            {
                                foreach (FileData fileToRemove in file.AppendedFiles.Where(x => x.SyncFolder == CurrentFolder.Path).ToList())
                                {
                                    file.AppendedFiles.Remove(fileToRemove);
                                }
                                SortAttachedFilesDirect(file);
                            }

                            if (CurrentFolder.Types == OTHER_FILES_TYPE)
                            {
                                foreach (OtherData fileToRemove in file.OtherFiles.Where(x => x.SyncFolder == CurrentFolder.Path).ToList())
                                {
                                    file.OtherFiles.Remove(fileToRemove);
                                }
                                SortOtherFilesDirect(file);
                            }
                        }

                        CurrentProject.Folders.Remove(CurrentFolder);
                    }
                }
            }

            public void SyncAllFolders()
            {
                foreach (FolderData folder in CurrentProject.Folders)
                {
                    SyncFolder(folder);
                }
            }

            public void SyncSelectedFolder()
            {
                SyncFolder(CurrentFolder);
            }

            public void SyncFile()
            {
                if (CurrentFile != null)
                {
                    foreach (FolderData folder in CurrentProject.Folders.Where(x => x.AttachToFilePath == CurrentFile.Sökväg))
                    {
                        SyncFolder(folder);
                    }
                }
            }

            public void SyncFolder(FolderData folder)
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
                        if (folder.Types == PDF_TYPE)
                        {
                            foreach (FileData fileToRemove in file.AppendedFiles.Where(x => x.SyncFolder == folder.Path).ToList())
                            {
                                file.AppendedFiles.Remove(fileToRemove);
                            }

                            foreach (FileData fileToAdd in GetFilesFromFolder(folder))
                            {
                                file.AppendedFiles.Add(fileToAdd);
                            }

                            SortAttachedFilesDirect(file);
                        }

                        if (folder.Types == OTHER_FILES_TYPE)
                        {
                            foreach (OtherData fileToRemove in file.OtherFiles.Where(x => x.SyncFolder == folder.Path).ToList())
                            {
                                file.OtherFiles.Remove(fileToRemove);
                            }

                            foreach (OtherData fileToAdd in GetOtherFilesFromFolder(folder))
                            {
                                file.OtherFiles.Add(fileToAdd);
                            }

                            SortOtherFilesDirect(file);
                        }
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

                    foreach (FileData file in filesToRemove)
                    {
                        CurrentProject.StoredFiles.Remove(file);
                    }
                    foreach (FileData file in filesToAdd)
                    {
                        // Skip if the exact path is already registered anywhere in the project
                        if (CurrentProject.StoredFiles.Any(x => x.Sökväg == file.Sökväg))
                            continue;

                        // If a file with the same name already exists, register the folder
                        // file as a new version rather than adding a duplicate entry
                        var existing = CurrentProject.StoredFiles.FirstOrDefault(x => x.Namn == file.Namn);
                        if (existing != null)
                            existing.AddVersion(file.Sökväg, "NEW");
                        else
                            CurrentProject.StoredFiles.Add(file);
                    }

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
