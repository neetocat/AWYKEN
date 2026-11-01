namespace AWYKEN.Shared.Services;

/// <summary>
/// The display state shared by the Dashboard, Schedule, Commute, and Alerts pages.
/// Pages refresh this state from SQLite so they show the same profile and classes.
/// </summary>
public sealed class DashboardState
{
    // A null display name means the person has not set a profile name; show Guest.
    public UserInfo User { get; private set; } = new(null, null);

    // Student details (student number, program, year level...) read from the COR.
    public StudentProfile Student { get; private set; } = StudentProfile.Empty;

    // Home and campus endpoints are loaded from the current profile's SQLite row.
    public CommuteLocationSettings CommuteLocations { get; private set; } = CommuteLocationSettings.Empty;

    // Weather is refreshed from the saved home coordinates when Home opens.
    public WeatherInfo Weather { get; private set; } = new(null, "Set home location");

    // The latest OSRM estimate is reused to calculate dashboard departure times.
    public int? EstimatedTravelMinutes { get; private set; }

    // The initial local database has no classes, so all schedule views start blank.
    public List<ScheduleItem> Schedule { get; private set; } = [];

    // Parsed COR classes stay separate until the user confirms them on the review page.
    public List<ScheduleItem> ImportPreview { get; private set; } = [];
    public bool HasImportPreview => ImportPreview.Count > 0;

    // The student details read from the COR also wait for the user's confirmation.
    public StudentProfile? ImportProfilePreview { get; private set; }

    // Alerts are derived from the selected day's classes; there are none when the schedule is empty.
    public List<AppNotification> Notifications { get; private set; } = [];

    public int UnreadNotificationCount => Notifications.Count(item => item.IsUnread);

    public void SetWeather(WeatherInfo weather) => Weather = weather;

    public void SetEstimatedTravelMinutes(int? minutes) => EstimatedTravelMinutes = minutes;

    /// <summary>Replace the shared profile and schedule after reading SQLite.</summary>
    public void ReplaceFromDatabase(
        string? displayName, string? email, IEnumerable<ScheduleItem> schedule,
        StudentProfile? student = null, CommuteLocationSettings? commuteLocations = null)
    {
        User = new UserInfo(displayName, email);
        Student = student ?? StudentProfile.Empty;
        CommuteLocations = commuteLocations ?? CommuteLocationSettings.Empty;
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

    public void SetImportPreview(IEnumerable<ScheduleItem> classes, StudentProfile? profile = null)
    {
        ImportPreview = classes.ToList();
        ImportProfilePreview = profile;
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

    public void ClearImportPreview()
    {
        ImportPreview = [];
        ImportProfilePreview = null;
    }

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
            var now = DateTime.Now;
            var next = Schedule
                .Select(item => new { Item = item, StartsAt = GetTodayStart(item.StartTime) })
                .Where(item => item.StartsAt is DateTime startsAt && startsAt >= now)
                .OrderBy(item => item.StartsAt)
                .FirstOrDefault();
            if (next is null || next.StartsAt is not DateTime classStart)
                return new NextClassInfo("", "No more classes today", "—", "No location yet", "—", "—", "—", false);

            var leaveBy = EstimatedTravelMinutes is int travelMinutes
                ? classStart.AddMinutes(-travelMinutes - 5).ToString("h:mm tt")
                : "Set route";
            var prepareBy = EstimatedTravelMinutes is int
                ? classStart.AddMinutes(-EstimatedTravelMinutes.Value - 5 - 15).ToString("h:mm tt")
                : "Set route";

            return new NextClassInfo(
                next.Item.CourseCode,
                next.Item.Title,
                classStart.ToString("h:mm tt"),
                next.Item.Location,
                prepareBy,
                leaveBy,
                FormatStartsIn(classStart - now),
                true);
        }
    }

    private static DateTime? GetTodayStart(string time)
    {
        if (!TimeOnly.TryParse(time, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            return null;
        return DateTime.Today.Add(parsed.ToTimeSpan());
    }

    private static string FormatStartsIn(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.FromMinutes(1))
            return "now";
        var minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        return minutes < 60
            ? $"{minutes}m"
            : $"{minutes / 60}h {minutes % 60}m";
    }
}

/// <summary>Saved address and map coordinates for the two commute endpoints.</summary>
public sealed record CommuteLocationSettings(
    string? HomeAddress, double? HomeLatitude, double? HomeLongitude,
    string? SchoolAddress, double? SchoolLatitude, double? SchoolLongitude)
{
    public static CommuteLocationSettings Empty { get; } = new(null, null, null, null, null, null);
}

/// <summary>A null/blank profile name is intentionally displayed as Guest.</summary>
public sealed record UserInfo(string? FirstName, string? Email)
{
    public string DisplayName => string.IsNullOrWhiteSpace(FirstName) ? "Guest" : FirstName;
}

public sealed record WeatherInfo(int? Temperature, string Condition);
public sealed record NextClassInfo(
    string CourseCode, string Title, string StartTime, string Location,
    string PrepareBy, string LeaveBy, string StartsIn, bool HasUpcomingClass = true);

/// <summary>Display-friendly class data shared by the current pages.</summary>
public sealed record ScheduleItem(
    string Id, string CourseCode, string Title, string StartTime, string EndTime,
    string Location, string Instructor, string InstructorInitials, string Mode,
    string ColorCode, string? Link, string? Cfn = null, string? Section = null,
    decimal? Units = null, string? Days = null, string? MeetingTimes = null,
    DateOnly? ScheduledDate = null, string? Notes = null, string? AttendanceStatus = null)
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

public static class ScheduleColorPalette
{
    private static readonly string[] Colors =
    [
        "#5268ca", "#e58a37", "#8056a9", "#15905d", "#c66b00",
        "#d65376", "#168a9b", "#6e8f32", "#9b5b3e", "#536d9b"
    ];

    public static string ForRecordId(int id) => Colors[Math.Abs(id - 1) % Colors.Length];
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
