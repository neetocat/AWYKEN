namespace AWYKEN.Shared.Data;

/// <summary>Kinds of entry the school calendar can hold.</summary>
public static class CalendarEventKinds
{
    /// <summary>A whole term range, e.g. "1st Semester 2026-2027". Used to fill term dates.</summary>
    public const string Semester = "Semester";
    public const string SemesterStart = "SemesterStart";
    public const string SemesterEnd = "SemesterEnd";
    public const string Holiday = "Holiday";
    public const string Suspension = "Suspension";
    public const string SemesterBreak = "SemesterBreak";
    public const string Examination = "Examination";
    public const string Event = "Event";

    public static readonly string[] All =
    [
        Semester, SemesterStart, SemesterEnd, Holiday, Suspension, SemesterBreak, Examination, Event
    ];

    public static string Label(string kind) => kind switch
    {
        Semester => "Semester range",
        SemesterStart => "Semester start",
        SemesterEnd => "Semester end",
        Holiday => "Holiday",
        Suspension => "Suspension",
        SemesterBreak => "Semester break",
        Examination => "Examination",
        _ => "Event"
    };
}

/// <summary>A feed the admin can import entries from (an iCal URL, like the student calendar).</summary>
public sealed class CalendarFeedRecord
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncMessage { get; set; }
    public bool IsArchived { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>One holiday, suspension, break, examination, or term range in the school calendar.</summary>
public sealed class CalendarEventRecord
{
    public int Id { get; set; }
    public int? FeedId { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = CalendarEventKinds.Event;
    public DateOnly StartDate { get; set; }
    // Inclusive last day, like the student calendar.
    public DateOnly EndDate { get; set; }
    public bool IsPublished { get; set; } = true;
    public bool IsArchived { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed record CalendarEventView(
    int Id, string Title, string Kind, DateOnly StartDate, DateOnly EndDate,
    bool IsPublished, bool IsArchived, string? FeedName, bool IsFromFeed)
{
    public string KindLabel => CalendarEventKinds.Label(Kind);

    public string DateRange => StartDate == EndDate
        ? StartDate.ToString("MMM d, yyyy")
        : $"{StartDate.ToString("MMM d, yyyy")} – {EndDate.ToString("MMM d, yyyy")}";
}

public sealed record CalendarFeedView(
    int Id, string Name, string Url, bool IsEnabled, DateTime? LastSyncedAt,
    string? LastSyncMessage, bool IsArchived);
