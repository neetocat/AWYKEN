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
        var rows = await db.Schedule.AsNoTracking()
            .Where(item => item.StartsAt >= startOfDay && item.StartsAt < startOfNextDay)
            .OrderBy(item => item.StartsAt)
            .ToListAsync(cancellationToken);

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

        await db.SaveChangesAsync(cancellationToken);
        await RefreshAsync(date, cancellationToken);
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
            row.MeetingLink);
    }

    private static DateTime CombineDateAndTime(DateOnly date, string time)
    {
        if (!DateTime.TryParseExact(time, ["h:mm tt", "H:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsedTime))
            throw new InvalidOperationException($"The class time '{time}' is not valid.");

        return date.ToDateTime(TimeOnly.FromDateTime(parsedTime));
    }
}
