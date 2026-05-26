using System.Collections.Generic;

namespace Finn.Storage
{
    /// <summary>
    /// Persisted UI state for a single project: last selected type filter,
    /// last selected file name, and expansion state for group headers.
    /// </summary>
    public class ProjectUIState
    {
        /// <summary>Last active type filter ("All Types", "PDF", …).</summary>
        public string ActiveType { get; set; } = "All Types";

        /// <summary>Display name of the last selected file, or null.</summary>
        public string? SelectedFileName { get; set; }

        /// <summary>Maps group/parent file display name → IsExpanded.</summary>
        public Dictionary<string, bool> GroupExpansion { get; set; } = new();
    }

    /// <summary>
    /// Root DTO for UIState.json — persists transient layout state that
    /// should survive app restarts but must not affect the project dirty flag.
    /// </summary>
    public class UIStateStorage
    {
        /// <summary>Name of the project that was active when the app last closed.</summary>
        public string? LastActiveProject { get; set; }

        // ── Tray / panel visibility (moved here from UIStorage) ──────────────
        public bool TrayNote         { get; set; } = true;
        public bool TrayCollections  { get; set; } = true;
        public bool TrayBookmarks    { get; set; }
        public bool TrayRecent       { get; set; } = true;
        public bool TrayVersions     { get; set; }
        public bool TrayOtherFiles   { get; set; }
        public bool TrayTodo         { get; set; } = true;

        public bool TreeViewOpen     { get; set; } = true;
        public bool CalendarOpen     { get; set; }
        public bool TimeSheetOpen    { get; set; }
        public bool ShowFolders      { get; set; }
        public bool ShowThumbnails   { get; set; }
        public bool TrayViewOpen     { get; set; }
        public bool ShowActionBar    { get; set; } = true;

        // ── TreeView sidebar expansion ───────────────────────────────────────
        /// <summary>
        /// Collapsed/expanded state of category, group, and subgroup nodes in
        /// the sidebar TreeView. Keys use a prefix to avoid clashes:
        ///   "cat:Archive", "grp:MyGroup", "sub:MySubgroup"
        /// Only nodes that differ from the default (expanded) need an entry.
        /// </summary>
        public Dictionary<string, bool> TreeNodeExpansion { get; set; } = new();

        // ── Per-project state ────────────────────────────────────────────────
        /// <summary>
        /// Keyed by project name; values carry the last type filter,
        /// selected file, and group expansion map for each project.
        /// </summary>
        public Dictionary<string, ProjectUIState> Projects { get; set; } = new();
    }
}
