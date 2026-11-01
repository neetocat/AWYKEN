namespace AWYKEN.Shared.Models;

public enum UmakEventType
{
    SemesterStart,
    SemesterEnd,
    Holiday,
    Exam,
    Event
}

/// <summary>
/// One entry from the UMak academic calendar. All times are Asia/Manila local time.
/// For all-day events Start and End are both midnight and End is the INCLUSIVE last day
/// (iCal's exclusive end date has already been adjusted).
/// </summary>
public sealed class UmakEvent
{
    public string Title { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }
    public UmakEventType Type { get; set; } = UmakEventType.Event;
    public string Description { get; set; } = "";
}
