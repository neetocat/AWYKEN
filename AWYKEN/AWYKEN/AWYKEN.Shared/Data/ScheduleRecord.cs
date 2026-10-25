namespace AWYKEN.Shared.Data;

/// <summary>
/// A class entered manually or later accepted from an import review. No rows are
/// inserted when the database is first created, so the schedule starts blank.
/// </summary>
public sealed class ScheduleRecord
{
    public int Id { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public required string CourseCode { get; set; }
    public required string Title { get; set; }
    public required string Location { get; set; }
    public required string Instructor { get; set; }
    public required string Mode { get; set; }
    public required string ColorCode { get; set; }
    public string? MeetingLink { get; set; }
    public string? Cfn { get; set; }
    public string? Section { get; set; }
    public decimal? Units { get; set; }
    public string? Days { get; set; }
    public string? MeetingTimes { get; set; }
}
