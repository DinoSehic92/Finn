namespace Finn.Model
{
    /// <summary>
    /// Represents a selectable path for the A or B side of a diff comparison.
    /// </summary>
    public sealed class DiffPathChoice(string label, string path) : System.IEquatable<DiffPathChoice>
    {
        /// <summary>Display label (e.g. "Original", "REV A", "REV B").</summary>
        public string Label { get; } = label;

        /// <summary>Full file path to the PDF.</summary>
        public string Path { get; } = path;

        public override string ToString() => Label;

        public bool Equals(DiffPathChoice? other) =>
            other is not null && string.Equals(Path, other.Path, System.StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object? obj) => Equals(obj as DiffPathChoice);

        public override int GetHashCode() =>
            System.StringComparer.OrdinalIgnoreCase.GetHashCode(Path ?? string.Empty);
    }
}
