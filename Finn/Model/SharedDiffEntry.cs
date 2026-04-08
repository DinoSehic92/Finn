using System.ComponentModel;

namespace Finn.Model;

/// <summary>
/// One row in the pull-diff dialog showing what changed between the local
/// and server copies of a shared project.
/// </summary>
public class SharedDiffEntry : INotifyPropertyChanged
{
    private bool _acceptIncoming;

    public string Change { get; init; } = "";
    public string Category { get; init; } = "";
    public string Detail { get; init; } = "";

    /// <summary>
    /// The file name this entry relates to (null for project-level entries like Folders/To-Do).
    /// Used to group accept-incoming decisions by file.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// When true during a Merge, the server value fully replaces the local value
    /// for this entry's file + category instead of the default additive merge.
    /// Only meaningful for "Modified" rows on files that exist on both sides.
    /// </summary>
    public bool AcceptIncoming
    {
        get => _acceptIncoming;
        set { _acceptIncoming = value; PropertyChanged?.Invoke(this, new(nameof(AcceptIncoming))); }
    }

    /// <summary>Whether the AcceptIncoming checkbox should be shown for this row.</summary>
    public bool CanAcceptIncoming => !string.IsNullOrEmpty(FileName) && Change == "Modified";

    public event PropertyChangedEventHandler? PropertyChanged;
}
