namespace AWYKEN.Shared.Models;

public sealed class ClassDraft
{
    public string CourseCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string StartTime { get; set; } = "08:00";
    public string EndTime { get; set; } = "09:00";
    public string Location { get; set; } = "";
    public string Instructor { get; set; } = "";
    public string Mode { get; set; } = "Face-to-face";
    // These fields are edited from the class form's Notes tab and saved with the class.
    public string Notes { get; set; } = "";
    public string? AttendanceStatus { get; set; }
    public string CreatedAtText { get; set; } = "";
    public byte[]? ImageData { get; set; }
    public string? ImageContentType { get; set; }

    public bool TryValidate(out TimeSpan start, out TimeSpan end, out string? error)
    {
        start = default;
        end = default;

        if (string.IsNullOrWhiteSpace(CourseCode) ||
            string.IsNullOrWhiteSpace(Title) ||
            string.IsNullOrWhiteSpace(StartTime) ||
            string.IsNullOrWhiteSpace(EndTime) ||
            string.IsNullOrWhiteSpace(Location) ||
            string.IsNullOrWhiteSpace(Instructor))
        {
            error = "Please fill in all fields.";
            return false;
        }

        if (!TimeSpan.TryParse(StartTime, out start) ||
            !TimeSpan.TryParse(EndTime, out end))
        {
            error = "Please enter valid start and end times.";
            return false;
        }

        if (end <= start)
        {
            error = "End time must be later than start time.";
            return false;
        }

        error = null;
        return true;
    }
}
