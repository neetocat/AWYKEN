using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AWYKEN.Shared.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;

namespace AWYKEN.Shared.Services;

/// <summary>Where the events currently being shown came from.</summary>
public enum UmakCalendarSource { Live, Cache, Bundled }

/// <param name="Source">Live feed, the last saved copy of the feed, or the built-in sample JSON.</param>
/// <param name="RetrievedAt">When the data was downloaded (Live/Cache). Null for the bundled sample.</param>
/// <param name="Error">Why the live feed could not be used, or null if nothing went wrong.</param>
public sealed record UmakCalendarStatus(UmakCalendarSource Source, DateTimeOffset? RetrievedAt, string? Error);

public interface IUmakCalendarService
{
    /// <summary>How the most recent load was satisfied. Read it after awaiting a Get call.</summary>
    UmakCalendarStatus Status { get; }

    /// <summary>Events for the current school year (Asia/Manila date).</summary>
    Task<IReadOnlyList<UmakEvent>> GetEventsAsync(CancellationToken ct = default);

    /// <summary>Events for the school year that starts in August of <paramref name="schoolYearStart"/>.</summary>
    Task<IReadOnlyList<UmakEvent>> GetEventsAsync(int schoolYearStart, CancellationToken ct = default);

    /// <summary>School years (start years) that have events, always including the current one. Newest first.</summary>
    Task<IReadOnlyList<int>> GetSchoolYearsAsync(CancellationToken ct = default);

    /// <summary>Makes the next Get call re-check the live feed instead of reusing the in-memory copy.</summary>
    void Refresh();

    /// <summary>A school year runs 1 August to 31 July, e.g. 2026 gives 2026-08-01 to 2027-07-31.</summary>
    static (DateOnly Start, DateOnly End) GetSchoolYearRange(int startYear) =>
        (new DateOnly(startYear, 8, 1), new DateOnly(startYear + 1, 7, 31));

    /// <summary>The school year (its start year) that contains <paramref name="date"/>.</summary>
    static int GetSchoolYear(DateOnly date) => date.Month >= 8 ? date.Year : date.Year - 1;

    /// <summary>"AY 2026-2027"</summary>
    static string FormatSchoolYear(int startYear) => $"AY {startYear}-{startYear + 1}";
}

/// <summary>
/// Reads the UMak academic calendar from a public Google Calendar .ics feed.
/// Order of preference: live feed, then the last feed saved on this device, then the bundled sample JSON.
/// </summary>
public sealed class UmakCalendarService : IUmakCalendarService
{
    /// <summary>
    /// Public .ics address of the UMak academic calendar. Leave empty to use only the bundled sample data.
    /// (Google Calendar > Settings > Integrate calendar > "Public address in iCal format".)
    /// </summary>
    public const string DefaultFeedUrl = "";

    private const string BundledResourceName = "umak-calendar.json";
    private static readonly TimeSpan LiveFeedLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private readonly HttpClient _httpClient;
    private readonly string _feedUrl;
    private readonly string _cachePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;

    public UmakCalendarService(HttpClient httpClient)
        : this(httpClient, DefaultFeedUrl, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AWYKEN", "umak-calendar.ics"))
    {
    }

    internal UmakCalendarService(HttpClient httpClient, string feedUrl, string cachePath)
    {
        _httpClient = httpClient;
        _feedUrl = feedUrl;
        _cachePath = cachePath;
    }

    public UmakCalendarStatus Status => _snapshot?.Status ?? new UmakCalendarStatus(UmakCalendarSource.Bundled, null, null);

    public Task<IReadOnlyList<UmakEvent>> GetEventsAsync(CancellationToken ct = default) =>
        GetEventsAsync(IUmakCalendarService.GetSchoolYear(UmakCalendarParser.TodayInManila()), ct);

    public async Task<IReadOnlyList<UmakEvent>> GetEventsAsync(int schoolYearStart, CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct);
        var (start, end) = IUmakCalendarService.GetSchoolYearRange(schoolYearStart);

        if (snapshot.IcsText is { } ics)
            return await Task.Run(() => UmakCalendarParser.ParseIcs(ics, start, end), ct);

        return UmakCalendarParser.FilterRange(snapshot.BundledEvents ?? [], start, end);
    }

    public async Task<IReadOnlyList<int>> GetSchoolYearsAsync(CancellationToken ct = default)
    {
        var snapshot = await GetSnapshotAsync(ct);
        var current = IUmakCalendarService.GetSchoolYear(UmakCalendarParser.TodayInManila());

        var years = snapshot.IcsText is { } ics
            ? await Task.Run(() => UmakCalendarParser.GetSchoolYears(ics, current), ct)
            : UmakCalendarParser.GetSchoolYears(snapshot.BundledEvents ?? []);

        years.Add(current);
        return years.OrderByDescending(year => year).ToList();
    }

    public void Refresh() => _snapshot = null;

    // ---- loading: live feed -> saved copy -> bundled sample ----

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_snapshot is { } existing && DateTimeOffset.UtcNow - existing.CheckedAt < MaxAge(existing))
                return existing;

            return _snapshot = await LoadAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    // A good live copy is reused for an hour. Fallback copies are retried every minute so the page recovers
    // quickly once the network is back. With no feed URL there is nothing to retry.
    private TimeSpan MaxAge(Snapshot snapshot) =>
        string.IsNullOrWhiteSpace(_feedUrl) ? TimeSpan.MaxValue
        : snapshot.Status.Source == UmakCalendarSource.Live ? LiveFeedLifetime
        : RetryDelay;

    private async Task<Snapshot> LoadAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        string? error = null;

        if (!string.IsNullOrWhiteSpace(_feedUrl))
        {
            try
            {
                var text = await _httpClient.GetStringAsync(_feedUrl, ct);
                UmakCalendarParser.EnsureValidIcs(text);
                await TrySaveCacheAsync(text, ct);
                return new Snapshot(text, null, new UmakCalendarStatus(UmakCalendarSource.Live, now, null), now);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                error = "Couldn't download the calendar feed.";
            }

            try
            {
                if (File.Exists(_cachePath))
                {
                    var text = await File.ReadAllTextAsync(_cachePath, ct);
                    UmakCalendarParser.EnsureValidIcs(text);
                    var savedAt = new DateTimeOffset(File.GetLastWriteTimeUtc(_cachePath), TimeSpan.Zero);
                    return new Snapshot(text, null, new UmakCalendarStatus(UmakCalendarSource.Cache, savedAt, error), now);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Unreadable saved copy: fall through to the bundled sample.
            }
        }

        var bundled = await LoadBundledAsync(ct);
        return new Snapshot(null, bundled, new UmakCalendarStatus(UmakCalendarSource.Bundled, null, error), now);
    }

    private async Task TrySaveCacheAsync(string text, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            // Write to a temp file first so a crash mid-write can't corrupt the last good copy.
            var temp = _cachePath + ".tmp";
            await File.WriteAllTextAsync(temp, text, ct);
            File.Move(temp, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failing to save the offline copy must never break showing the live data.
        }
    }

    private static async Task<IReadOnlyList<UmakEvent>> LoadBundledAsync(CancellationToken ct)
    {
        // Embedded in AWYKEN.Shared (see the csproj) so the Web and MAUI hosts load it the same way.
        await using var stream = typeof(UmakCalendarService).Assembly.GetManifestResourceStream(BundledResourceName)
            ?? throw new FileNotFoundException($"Bundled calendar '{BundledResourceName}' is missing.");
        var file = await JsonSerializer.DeserializeAsync<BundledCalendar>(stream, JsonOptions, ct);
        return file?.Events.Select(item => item.ToEvent()).ToList() ?? [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed record Snapshot(
        string? IcsText, IReadOnlyList<UmakEvent>? BundledEvents, UmakCalendarStatus Status, DateTimeOffset CheckedAt);

    private sealed class BundledCalendar
    {
        public List<BundledEvent> Events { get; set; } = [];
    }

    private sealed class BundledEvent
    {
        public string Title { get; set; } = "";
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public bool IsAllDay { get; set; } = true;
        public UmakEventType? Type { get; set; }
        public string? Description { get; set; }

        public UmakEvent ToEvent()
        {
            // In the JSON, an all-day End is the inclusive last day (no iCal-style exclusive end).
            var start = IsAllDay ? Start.Date : Start;
            var end = End is { } value ? (IsAllDay ? value.Date : value) : start;
            return new UmakEvent
            {
                Title = Title,
                Start = start,
                End = end < start ? start : end,
                IsAllDay = IsAllDay,
                Type = Type ?? UmakCalendarParser.Classify(Title),
                Description = Description ?? ""
            };
        }
    }
}

/// <summary>Turns iCal text into <see cref="UmakEvent"/> values. Pure and static so it is easy to test.</summary>
public static class UmakCalendarParser
{
    // ---------------------------------------------------------------------------------------------
    // EDIT HERE: title keywords used to classify an event. Case-insensitive regular expressions.
    // Rules are checked top to bottom and the first match wins; anything unmatched is UmakEventType.Event.
    // ---------------------------------------------------------------------------------------------
    private static readonly (UmakEventType Type, string Pattern)[] ClassificationRules =
    [
        (UmakEventType.SemesterStart, @"start of (the )?(classes|semester|(1st|2nd|first|second|summer) (sem|term))|first day of (classes|the semester)|opening of classes|classes begin"),
        (UmakEventType.SemesterEnd,   @"end of (the )?(\w+ )?(semester|classes|term)|last day of (classes|the semester)|classes end|semester ends?"),
        (UmakEventType.Holiday,       @"holiday|no classes?|suspension|suspended|non-working"),
        (UmakEventType.Exam,          @"\bexams?\b|\bexaminations?\b|\bmidterms?\b|\bfinals?\b")
    ];

    private static readonly (UmakEventType Type, Regex Regex)[] CompiledRules = ClassificationRules
        .Select(rule => (rule.Type, new Regex(rule.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
        .ToArray();

    private static readonly TimeZoneInfo ManilaZone = FindManilaZone();

    private static TimeZoneInfo FindManilaZone()
    {
        foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }

        // Metro Manila has no daylight saving, so a fixed UTC+8 is always correct.
        return TimeZoneInfo.CreateCustomTimeZone("Asia/Manila", TimeSpan.FromHours(8), "Philippine Standard Time", "PST");
    }

    public static DateOnly TodayInManila() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaZone));

    public static UmakEventType Classify(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return UmakEventType.Event;

        foreach (var (type, regex) in CompiledRules)
            if (regex.IsMatch(title))
                return type;

        return UmakEventType.Event;
    }

    /// <summary>Throws if the text is not a readable iCalendar file.</summary>
    public static void EnsureValidIcs(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("The feed is not an iCalendar file.");
        if (Calendar.Load(text) is null)
            throw new FormatException("The feed could not be read.");
    }

    /// <summary>
    /// Expands the feed (including RRULE recurrences) for the dates <paramref name="rangeStart"/> to
    /// <paramref name="rangeEnd"/> inclusive, converted to Asia/Manila, sorted by start.
    /// </summary>
    public static IReadOnlyList<UmakEvent> ParseIcs(string ics, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var calendar = Calendar.Load(ics) ?? throw new FormatException("The feed could not be read.");
        var rangeFirst = rangeStart.ToDateTime(TimeOnly.MinValue);
        var toExclusive = rangeEnd.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // Start one day early: floating/zoned comparisons inside Ical.Net can be off by a few hours.
        var searchStart = new CalDateTime(rangeFirst.AddDays(-1), hasTime: true);
        var events = new List<UmakEvent>();

        foreach (var occurrence in calendar.GetOccurrences<CalendarEvent>(searchStart))
        {
            if (occurrence.Source is not CalendarEvent source || !source.IsActive)
                continue;

            var start = ToManila(occurrence.Period.StartTime);

            // Occurrences arrive in start order, so once we are past the range we can stop.
            // That is what keeps an open-ended RRULE from running forever. The extra day absorbs time zone skew.
            if (start >= toExclusive.AddDays(1))
                break;

            var isAllDay = !occurrence.Period.StartTime.HasTime;
            var end = ResolveEnd(occurrence.Period.StartTime, occurrence.Period.EffectiveEndTime, start);

            if (end.Date < rangeFirst || start >= toExclusive)
                continue;

            var title = string.IsNullOrWhiteSpace(source.Summary) ? "(Untitled event)" : source.Summary.Trim();
            events.Add(new UmakEvent
            {
                Title = title,
                Start = start,
                End = end,
                IsAllDay = isAllDay,
                Type = Classify(title),
                Description = source.Description?.Trim() ?? ""
            });
        }

        return events.OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Only the events that touch the given dates (inclusive), sorted by start.</summary>
    public static IReadOnlyList<UmakEvent> FilterRange(IEnumerable<UmakEvent> events, DateOnly rangeStart, DateOnly rangeEnd)
    {
        var rangeFirst = rangeStart.ToDateTime(TimeOnly.MinValue);
        var rangeLast = rangeEnd.ToDateTime(TimeOnly.MinValue);
        return events
            .Where(e => e.Start.Date <= rangeLast && e.End.Date >= rangeFirst)
            .OrderBy(e => e.Start).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static HashSet<int> GetSchoolYears(IEnumerable<UmakEvent> events)
    {
        var years = new HashSet<int>();
        foreach (var e in events)
        {
            years.Add(IUmakCalendarService.GetSchoolYear(DateOnly.FromDateTime(e.Start)));
            years.Add(IUmakCalendarService.GetSchoolYear(DateOnly.FromDateTime(e.End)));
        }
        return years;
    }

    /// <summary>
    /// School years the feed can show: the year of every event's start, plus every year up to the
    /// following one for events that repeat with no end (limited to five years back).
    /// </summary>
    public static HashSet<int> GetSchoolYears(string ics, int currentSchoolYear)
    {
        var calendar = Calendar.Load(ics) ?? throw new FormatException("The feed could not be read.");
        var years = new HashSet<int>();

        foreach (var source in calendar.Events)
        {
            if (source.DtStart is not { } dtStart)
                continue;

            var firstYear = IUmakCalendarService.GetSchoolYear(DateOnly.FromDateTime(ToManila(dtStart)));
            years.Add(firstYear);

            if (source.RecurrenceRule is { } rule)
            {
                var lastYear = rule.Until is { } until
                    ? IUmakCalendarService.GetSchoolYear(DateOnly.FromDateTime(ToManila(until)))
                    : currentSchoolYear + 1;

                for (var year = Math.Max(firstYear, currentSchoolYear - 5); year <= Math.Min(lastYear, currentSchoolYear + 1); year++)
                    years.Add(year);
            }
        }

        return years;
    }

    /// <summary>Converts an iCal value to Asia/Manila. Date-only values stay at midnight of that date.</summary>
    public static DateTime ToManila(CalDateTime value)
    {
        if (!value.HasTime)
            return value.Date.ToDateTime(TimeOnly.MinValue);

        // A time with no zone ("floating") is taken to already be Manila time.
        if (value.IsFloating)
            return DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified);

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(value.AsUtc, ManilaZone), DateTimeKind.Unspecified);
    }

    /// <summary>
    /// iCal all-day events end on an EXCLUSIVE date (a one-day event on 10 Aug ends on 11 Aug),
    /// so the inclusive last day is one day earlier. Timed events keep their exact end.
    /// </summary>
    public static DateTime ResolveEnd(CalDateTime start, CalDateTime? endExclusive, DateTime startManila)
    {
        if (endExclusive is null)
            return startManila;

        var end = ToManila(endExclusive);
        if (!start.HasTime)
            end = end.AddDays(-1);

        return end < startManila ? startManila : end;
    }
}
