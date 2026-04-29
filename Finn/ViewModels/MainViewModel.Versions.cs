using CommunityToolkit.Mvvm.Input;
using Finn.Model;
using System;
using System.IO;
using System.Linq;
using System.Windows.Input;

namespace Finn.ViewModels
{
    /// <summary>
    /// Version management commands and operations.
    /// </summary>
    public partial class MainViewModel
    {
        #region Version Commands

        private ICommand? _setActiveVersionCommand;
        public ICommand SetActiveVersionCommand => _setActiveVersionCommand ??= new RelayCommand(SetActiveVersion);

        private ICommand? _resetToOriginalCommand;
        public ICommand ResetToOriginalCommand => _resetToOriginalCommand ??= new RelayCommand(ResetToOriginal);

        private ICommand? _openSelectedVersionCommand;
        public ICommand OpenSelectedVersionCommand => _openSelectedVersionCommand ??= new RelayCommand(OpenSelectedVersion);

        private ICommand? _openSelectedVersionFolderCommand;
        public ICommand OpenSelectedVersionFolderCommand => _openSelectedVersionFolderCommand ??= new RelayCommand(OpenSelectedVersionFolder);

        private ICommand? _setFirstVersionCommand;
        public ICommand SetFirstVersionCommand => _setFirstVersionCommand ??= new RelayCommand(SetFirstVersionOnSelected);

        private ICommand? _setLastVersionCommand;
        public ICommand SetLastVersionCommand => _setLastVersionCommand ??= new RelayCommand(SetLastVersionOnSelected);

        #endregion

        public void SetActiveVersion()
        {
            if (CurrentFile == null || SelectedVersion == null) return;
            CurrentFile.CurrentVersion = SelectedVersion.Label;
            MarkDirty();
        }

        public void ResetToOriginal()
        {
            if (CurrentFiles == null) return;
            foreach (var file in CurrentFiles)
                if (file.HasVersions)
                    file.CurrentVersion = string.Empty;
            MarkDirty();
        }

        public void RemoveSelectedVersion()
        {
            if (CurrentFile == null || SelectedVersion == null) return;
            CurrentFile.RemoveVersion(SelectedVersion);
            MarkDirty();
        }

        public void OpenSelectedVersion()
        {
            if (SelectedVersion == null) return;
            OpenFileDirect(SelectedVersion.Sökväg);
        }

        public void OpenSelectedVersionFolder()
        {
            if (SelectedVersion == null) return;
            OpenPathDirect(SelectedVersion.Sökväg);
        }

        public void SetCurrentVersionOnSelected(string label)
        {
            if (CurrentFiles == null) return;
            foreach (var file in CurrentFiles)
                if (file.Versions.Any(v => v.Label == label))
                    file.CurrentVersion = label;
            MarkDirty();
        }

        public void SetFirstVersionOnSelected()
        {
            if (CurrentFiles == null) return;
            foreach (var file in CurrentFiles)
                if (file.Versions.Count > 0)
                    file.CurrentVersion = file.Versions[0].Label;
            MarkDirty();
        }

        public void SetLastVersionOnSelected()
        {
            if (CurrentFiles == null) return;
            foreach (var file in CurrentFiles)
                if (file.Versions.Count > 0)
                    file.CurrentVersion = file.Versions[^1].Label;
            MarkDirty();
        }

        public void LabelFirstVersionOnSelected(string label)
        {
            if (CurrentFiles == null || string.IsNullOrEmpty(label)) return;
            foreach (var file in CurrentFiles)
                if (file.Versions.Count > 0)
                    file.SetVersionLabel(file.Versions[0], label);
            MarkDirty();
        }

        public void LabelLastVersionOnSelected(string label)
        {
            if (CurrentFiles == null || string.IsNullOrEmpty(label)) return;
            foreach (var file in CurrentFiles)
                if (file.Versions.Count > 0)
                    file.SetVersionLabel(file.Versions[^1], label);
            MarkDirty();
        }

        /// <summary>
        /// Attempts to extract a date from the folder hierarchy of the selected
        /// version and applies it as the label. Walks up the directory tree
        /// checking each folder name for an embedded date, stopping at the
        /// configured version folder root so it never picks up dates from
        /// unrelated parent directories.
        /// </summary>
        public void LabelVersionFromFolderDate()
        {
            if (CurrentFile == null || SelectedVersion == null) return;
            if (string.IsNullOrEmpty(SelectedVersion.Sökväg)) return;

            string? dir = Path.GetDirectoryName(SelectedVersion.Sökväg);
            if (string.IsNullOrEmpty(dir)) return;

            // Find the version folder root that contains this file
            string? root = CurrentProject?.Folders
                .Where(f => f.Types == VERSIONS_TYPE && !string.IsNullOrEmpty(f.Path))
                .FirstOrDefault(f => SelectedVersion.Sökväg.StartsWith(f.Path, StringComparison.OrdinalIgnoreCase))
                ?.Path;

            string? date = TryExtractDateFromPath(dir, root);
            if (date != null)
            {
                CurrentFile.SetVersionLabel(SelectedVersion, date);
                MarkDirty();
            }
        }

        public void AddManualVersion(string filePath, string label)
        {
            if (CurrentFile == null) return;
            CurrentFile.AddVersion(filePath, label);
            MarkDirty();
        }
    }
}
