namespace Finn.Model
{
    /// <summary>
    /// Represents a folder that has detected changes and may need syncing.
    /// </summary>
    public class SyncStatusEntry
    {
        /// <summary>Display name of the folder.</summary>
        public string FolderName { get; init; } = string.Empty;

        /// <summary>Name of the project the folder belongs to.</summary>
        public string ProjectName { get; init; } = string.Empty;

        /// <summary>Disk path of the folder, used to locate the <see cref="FolderData"/> at sync time.</summary>
        public string FolderPath { get; init; } = string.Empty;

        /// <summary>User-facing label for the folder's sync mode (e.g. "Sync", "Attached", "Other Files", "Versions").</summary>
        public string FolderType { get; init; } = string.Empty;

        /// <summary>
        /// Short description of the detected changes, e.g. "+3 new, -1 removed"
        /// or "Files modified". Shown in the flyout so the user knows what to expect.
        /// </summary>
        public string ChangesSummary { get; init; } = string.Empty;

        /// <summary>
        /// Creates a <see cref="SyncStatusEntry"/> from a folder and its owning project name.
        /// </summary>
        public static SyncStatusEntry FromFolder(FolderData folder, string projectName, string? changesSummary = null) => new()
        {
            FolderName = folder.Name,
            ProjectName = projectName,
            FolderPath = folder.Path,
            FolderType = folder.DisplayType,
            ChangesSummary = changesSummary ?? ""
        };
    }
}
