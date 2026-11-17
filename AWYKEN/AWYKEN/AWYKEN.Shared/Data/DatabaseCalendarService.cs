using AWYKEN.Shared.Models;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Shared.Data;

/// <summary>
/// The one shared school calendar. Reads the admin-managed <c>CalendarEvents</c> table, so an
/// entry added, edited, published, or archived in the admin dashboard is what every student and
/// guest sees on <c>/umak-events</c>.
/// <para>
/// While the table has no published entries the built-in UMak sample is shown instead, so a
/// fresh database never renders an empty calendar.
/// </para>
/// </summary>
public sealed class DatabaseCalendarService(IDbContextFactory<AwykenDbContext> contextFactory)
    : IUmakCalendarService
{
    private UmakCalendarStatus _status = new(UmakCalendarSource.Bundled, null, null);

    public UmakCalendarStatus Status => _status;

    public Task<IReadOnlyList<UmakEvent>> GetEventsAsync(CancellationToken ct = default) =>
        GetEventsAsync(IUmakCalendarService.GetSchoolYear(UmakCalendarParser.TodayInManila()), ct);

    public async Task<IReadOnlyList<UmakEvent>> GetEventsAsync(int schoolYearStart, CancellationToken ct = default)
    {
        var events = await LoadAsync(ct);
        var (start, end) = IUmakCalendarService.GetSchoolYearRange(schoolYearStart);
        var first = start.ToDateTime(TimeOnly.MinValue);
        var last = end.ToDateTime(TimeOnly.MinValue);

        return events
            .Where(item => item.Start.Date <= last && item.End.Date >= first)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<int>> GetSchoolYearsAsync(CancellationToken ct = default)
    {
        var events = await LoadAsync(ct);
        var current = IUmakCalendarService.GetSchoolYear(UmakCalendarParser.TodayInManila());
        var years = UmakCalendarParser.GetSchoolYears(events);
        years.Add(current);
        return years.OrderByDescending(year => year).ToList();
    }

    /// <summary>Nothing is cached, so an admin edit is visible on the next page load.</summary>
    public void Refresh()
    {
    }

    private async Task<IReadOnlyList<UmakEvent>> LoadAsync(CancellationToken ct)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        // Semester ranges are only for term dates; as student events they would span months.
        var hasAdminEntries = await db.CalendarEvents.AsNoTracking().AnyAsync(ct);
        if (!hasAdminEntries)
        {
            _status = new UmakCalendarStatus(UmakCalendarSource.Bundled, null, null);
            return await UmakCalendarService.LoadBundledEventsAsync(ct);
        }

        var rows = await db.CalendarEvents.AsNoTracking()
            .Where(item => item.IsPublished && !item.IsArchived && item.Kind != CalendarEventKinds.Semester)
            .OrderBy(item => item.StartDate)
            .ToListAsync(ct);

        _status = new UmakCalendarStatus(UmakCalendarSource.Database, null, null);
        return rows.Select(ToEvent).ToList();
    }

    private static UmakEvent ToEvent(CalendarEventRecord row) => new()
    {
        Title = row.Title,
        Start = row.StartDate.ToDateTime(TimeOnly.MinValue),
        End = row.EndDate.ToDateTime(TimeOnly.MinValue),
        IsAllDay = true,
        Type = MapType(row.Kind),
        Description = ""
    };

    private static UmakEventType MapType(string kind) => kind switch
    {
        CalendarEventKinds.SemesterStart => UmakEventType.SemesterStart,
        CalendarEventKinds.SemesterEnd => UmakEventType.SemesterEnd,
        CalendarEventKinds.Examination => UmakEventType.Exam,
        CalendarEventKinds.Holiday or CalendarEventKinds.Suspension or CalendarEventKinds.SemesterBreak
            => UmakEventType.Holiday,
        _ => UmakEventType.Event
    };
}
