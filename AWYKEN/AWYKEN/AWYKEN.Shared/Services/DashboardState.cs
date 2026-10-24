namespace AWYKEN.Shared.Services;

/// <summary>
/// Shared sample data for the dashboard pages. Replace these values with backend data
/// when the signed-in user's data service is connected.
/// </summary>
public sealed class DashboardState
{
    public UserInfo User { get; } = new("Mika");

    public WeatherInfo Weather { get; } = new(29, "Light rain");

    public List<ScheduleItem> Schedule { get; } =
    [
        new("1", "MATH 1102", "Mathematics in the Modern World", "7:30 AM", "9:00 AM",
            "Room 204", "Elena Santos", "ES", "Face-to-face", "#5268ca", null),
        new("2", "UTS 1001", "Understanding the Self", "10:00 AM", "11:30 AM",
            "Room 302", "Marco Villanueva", "MV", "Face-to-face", "#e58a37", null),
        new("3", "CS 2203", "Data Structures", "1:00 PM", "2:30 PM",
            "Google Meet", "Iris Mendoza", "IM", "Online", "#8056a9", "https://meet.google.com/")
    ];

    public List<AppNotification> Notifications { get; }

    public int UnreadNotificationCount => Notifications.Count(item => item.IsUnread);

    public DashboardState()
    {
        var nextClass = NextClass;
        var onlineClass = Schedule.First(item => item.Mode == "Online");

        Notifications =
        [
            new("ready", "Today", "Time to get ready",
                $"{nextClass.Title} starts at {nextClass.StartTime}. Pack your notes, calculator, and school ID.",
                "Now", "clock", true, "View checklist"),
            new("leave", "Today", "Leave in 5 minutes",
                $"Light traffic on your route. Leave by {nextClass.LeaveBy} AM to arrive comfortably.",
                "6:37 AM", "route", true, null),
            new("meeting", "Today", "Online class link ready",
                $"Your {onlineClass.Title} meeting starts at {onlineClass.StartTime}.",
                "6:30 AM", "online", true, "Join meeting"),
            new("imported", "Yesterday", "Schedule imported",
                $"{Schedule.Count} classes were added successfully from your COR.",
                "4:18 PM", "imported", false, null)
        ];
    }

    public void MarkAllNotificationsRead()
    {
        foreach (var item in Notifications)
            item.IsUnread = false;
    }

    public NextClassInfo NextClass
    {
        get
        {
            var first = Schedule[0];
            return new NextClassInfo(
                first.CourseCode, first.Title, first.StartTime, $"{first.Location} • Main Building",
                "6:15", "6:42", "1h 24m");
        }
    }
}

public sealed record UserInfo(string FirstName);
public sealed record WeatherInfo(int Temperature, string Condition);
public sealed record NextClassInfo(
    string CourseCode, string Title, string StartTime, string Location,
    string PrepareBy, string LeaveBy, string StartsIn);

public sealed record ScheduleItem(
    string Id, string CourseCode, string Title, string StartTime, string EndTime,
    string Location, string Instructor, string InstructorInitials, string Mode,
    string ColorCode, string? Link)
{
    public string Time => StartTime[..StartTime.LastIndexOf(' ')];
    public string Period => StartTime[(StartTime.LastIndexOf(' ') + 1)..];
    public string Subtitle => Mode == "Online"
        ? $"Online • {Location}"
        : $"{Location} • Prof. {Instructor}";
    public string Type => Mode == "Online" ? "online" : "in-person";
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
