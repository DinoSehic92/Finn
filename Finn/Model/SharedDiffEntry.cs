using System.ComponentModel;

namespace Finn.Model;

/// <summary>
/// One row in the pull-diff dialog showing what changed between the local
/// and server copies of a shared project.
/// </summary>
public class SharedDiffEntry : INotifyPropertyChanged
{
    private bool _keepLocal;

    public string Change { get; init; } = "";
    public string Category { get; init; } = "";
    public string Detail { get; init; } = "";

    /// <summary>
    /// The file name this entry relates to (null for project-level entries like Folders/To-Do).
    /// Used to group keep-local decisions by file.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// When checked, the local value is preserved instead of being overwritten
    /// by the server value. Applies to both Merge and Replace modes.
    /// Only meaningful for "Modified" rows on files that exist on both sides.
    /// </summary>
    public bool KeepLocal
    {
        get => _keepLocal;
        set { _keepLocal = value; PropertyChanged?.Invoke(this, new(nameof(KeepLocal))); }
    }

    /// <summary>Whether the KeepLocal checkbox should be shown for this row.</summary>
    public bool CanKeepLocal => !string.IsNullOrEmpty(FileName) && Change == "Modified";

    public event PropertyChangedEventHandler? PropertyChanged;
}
