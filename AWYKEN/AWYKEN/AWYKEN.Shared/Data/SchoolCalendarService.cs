using System.Text.RegularExpressions;
using AWYKEN.Shared.Models;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Shared.Data;

/// <summary>
/// Stores the school calendar in the database. Entries can be typed by hand or imported from
/// an iCal feed (the same format the student calendar reads), which is what makes the
/// calendar usable by Academic Management for term dates.
/// </summary>
public sealed class SchoolCalendarService(
    IDbContextFactory<AwykenDbContext> contextFactory,
    LocalAccountSession accountSession,
    HttpClient httpClient) : ISchoolCalendarService
{
    private const string NotAnAdmin = "Only an administrator can change the school calendar.";
    private static readonly Regex SchoolYearPattern = new(@"^(\d{4})-(\d{4})$", RegexOptions.Compiled);
    private static readonly SemaphoreSlim SeedGate = new(1, 1);

    // ---------------------------------------------------------------- events

    public async Task<IReadOnlyList<CalendarEventView>> GetEventsAsync(
        bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // The bundled official calendar is the initial content for a new shared calendar.
        // Seeding it here makes the admin's first visit show the same editable entries that
        // students have been seeing as the bundled fallback.
        if (await IsAdminAsync(db, cancellationToken)
            && !await db.CalendarEvents.AnyAsync(cancellationToken))
        {
            await SeedGate.WaitAsync(cancellationToken);
            try
            {
                if (!await db.CalendarEvents.AnyAsync(cancellationToken))
                {
                    var bundled = await UmakCalendarService.LoadBundledEventsAsync(cancellationToken);
                    db.CalendarEvents.AddRange(bundled.Select(item => new CalendarEventRecord
                    {
                        Title = item.Title,
                        Kind = MapKind(item.Type),
                        StartDate = DateOnly.FromDateTime(item.Start),
                        EndDate = DateOnly.FromDateTime(item.End),
                        IsPublished = true,
                        UpdatedAt = DateTime.UtcNow
                    }));
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            finally
            {
                SeedGate.Release();
            }
        }

        var events = await db.CalendarEvents.AsNoTracking()
            .Where(item => includeArchived || !item.IsArchived)
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.Title)
            .ToListAsync(cancellationToken);

        var feedNames = (await db.CalendarFeeds.AsNoTracking()
                .Select(feed => new { feed.Id, feed.Name })
                .ToListAsync(cancellationToken))
            .ToDictionary(feed => feed.Id, feed => feed.Name);

        return events
            .Select(item => new CalendarEventView(
                item.Id,
                item.Title,
                item.Kind,
                item.StartDate,
                item.EndDate,
                item.IsPublished,
                item.IsArchived,
                item.FeedId is int id && feedNames.TryGetValue(id, out var name) ? name : null,
                item.FeedId is not null))
            .ToList();
    }

    public async Task<string?> SaveEventAsync(
        CalendarEventRecord calendarEvent, CancellationToken cancellationToken = default)
    {
        var title = calendarEvent.Title.Trim();
        var kind = calendarEvent.Kind.Trim();

        if (title.Length == 0)
            return "Enter an event title.";
        if (!CalendarEventKinds.All.Contains(kind))
            return "Choose a valid event kind.";
        if (calendarEvent.EndDate < calendarEvent.StartDate)
            return "The end date cannot be before the start date.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        CalendarEventRecord row;
        if (calendarEvent.Id == 0)
        {
            row = new CalendarEventRecord { FeedId = null };
            db.CalendarEvents.Add(row);
        }
        else
        {
            row = await db.CalendarEvents.SingleOrDefaultAsync(item => item.Id == calendarEvent.Id,
                    cancellationToken)
                ?? throw new InvalidOperationException("That calendar entry no longer exists.");
        }

        row.Title = title;
        row.Kind = kind;
        row.StartDate = calendarEvent.StartDate;
        row.EndDate = calendarEvent.EndDate;
        row.IsPublished = calendarEvent.IsPublished;
        row.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SetPublishedAsync(
        int eventId, bool published, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var item = await db.CalendarEvents.SingleOrDefaultAsync(row => row.Id == eventId, cancellationToken);
        if (item is null)
            return "That calendar entry no longer exists.";

        item.IsPublished = published;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ArchiveEventAsync(
        int eventId, bool archived, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var item = await db.CalendarEvents.SingleOrDefaultAsync(row => row.Id == eventId, cancellationToken);
        if (item is null)
            return "That calendar entry no longer exists.";

        item.IsArchived = archived;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> DeleteEventAsync(
        int eventId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var item = await db.CalendarEvents.SingleOrDefaultAsync(row => row.Id == eventId, cancellationToken);
        if (item is null)
            return "That calendar entry no longer exists.";

        db.CalendarEvents.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // ----------------------------------------------------------------- feeds

    public async Task<IReadOnlyList<CalendarFeedView>> GetFeedsAsync(
        bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.CalendarFeeds.AsNoTracking()
            .Where(feed => includeArchived || !feed.IsArchived)
            .OrderBy(feed => feed.Name)
            .Select(feed => new CalendarFeedView(
                feed.Id, feed.Name, feed.Url, feed.IsEnabled, feed.LastSyncedAt,
                feed.LastSyncMessage, feed.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> SaveFeedAsync(
        CalendarFeedRecord feed, CancellationToken cancellationToken = default)
    {
        var name = feed.Name.Trim();
        var url = feed.Url.Trim();

        if (name.Length == 0)
            return "Give the data source a name.";
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "Enter an http(s) address for the feed.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        if (await db.CalendarFeeds.AnyAsync(
                row => row.Id != feed.Id && row.Url == url && !row.IsArchived, cancellationToken))
            return "That feed address is already added.";

        CalendarFeedRecord row;
        if (feed.Id == 0)
        {
            row = new CalendarFeedRecord();
            db.CalendarFeeds.Add(row);
        }
        else
        {
            row = await db.CalendarFeeds.SingleOrDefaultAsync(item => item.Id == feed.Id, cancellationToken)
                ?? throw new InvalidOperationException("That data source no longer exists.");
        }

        row.Name = name;
        row.Url = url;
        row.IsEnabled = feed.IsEnabled;
        row.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ArchiveFeedAsync(
        int feedId, bool archived, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var feed = await db.CalendarFeeds.SingleOrDefaultAsync(item => item.Id == feedId, cancellationToken);
        if (feed is null)
            return "That data source no longer exists.";

        feed.IsArchived = archived;
        feed.IsEnabled = !archived && feed.IsEnabled;
        feed.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ImportFeedAsync(int feedId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var feed = await db.CalendarFeeds.SingleOrDefaultAsync(item => item.Id == feedId, cancellationToken);
        if (feed is null)
            return "That data source no longer exists.";
        if (feed.IsArchived)
            return "Restore the data source before importing.";

        string ics;
        try
        {
            ics = await httpClient.GetStringAsync(feed.Url, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            feed.LastSyncedAt = DateTime.UtcNow;
            feed.LastSyncMessage = "Download failed.";
            await db.SaveChangesAsync(cancellationToken);
            return "The feed could not be downloaded. Check the address and that it is public.";
        }

        IReadOnlyList<UmakEvent> parsed;
        try
        {
            UmakCalendarParser.EnsureValidIcs(ics);
            parsed = UmakCalendarParser.ParseIcs(
                ics,
                DateOnly.FromDateTime(DateTime.Today.AddYears(-1)),
                DateOnly.FromDateTime(DateTime.Today.AddYears(3)));
        }
        catch (FormatException exception)
        {
            feed.LastSyncedAt = DateTime.UtcNow;
            feed.LastSyncMessage = "The download is not a valid iCal feed.";
            await db.SaveChangesAsync(cancellationToken);
            return exception.Message;
        }

        var existing = await db.CalendarEvents
            .Where(item => item.FeedId == feedId)
            .ToListAsync(cancellationToken);

        var added = 0;
        var refreshed = 0;
        foreach (var item in parsed)
        {
            var start = DateOnly.FromDateTime(item.Start);
            var end = DateOnly.FromDateTime(item.End);
            if (end < start)
                end = start;

            var kind = MapKind(item.Type);
            var match = existing.FirstOrDefault(row =>
                string.Equals(row.Title, item.Title, StringComparison.OrdinalIgnoreCase)
                && row.StartDate == start);

            if (match is null)
            {
                db.CalendarEvents.Add(new CalendarEventRecord
                {
                    FeedId = feedId,
                    Title = item.Title,
                    Kind = kind,
                    StartDate = start,
                    EndDate = end,
                    IsPublished = true,
                    UpdatedAt = DateTime.UtcNow
                });
                added++;
            }
            else
            {
                match.EndDate = end;
                match.Kind = kind;
                match.UpdatedAt = DateTime.UtcNow;
                refreshed++;
            }
        }

        feed.LastSyncedAt = DateTime.UtcNow;
        feed.LastSyncMessage = $"{added} added, {refreshed} refreshed.";
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // ------------------------------------------------------------ term dates

    public async Task<(DateOnly Start, DateOnly End)?> FindTermDatesAsync(
        string schoolYear, string semester, CancellationToken cancellationToken = default)
    {
        if (!TryParseSchoolYear(schoolYear, out var startYear))
            return null;

        var semesterKey = SemesterKey(semester);
        if (semesterKey is null)
            return null;

        var rangeStart = new DateOnly(startYear, 7, 1);
        var rangeEnd = new DateOnly(startYear + 1, 6, 30);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.CalendarEvents.AsNoTracking()
            .Where(item => !item.IsArchived
                && item.StartDate >= rangeStart
                && item.StartDate <= rangeEnd)
            .ToListAsync(cancellationToken);

        // An explicitly entered Semester range is the clearest source when present.
        var ranges = candidates
            .Where(item => item.Kind == CalendarEventKinds.Semester)
            .Where(item => TitleMentionsSemester(item.Title, semesterKey))
            .ToList();
        if (ranges.Count > 0)
        {
            var range = ranges.OrderBy(item => item.StartDate).First();
            return range.EndDate > range.StartDate ? (range.StartDate, range.EndDate) : null;
        }

        // Otherwise pair entries by their Kind and chronological position. This works for
        // titles such as "First Day of Classes" that do not contain "1st semester".
        var starts = candidates
            .Where(item => IsCalendarKind(item.Kind, CalendarEventKinds.SemesterStart))
            .OrderBy(item => item.StartDate)
            .ToList();
        var remainingEnds = candidates
            .Where(item => IsCalendarKind(item.Kind, CalendarEventKinds.SemesterEnd))
            .OrderBy(item => item.EndDate)
            .ToList();

        var pairs = new List<(DateOnly Start, DateOnly End)>();
        foreach (var startEvent in starts)
        {
            var endIndex = remainingEnds.FindIndex(item => item.EndDate >= startEvent.StartDate);
            if (endIndex < 0)
                continue;

            var endEvent = remainingEnds[endIndex];
            remainingEnds.RemoveAt(endIndex);
            pairs.Add((startEvent.StartDate, endEvent.EndDate));
        }

        var pairIndex = semesterKey switch { "1" => 0, "2" => 1, _ => 2 };
        if (pairIndex < pairs.Count)
            return pairs[pairIndex];

        // Keep title-based lookup for calendars that label a standalone start/end by semester.
        var matching = candidates
            .Where(item => TitleMentionsSemester(item.Title, semesterKey))
            .ToList();
        var titledStart = matching.FirstOrDefault(item => IsCalendarKind(item.Kind, CalendarEventKinds.SemesterStart));
        var titledEnd = matching.FirstOrDefault(item => IsCalendarKind(item.Kind, CalendarEventKinds.SemesterEnd));
        return titledStart is not null && titledEnd is not null && titledEnd.EndDate > titledStart.StartDate
            ? (titledStart.StartDate, titledEnd.EndDate)
            : null;
    }

    private static bool IsCalendarKind(string actual, string expected)
    {
        static string Normalize(string value) => new(value.Where(char.IsLetterOrDigit).ToArray());
        return string.Equals(Normalize(actual), Normalize(expected), StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- helpers

    private async Task<bool> IsAdminAsync(AwykenDbContext db, CancellationToken cancellationToken)
    {
        if (accountSession.ProfileId is not int profileId)
            return false;

        return await db.Profiles.AsNoTracking()
            .Where(profile => profile.Id == profileId)
            .Select(profile => profile.IsAdmin)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string MapKind(UmakEventType type) => type switch
    {
        UmakEventType.SemesterStart => CalendarEventKinds.SemesterStart,
        UmakEventType.SemesterEnd => CalendarEventKinds.SemesterEnd,
        UmakEventType.Holiday => CalendarEventKinds.Holiday,
        UmakEventType.Exam => CalendarEventKinds.Examination,
        _ => CalendarEventKinds.Event
    };

    private static bool TryParseSchoolYear(string schoolYear, out int startYear)
    {
        startYear = 0;
        var match = SchoolYearPattern.Match(schoolYear.Trim());
        if (!match.Success)
            return false;

        startYear = int.Parse(match.Groups[1].Value);
        var endYear = int.Parse(match.Groups[2].Value);
        return endYear == startYear + 1;
    }

    private static string? SemesterKey(string semester)
    {
        var value = semester.Trim().ToLowerInvariant();
        if (value.StartsWith("1") || value.Contains("first"))
            return "1";
        if (value.StartsWith("2") || value.Contains("second"))
            return "2";
        return value.Contains("summer") ? "summer" : null;
    }

    private static bool TitleMentionsSemester(string title, string semesterKey)
    {
        var value = title.ToLowerInvariant();
        return semesterKey switch
        {
            "1" => value.Contains("1st") || value.Contains("first semester") || value.Contains("1st semester"),
            "2" => value.Contains("2nd") || value.Contains("second semester") || value.Contains("2nd semester"),
            _ => value.Contains("summer")
        };
    }
}
