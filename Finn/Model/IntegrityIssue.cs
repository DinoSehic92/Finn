namespace Finn.Model
{
    public enum IntegrityIssueSeverity
    {
        Warning,
        Error
    }

    public sealed record IntegrityIssue(
        IntegrityIssueSeverity Severity,
        string Code,
        string Message,
        string? FileName = null,
        string? FolderPath = null);
}
