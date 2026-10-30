namespace AWYKEN.Shared.Data;

/// <summary>A note attached to one course occurrence on a specific calendar date.</summary>
public sealed class CourseNoteRecord
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int ScheduleRecordId { get; set; }
    public DateTime ClassDate { get; set; }
    // Kept as editable text because the user asked to be able to adjust the displayed timestamp.
    public required string CreatedAtText { get; set; }
    public string Text { get; set; } = "";
    public string? AttendanceStatus { get; set; }
    public byte[]? ImageData { get; set; }
    public string? ImageContentType { get; set; }
    public string CourseCode { get; set; } = "";
    public string CourseTitle { get; set; } = "";
}
