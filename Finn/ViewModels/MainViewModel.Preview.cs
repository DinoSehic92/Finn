using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Finn.Model;

namespace Finn.ViewModels
{
    // Partial class extension for preview and file-drop orchestration
    public partial class MainViewModel
    {
        /// <summary>
        /// Handles preview request orchestration when a file is selected.
        /// Replaces the logic previously in MainView.SetPreviewRequest.
        /// </summary>
        public async Task RequestPreviewAsync(FileData? file, string? searchText = null)
        {
            if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                return;

            CheckSingleFile();

            if (file != null && file.HasPdfExtension())
            {
                PreviewVM.RequestFile = file;

                await PreviewVM.SetFileAsync(searchText);
            }
            else
            {
                await PreviewVM.CloseRendererAsync();
            }
        }

        public async Task RequestPreviewLeftAsync(FileData? file)
        {
            if (file == null || !file.HasPdfExtension())
                return;

            if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                UI.PreviewEmbeddedOpen = true;

            CheckSingleFile();
            PreviewVM.RequestFile = file;
            // Preserve the dual-file layout — only replace the left document.
            await PreviewVM.SetFileAsync(preserveDualFile: true);
        }

        public async Task RequestPreview2Async(FileData? file)
        {
            if (file == null || !file.HasPdfExtension())
                return;

            if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                UI.PreviewEmbeddedOpen = true;

            await PreviewVM.SetFile2Async(file);
        }

        /// <summary>
        /// Previews a specific version of a file without changing the stored path.
        /// </summary>
        public async Task PreviewVersionAsync(FileVersionData version, string? searchText = null)
        {
            if (version == null) return;
            SelectedVersion = version;
            // Remember the real file so CanCompareVersions works during version preview
            PreviewVM.VersionSourceFile = CurrentFile;
            var stub = new FileData { Sökväg = version.Sökväg, Namn = version.ShortName };
            // Point the stub at the version's own annotation layers so strokes
            // survive version switches instead of being lost with the stub.
            stub.AnnotationLayers = version.AnnotationLayers;
            // Inherit cache flag from the parent file so versions use the local cache.
            if (CurrentFile?.IsCached == true)
                stub.IsCached = true;
            await RequestPreviewAsync(stub, searchText);
        }

        /// <summary>
        /// Returns to the original file after previewing a version.
        /// </summary>
        public async Task ReturnToOriginalAsync()
        {
            if (!IsViewingVersion) return;
            ClearSelectedVersion();
            await RequestPreviewAsync(CurrentFile);
        }

        /// <summary>
        /// Async version that shows an import dialog when files are dropped,
        /// letting the user confirm and pick a category.
        /// </summary>
        public async Task AddDroppedFilesAsync(IEnumerable<string> paths, Window mainWindow)
        {
            if (IsSearchResult) return;

            await AddFilesWithVersionCheck(paths, mainWindow, "Dropped");
            UpdateTreeview();
        }

        /// <summary>
        /// Processes dropped paths for the other-files grid.
        /// Ignored when viewing search results to avoid modifying a transient project.
        /// </summary>
        public async Task AddDroppedOtherFilesAsync(IEnumerable<string> filePaths, IEnumerable<string> folderPaths)
        {
            if (OtherFilesOwner == null || IsSearchResult || IsSyncing) return;
            IsSyncing = true;
            try
            {
                foreach (string path in filePaths)
                    AddOtherFile(path);

                foreach (string path in folderPaths)
                {
                    if (IsDuplicateFolderPath(path))
                        continue;

                    var folder = CreateAttachedFolder(path, "Other Files");
                    CurrentProject.Folders.Add(folder);
                    await SyncFolderAsync(folder);
                }

                RefreshFolderWatchers();
            }
            finally
            {
                IsSyncing = false;
            }
        }

        /// <summary>
        /// Processes dropped folder paths for the folder grid.
        /// Ignored when viewing search results to avoid adding folders to a transient project.
        /// </summary>
        public async Task AddDroppedFoldersAsync(IEnumerable<string> paths, Window? mainWindow = null)
        {
            if (IsSearchResult || IsSyncing) return;
            IsSyncing = true;
            try
            {
                foreach (string path in paths)
                {
                    if (IsDuplicateFolderPath(path))
                        continue;

                    var folder = new FolderData
                    {
                        Name = new System.IO.DirectoryInfo(path).Name,
                        AttachToFile = "PROJECT",
                        Types = "PDF",
                        Path = path
                    };
                    CurrentProject.Folders.Add(folder);
                    await SyncFolderAsync(folder, mainWindow);
                }

                RefreshFolderWatchers();
            }
            finally
            {
                IsSyncing = false;
            }
        }

        /// <summary>
        /// Processes dropped folder paths for the folder grid as version-delivery folders.
        /// Guards against concurrent syncs.
        /// </summary>
        public async Task AddDroppedVersionFoldersAsync(IEnumerable<string> paths, Window? mainWindow = null)
        {
            if (IsSearchResult || IsSyncing) return;
            IsSyncing = true;
            try
            {
                foreach (string path in paths)
                {
                    NewVersionFolder(path);
                    var folder = CurrentProject.Folders.LastOrDefault();
                    if (folder != null)
                        await SyncFolderAsync(folder, mainWindow);
                }
            }
            finally
            {
                IsSyncing = false;
            }
        }

        /// <summary>
        /// Creates and syncs an attached-files folder for the current file.
        /// Skips duplicate paths. Guards against concurrent syncs.
        /// Called from the Attach Files dialog.
        /// </summary>
        public async Task AddAttachedFolderAsync(string folderPath)
        {
            if (CurrentFile == null || string.IsNullOrEmpty(folderPath)) return;
            if (IsDuplicateFolderPath(folderPath)) return;

            IsSyncing = true;
            try
            {
                NewFileFolder();
                var folder = CurrentProject.Folders.LastOrDefault();
                if (folder != null)
                {
                    folder.Path = folderPath;
                    folder.Name = new System.IO.DirectoryInfo(folderPath).Name;
                    folder.Types = "PDF";
                    await SyncFolderAsync(folder);
                }
                RefreshFolderWatchers();
            }
            finally
            {
                IsSyncing = false;
            }
        }

        private FolderData CreateAttachedFolder(string path, string types) => new()
        {
            Name = new System.IO.DirectoryInfo(path).Name,
            AttachToFile = CurrentFile!.Namn,
            AttachToFilePath = CurrentFile.Sökväg,
            Types = types,
            Path = path
        };

        /// <summary>
        /// Selects files from a secondary list (collection/recent) and navigates to the project.
        /// Uses lightweight tree navigation instead of a full rebuild.
        /// For appended files, falls back to the parent file's project context.
        /// </summary>
        public void SelectAndNavigateFiles(IList<FileData> files)
        {
            var file = files.FirstOrDefault();
            var nav = file;

            // Appended files may have empty Uppdrag in legacy data — use parent
            if (nav != null && string.IsNullOrEmpty(nav.Uppdrag) && nav.ParentFile != null)
                nav = nav.ParentFile;

            if (nav is { Uppdrag: not "", Filtyp: not "" })
            {
                NavigateTo(nav.Uppdrag, nav.Filtyp);
            }

            SelectFiles(files);
            NavigateTreeToCurrentProject();
        }
    }
}