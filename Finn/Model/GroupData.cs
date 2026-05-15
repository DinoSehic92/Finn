using System.Text.Json.Serialization;

namespace Finn.Model
{
    /// <summary>
    /// Represents a named project group (or subgroup) in the tree view.
    /// Projects reference a group by <see cref="Name"/> via <see cref="ProjectData.Parent"/>.
    /// </summary>
    public class GroupData
    {
        /// <summary>Display name. Also the key referenced by <see cref="ProjectData.Parent"/>.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Name of the parent group, or null/empty for a top-level group.
        /// Only one level of nesting is supported (group → subgroup → projects).
        /// </summary>
        public string? ParentGroup { get; set; }

        /// <summary>Category this group belongs to ("Project", "Archive", "Library").</summary>
        public string Category { get; set; } = "Project";

        /// <summary>Controls display order within the same level.</summary>
        public int SortOrder { get; set; }

        /// <summary>Color tag name (e.g. "Blue", "Green") matching the file/project tag palette. Empty = no color.</summary>
        public string ColorTag { get; set; } = string.Empty;
    }
}
