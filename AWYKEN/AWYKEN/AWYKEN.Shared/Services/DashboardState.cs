namespace AWYKEN.Shared.Services;

/// <summary>
/// The display state shared by the Dashboard, Schedule, Commute, and Alerts pages.
/// Pages refresh this state from SQLite so they show the same profile and classes.
/// </summary>
public sealed class DashboardState
{
    // A null display name means the person has not set a profile name; show Guest.
    public UserInfo User { get; private set; } = new(null, null);

    // Weather is optional and remains empty until a weather service is connected.
    public WeatherInfo Weather { get; } = new(null, "No weather data");

    // The initial local database has no classes, so all schedule views start blank.
    public List<ScheduleItem> Schedule { get; private set; } = [];

    // Parsed COR classes stay separate until the user confirms them on the review page.
    public List<ScheduleItem> ImportPreview { get; private set; } = [];
    public bool HasImportPreview => ImportPreview.Count > 0;

    // Alerts are derived from the selected day's classes; there are none when the schedule is empty.
    public List<AppNotification> Notifications { get; private set; } = [];

    public int UnreadNotificationCount => Notifications.Count(item => item.IsUnread);

    /// <summary>Replace the shared profile and schedule after reading SQLite.</summary>
    public void ReplaceFromDatabase(string? displayName, string? email, IEnumerable<ScheduleItem> schedule)
    {
        User = new UserInfo(displayName, email);
        Schedule = schedule.OrderBy(item => item.SortTime).ToList();

        // Build simple alerts from real class rows. An empty database remains quiet.
        Notifications = Schedule.Select(item => new AppNotification(
            $"class-{item.Id}",
            "Today",
            item.Title,
            $"{item.CourseCode} starts at {item.StartTime}.",
            item.StartTime,
            item.Mode == "Online" ? "online" : "clock",
            true,
            item.Mode == "Online" ? "Join meeting" : null)).ToList();
    }

    public void SetImportPreview(IEnumerable<ScheduleItem> classes)
    {
        ImportPreview = classes.ToList();
    }

    public void SaveImportPreview()
    {
        if (!HasImportPreview)
            return;

        Schedule.Clear();
        Schedule.AddRange(ImportPreview);
        ImportPreview = [];
    }

    public void UpdateImportPreview(ScheduleItem updated)
    {
        var index = ImportPreview.FindIndex(item => item.Id == updated.Id);
        if (index >= 0)
            ImportPreview[index] = updated;
    }

    public void ClearImportPreview() => ImportPreview = [];

    public void MarkAllNotificationsRead()
    {
        foreach (var item in Notifications)
            item.IsUnread = false;
    }

    /// <summary>Use the first upcoming class, or a clear empty-state value.</summary>
    public NextClassInfo NextClass
    {
        get
        {
            var first = Schedule.FirstOrDefault();
            if (first is null)
                return new NextClassInfo("", "No upcoming classes", "—", "No location yet", "—", "—", "—");

            return new NextClassInfo(
                first.CourseCode,
                first.Title,
                first.StartTime,
                first.Location,
                "—",
                "—",
                "—");
        }
    }
}

/// <summary>A null/blank profile name is intentionally displayed as Guest.</summary>
public sealed record UserInfo(string? FirstName, string? Email)
{
    public string DisplayName => string.IsNullOrWhiteSpace(FirstName) ? "Guest" : FirstName;
}

public sealed record WeatherInfo(int? Temperature, string Condition);
public sealed record NextClassInfo(
    string CourseCode, string Title, string StartTime, string Location,
    string PrepareBy, string LeaveBy, string StartsIn);

/// <summary>Display-friendly class data shared by the current pages.</summary>
public sealed record ScheduleItem(
    string Id, string CourseCode, string Title, string StartTime, string EndTime,
    string Location, string Instructor, string InstructorInitials, string Mode,
    string ColorCode, string? Link, string? Cfn = null, string? Section = null,
    decimal? Units = null, string? Days = null, string? MeetingTimes = null)
{
    public string DisplayTimes => string.IsNullOrWhiteSpace(MeetingTimes)
        ? $"{StartTime}{(string.IsNullOrWhiteSpace(EndTime) ? "" : $" – {EndTime}")}"
        : MeetingTimes;
    public string Time => StartTime.Contains(' ') ? StartTime[..StartTime.LastIndexOf(' ')] : StartTime;
    public string Period => StartTime.Contains(' ') ? StartTime[(StartTime.LastIndexOf(' ') + 1)..] : "";
    public string Subtitle => Mode == "Online"
        ? $"Online • {Location}"
        : $"{Location} • Prof. {Instructor}";
    public string Type => Mode == "Online" ? "online" : "in-person";

    // Used only for consistent display ordering; persisted timestamps remain in the database.
    public DateTime SortTime => DateTime.TryParse(StartTime, out var time) ? time : DateTime.MaxValue;
}

public sealed class AppNotification(
    string id, string group, string title, string message, string time,
    string kind, bool isUnread, string? actionLabel)
{
    public string Id { get; } = id;
    public string Group { get; } = group;
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string Time { get; } = time;
    public string Kind { get; } = kind;
    public bool IsUnread { get; set; } = isUnread;
    public string? ActionLabel { get; } = actionLabel;
}
