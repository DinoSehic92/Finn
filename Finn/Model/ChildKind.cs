namespace Finn.Model
{
    /// <summary>
    /// Describes the relationship of a <see cref="FileData"/> entry to its parent.
    /// </summary>
    public enum ChildKind
    {
        /// <summary>Top-level file with no parent.</summary>
        None,

        /// <summary>Child nested inside a group header (<see cref="FileData.IsGroup"/> = true).</summary>
        GroupChild,

        /// <summary>Child directly attached to a regular file (not a group).</summary>
        AttachedChild,
    }
}
