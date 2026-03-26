namespace Finn.Model
{
    /// <summary>
    /// A single row in the weekly diary detail grid.
    /// Represents one day's combined diary text for a project.
    /// </summary>
    public class WeekDiaryEntry
    {
        public string Day { get; set; } = string.Empty;
        public string Diary { get; set; } = string.Empty;
    }
}
