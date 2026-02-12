using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            if (!PreviewEmbeddedOpen && !PreviewWindowOpen)
                return;

            CheckSingleFile();

            if (file != null && file.IsValidPdf())
            {
                PreviewVM.SetupPage(file.DefaultPage);
                PreviewVM.RequestFile = file;
                PreviewVM.ToggleVisibility(false);

                await PreviewVM.SetFileAsync(searchText);
            }
            else
            {
                await PreviewVM.CloseRendererAsync();
            }
        }

        /// <summary>
        /// Processes dropped file paths, adding PDFs to the current project.
        /// </summary>
        public void AddDroppedFiles(IEnumerable<string> paths)
        {
            foreach (string path in paths)
                AddFilesDrag(path);

            UpdateTreeview();
        }

        /// <summary>
        /// Processes dropped paths for the appendix grid (attached PDFs and folders).
        /// </summary>
        public void AddDroppedAppendedFiles(IEnumerable<string> filePaths, IEnumerable<string> folderPaths)
        {
            if (CurrentFile == null) return;

            foreach (string path in filePaths)
                AddAppendedFile(path);

            foreach (string path in folderPaths)
            {
                var folder = CreateAttachedFolder(path, "PDF");
                CurrentProject.Folders.Add(folder);
                SyncFolder(folder);
            }
        }

        /// <summary>
        /// Processes dropped paths for the other-files grid.
        /// </summary>
        public void AddDroppedOtherFiles(IEnumerable<string> filePaths, IEnumerable<string> folderPaths)
        {
            if (CurrentFile == null) return;

            foreach (string path in filePaths)
                AddOtherFile(path);

            foreach (string path in folderPaths)
            {
                var folder = CreateAttachedFolder(path, "Other Files");
                CurrentProject.Folders.Add(folder);
                SyncFolder(folder);
            }
        }

        /// <summary>
        /// Processes dropped folder paths for the folder grid.
        /// </summary>
        public void AddDroppedFolders(IEnumerable<string> paths)
        {
            foreach (string path in paths)
            {
                CurrentProject.Folders.Add(new FolderData
                {
                    Name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? string.Empty,
                    Types = "PDF",
                    Path = path
                });
            }
        }

        private FolderData CreateAttachedFolder(string path, string types) => new()
        {
            Name = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) ?? string.Empty,
            AttachToFile = CurrentFile!.Namn,
            AttachToFilePath = CurrentFile.Sökväg,
            Types = types,
            Path = path
        };

        /// <summary>
        /// Selects files from a secondary list (collection/recent) and navigates to the project.
        /// </summary>
        public void SelectAndNavigateFiles(IList<FileData> files)
        {
            var file = files.FirstOrDefault();
            if (file is { Uppdrag: not "", Filtyp: not "" })
            {
                SelectProject(file.Uppdrag);
                SelectType(file.Filtyp);
            }

            select_files(files);
            UpdateTreeview();
        }
    }
}