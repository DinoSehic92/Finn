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

            if (file != null && file.IsValidPdf())
            {
                PreviewVM.RequestFile = file;
                PreviewVM.ToggleVisibility(false);

                await PreviewVM.SetFileAsync(searchText);
            }
            else
            {
                await PreviewVM.CloseRendererAsync();
            }
        }

        public async Task RequestPreviewLeftAsync(FileData? file)
        {
            if (file == null || !file.IsValidPdf())
                return;

            if (!UI.PreviewEmbeddedOpen && !PreviewWindowOpen)
                UI.PreviewEmbeddedOpen = true;

            CheckSingleFile();
            PreviewVM.RequestFile = file;
            PreviewVM.ToggleVisibility(false);
            await PreviewVM.SetFileAsync();
        }

        public async Task RequestPreview2Async(FileData? file)
        {
            if (file == null || !file.IsValidPdf())
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
            var stub = new FileData { Sökväg = version.Sökväg, Namn = version.ShortName };
            await RequestPreviewAsync(stub, searchText);
        }

        /// <summary>
        /// Processes dropped file paths, adding PDFs to the current project.
        /// Ignored when viewing search results to avoid adding files to a transient project.
        /// </summary>
        public void AddDroppedFiles(IEnumerable<string> paths)
        {
            if (IsSearchResult) return;

            foreach (string path in paths)
                AddFilesDrag(path);

            UpdateTreeview();
        }

        /// <summary>
        /// Async version of <see cref="AddDroppedFiles"/> that shows a version-import
        /// dialog when any of the dropped files match existing entries by name.
        /// </summary>
        public async Task AddDroppedFilesAsync(IEnumerable<string> paths, Window mainWindow)
        {
            if (IsSearchResult) return;

            await AddFilesWithVersionCheck(paths, mainWindow);
            UpdateTreeview();
        }

        /// <summary>
        /// Processes dropped paths for the other-files grid.
        /// Ignored when viewing search results to avoid modifying a transient project.
        /// </summary>
        public async Task AddDroppedOtherFilesAsync(IEnumerable<string> filePaths, IEnumerable<string> folderPaths)
        {
            if (OtherFilesOwner == null || IsSearchResult) return;

            foreach (string path in filePaths)
                AddOtherFile(path);

            foreach (string path in folderPaths)
            {
                var folder = CreateAttachedFolder(path, "Other Files");
                CurrentProject.Folders.Add(folder);
                await SyncFolderAsync(folder);
            }
        }

        /// <summary>
        /// Processes dropped folder paths for the folder grid.
        /// Ignored when viewing search results to avoid adding folders to a transient project.
        /// </summary>
        public async Task AddDroppedFoldersAsync(IEnumerable<string> paths, Window? mainWindow = null)
        {
            if (IsSearchResult) return;

            foreach (string path in paths)
            {
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