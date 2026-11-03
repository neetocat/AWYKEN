namespace AWYKEN.Shared.Data;

/// <summary>A teacher-awarded incentive attached to one imported class.</summary>
public sealed class StudentIncentiveRecord
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int ScheduleRecordId { get; set; }
    public required string CourseCode { get; set; }
    public required string CourseTitle { get; set; }
    public int Points { get; set; }
    public required string Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
