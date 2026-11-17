namespace AWYKEN.Shared.Data;

/// <summary>
/// One academic period, e.g. "2026-2027, 1st Semester". Admin-owned reference data; students
/// and classes point at it instead of repeating free text. Rows are archived, never deleted.
/// </summary>
public sealed class AcademicTermRecord
{
    public int Id { get; set; }
    public string SchoolYear { get; set; } = "";
    public string Semester { get; set; } = "";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    // Exactly one term may be current; enforced by a filtered unique index.
    public bool IsCurrent { get; set; }
    public bool IsArchived { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A subject in the school catalog, independent of any student's schedule.</summary>
public sealed class SubjectRecord
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal Units { get; set; } = 3m;
    public string? College { get; set; }
    public bool IsArchived { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// One class offering: a subject taught in a term, with its section, teacher, room and meeting
/// pattern. This is the school-wide row; a student's <see cref="ScheduleRecord"/> stays separate.
/// </summary>
public sealed class ClassOfferingRecord
{
    public int Id { get; set; }
    public int TermId { get; set; }
    public int SubjectId { get; set; }
    public string SectionCode { get; set; } = "";
    public string Instructor { get; set; } = "";
    public string Room { get; set; } = "";
    // Same convention as the student schedule: "|Mon|Wed|".
    public string Days { get; set; } = "";
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public string Status { get; set; } = ClassOfferingStatuses.Draft;
    public bool IsArchived { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class ClassOfferingStatuses
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Cancelled = "Cancelled";

    public static readonly string[] All = [Draft, Published, Cancelled];
}

/// <summary>Flat projection of a term for the admin tables.</summary>
public sealed record AcademicTermView(
    int Id, string SchoolYear, string Semester, DateOnly StartDate, DateOnly EndDate,
    bool IsCurrent, bool IsArchived)
{
    public string Name => $"{SchoolYear} · {Semester} semester";
}

/// <summary>Flat projection of a subject for the admin tables.</summary>
public sealed record SubjectView(
    int Id, string Code, string Title, decimal Units, string? College, bool IsArchived);

/// <summary>
/// One distinct course gathered from the students' own schedule rows, grouped by course code,
/// section, and term. This is the admin work queue: promote it into the catalog, then link it.
/// </summary>
public sealed record CollectedCourseView(
    string CourseCode,
    string? Section,
    string Title,
    string Instructor,
    string Room,
    string Days,
    string? MeetingTimes,
    decimal? Units,
    int? TermId,
    string? TermName,
    int StudentCount,
    int RowCount,
    int LinkedCount,
    int? SubjectId,
    int? OfferingId)
{
    public bool HasSubject => SubjectId is not null;

    public bool HasOffering => OfferingId is not null;

    public string Status => HasOffering ? "In catalog" : HasSubject ? "Subject only" : "Not in catalog";

    public string DisplaySection => string.IsNullOrWhiteSpace(Section) ? "—" : Section;
}

/// <summary>Class offering joined with its term and subject for the admin tables.</summary>
public sealed record ClassOfferingView(
    int Id, int TermId, string TermSchoolYear, string TermSemester,
    int SubjectId, string SubjectCode, string SubjectTitle,
    string SectionCode, string Instructor, string Room, string Days,
    TimeOnly StartTime, TimeOnly EndTime, string Status, bool IsArchived)
{
    public string TermName => $"{TermSchoolYear} · {TermSemester}";
    public string DisplayDays => Days.Replace("|", ", ").Trim(' ', ',');
    public string TimeRange =>
        $"{DateTime.Today.Add(StartTime.ToTimeSpan()):h:mm tt} – {DateTime.Today.Add(EndTime.ToTimeSpan()):h:mm tt}";
}
