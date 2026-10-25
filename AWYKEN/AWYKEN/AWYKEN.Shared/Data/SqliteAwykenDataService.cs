using System.Globalization;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Shared.Data;

/// <summary>Reads and writes the local SQLite database for all shared pages.</summary>
public sealed class SqliteAwykenDataService(
    IDbContextFactory<AwykenDbContext> contextFactory,
    DashboardState state) : IAwykenDataService
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

            // Apply schema migrations first. A missing profile gets a null name,
            // which DashboardState presents as Guest; no schedule rows are seeded.
            await db.Database.MigrateAsync(cancellationToken);
            if (!await db.Profiles.AnyAsync(cancellationToken))
            {
                db.Profiles.Add(new UserProfileRecord { DisplayName = null, Email = null });
                await db.SaveChangesAsync(cancellationToken);
            }

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task RefreshAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.Profiles.AsNoTracking()
            .OrderBy(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var startOfDay = date.ToDateTime(TimeOnly.MinValue);
        var startOfNextDay = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var allRows = await db.Schedule.AsNoTracking().ToListAsync(cancellationToken);
        var day = date.DayOfWeek.ToString()[..3];
        var rows = allRows
            .Where(item => item.Days is null
                ? item.StartsAt >= startOfDay && item.StartsAt < startOfNextDay
                : item.Days.Contains($"|{day}|", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.StartsAt)
            .ToList();

        // Every page reads into the same scoped state object, so name, schedule,
        // next class, and derived alerts agree across navigation.
        state.ReplaceFromDatabase(
            profile?.DisplayName,
            profile?.Email,
            rows.Select(ToScheduleItem));
    }

    public async Task SaveScheduleItemAsync(
        ScheduleItem item, DateOnly date, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var start = CombineDateAndTime(date, item.StartTime);
        var end = CombineDateAndTime(date, item.EndTime);

        ScheduleRecord record;
        if (int.TryParse(item.Id, out var id))
        {
            record = await db.Schedule.SingleOrDefaultAsync(row => row.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("That class no longer exists in the local database.");
        }
        else
        {
            record = new ScheduleRecord
            {
                CourseCode = item.CourseCode,
                Title = item.Title,
                Location = item.Location,
                Instructor = item.Instructor,
                Mode = item.Mode,
                ColorCode = item.ColorCode
            };
            db.Schedule.Add(record);
        }

        record.StartsAt = start;
        record.EndsAt = end;
        record.CourseCode = item.CourseCode;
        record.Title = item.Title;
        record.Location = item.Location;
        record.Instructor = item.Instructor;
        record.Mode = item.Mode;
        record.ColorCode = item.ColorCode;
        record.MeetingLink = item.Link;
        record.Cfn = item.Cfn;
        record.Section = item.Section;
        record.Units = item.Units;
        record.Days = NormalizeDays(item.Days) ?? $"|{date.DayOfWeek.ToString()[..3]}|";
        record.MeetingTimes = item.MeetingTimes;

        await db.SaveChangesAsync(cancellationToken);
        await RefreshAsync(date, cancellationToken);
    }

    public async Task SaveImportedScheduleAsync(
        IEnumerable<ScheduleItem> items, DateOnly referenceDate, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var imported = items.ToList();

        foreach (var item in imported)
        {
            var existing = await db.Schedule.FirstOrDefaultAsync(row =>
                row.CourseCode == item.CourseCode && row.Title == item.Title && row.Section == item.Section,
                cancellationToken);
            var start = CombineDateAndTimeOrDefault(referenceDate, item.StartTime);
            var end = CombineDateAndTimeOrDefault(referenceDate, item.EndTime, start);
            var record = existing ?? new ScheduleRecord
            {
                StartsAt = start,
                EndsAt = end,
                CourseCode = item.CourseCode,
                Title = item.Title,
                Location = item.Location,
                Instructor = item.Instructor,
                Mode = item.Mode,
                ColorCode = item.ColorCode
            };

            record.StartsAt = start;
            record.EndsAt = end;
            record.CourseCode = item.CourseCode;
            record.Title = item.Title;
            record.Location = item.Location;
            record.Instructor = item.Instructor;
            record.Mode = item.Mode;
            record.ColorCode = item.ColorCode;
            record.Cfn = item.Cfn;
            record.Section = item.Section;
            record.Units = item.Units;
            record.Days = NormalizeDays(item.Days);
            record.MeetingTimes = item.MeetingTimes;
            record.MeetingLink = item.Link;
            if (existing is null)
                db.Schedule.Add(record);
        }

        await db.SaveChangesAsync(cancellationToken);
        await RefreshAsync(referenceDate, cancellationToken);
    }

    public async Task DeleteScheduleItemAsync(
        string id, DateOnly date, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (!int.TryParse(id, out var parsedId))
            return;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var record = await db.Schedule.SingleOrDefaultAsync(row => row.Id == parsedId, cancellationToken);
        if (record is not null)
        {
            db.Schedule.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
        }

        await RefreshAsync(date, cancellationToken);
    }

    private static ScheduleItem ToScheduleItem(ScheduleRecord row)
    {
        var initials = string.Join("", row.Instructor
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0])));

        return new ScheduleItem(
            row.Id.ToString(CultureInfo.InvariantCulture),
            row.CourseCode,
            row.Title,
            row.StartsAt.ToString("h:mm tt", CultureInfo.InvariantCulture),
            row.EndsAt.ToString("h:mm tt", CultureInfo.InvariantCulture),
            row.Location,
            row.Instructor,
            initials,
            row.Mode,
            row.ColorCode,
            row.MeetingLink,
            row.Cfn,
            row.Section,
            row.Units,
            row.Days?.Replace("|", ", ").Trim(' ', ','),
            row.MeetingTimes);
    }

    private static DateTime CombineDateAndTimeOrDefault(DateOnly date, string time, DateTime? fallback = null)
    {
        if (DateTime.TryParseExact(time, ["h:mm tt", "H:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedTime))
            return date.ToDateTime(TimeOnly.FromDateTime(parsedTime));

        return fallback ?? date.ToDateTime(TimeOnly.MinValue);
    }

    private static string? NormalizeDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var names = value.Split([',', '|', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length >= 3 ? part[..3] : part)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return $"|{string.Join('|', names)}|";
    }

    private static DateTime CombineDateAndTime(DateOnly date, string time)
    {
        if (!DateTime.TryParseExact(time, ["h:mm tt", "H:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedTime))
            throw new InvalidOperationException($"The class time '{time}' is not valid.");

        return date.ToDateTime(TimeOnly.FromDateTime(parsedTime));
    }
}
