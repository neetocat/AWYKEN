using System.Globalization;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Shared.Data;

/// <summary>Reads and writes the local SQLite database for all shared pages.</summary>
public sealed class SqliteAwykenDataService(
    IDbContextFactory<AwykenDbContext> contextFactory,
    DashboardState state,
    LocalAccountSession accountSession) : IAwykenDataService
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public bool IsAuthenticated => accountSession.IsAuthenticated;

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
            // Repair databases whose migration history says the student-profile
            // migration ran even though the column was not written to the file.
            await EnsureAcademicTermColumnAsync(db, cancellationToken);
            // Keep an anonymous Guest profile for visitors even after accounts are added.
            if (!await db.Profiles.AnyAsync(profile => profile.Email == null, cancellationToken))
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

    private static async Task EnsureAcademicTermColumnAsync(
        AwykenDbContext db, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info(Profiles);";
        await db.Database.OpenConnectionAsync(cancellationToken);

        var hasAcademicTerm = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "AcademicTerm", StringComparison.OrdinalIgnoreCase))
                {
                    hasAcademicTerm = true;
                    break;
                }
            }
        }

        if (!hasAcademicTerm)
        {
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE \"Profiles\" ADD COLUMN \"AcademicTerm\" TEXT NULL;",
                cancellationToken);
        }
    }

    public async Task<string?> SignUpAsync(
        string displayName, string email, string password, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var normalizedEmail = NormalizeEmail(email);
        if (!IsValidEmail(normalizedEmail))
            return "Enter a valid email address.";
        if (normalizedEmail.Length > 200)
            return "Email addresses must be 200 characters or fewer.";
        if (password.Length < 8)
            return "Use a password with at least 8 characters.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Profiles.AnyAsync(profile => profile.Email == normalizedEmail, cancellationToken))
            return "An account with that email already exists. Log in instead.";

        var profile = new UserProfileRecord
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? normalizedEmail[..normalizedEmail.IndexOf('@')]
                : displayName.Trim(),
            Email = normalizedEmail,
            PasswordHash = await Task.Run(() => LocalPasswordHasher.Hash(password), cancellationToken)
        };
        db.Profiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        accountSession.SignIn(profile.Id);
        await RefreshAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken);
        return null;
    }

    public async Task<string?> SignInAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var normalizedEmail = NormalizeEmail(email);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profile = await db.Profiles.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Email == normalizedEmail, cancellationToken);
        if (profile is null || !await Task.Run(() => LocalPasswordHasher.Verify(password, profile.PasswordHash), cancellationToken))
            return "Email or password is incorrect.";

        accountSession.SignIn(profile.Id);
        await RefreshAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken);
        return null;
    }

    public async Task SignOutAsync(DateOnly refreshDate, CancellationToken cancellationToken = default)
    {
        accountSession.SignOut();
        await RefreshAsync(refreshDate, cancellationToken);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool IsValidEmail(string email)
    {
        try
        {
            return new System.Net.Mail.MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private async Task<int> GetCurrentProfileIdAsync(AwykenDbContext db, CancellationToken cancellationToken)
    {
        if (accountSession.ProfileId is int signedInProfileId)
            return signedInProfileId;

        return await db.Profiles.Where(profile => profile.Email == null)
            .Select(profile => profile.Id)
            .FirstAsync(cancellationToken);
    }

    public async Task RefreshAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == profileId, cancellationToken);

        var startOfDay = date.ToDateTime(TimeOnly.MinValue);
        var startOfNextDay = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var allRows = await db.Schedule.AsNoTracking()
            .Where(item => item.ProfileId == profileId)
            .ToListAsync(cancellationToken);
        var day = date.DayOfWeek.ToString()[..3];
        var rows = allRows
            .Where(item => item.Days is null
                ? item.StartsAt >= startOfDay && item.StartsAt < startOfNextDay
                : item.Days.Contains($"|{day}|", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.StartsAt)
            .ToList();

        ApplyToSharedState(profile, rows);
    }

    public async Task RefreshWeekAsync(DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == profileId, cancellationToken);

        var weekStartDateTime = weekStart.ToDateTime(TimeOnly.MinValue);
        var weekEnd = weekStart.AddDays(7).ToDateTime(TimeOnly.MinValue);
        var weekDays = Enumerable.Range(0, 7)
            .Select(offset => weekStart.AddDays(offset).DayOfWeek.ToString()[..3])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allRows = await db.Schedule.AsNoTracking()
            .Where(item => item.ProfileId == profileId)
            .ToListAsync(cancellationToken);
        var rows = allRows
            .Where(item => item.Days is null
                ? item.StartsAt >= weekStartDateTime && item.StartsAt < weekEnd
                : weekDays.Any(day => item.Days.Contains($"|{day}|", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.StartsAt)
            .ToList();

        ApplyToSharedState(profile, rows);
    }

    private void ApplyToSharedState(UserProfileRecord? profile, IEnumerable<ScheduleRecord> rows)
    {
        // Pages share this state, so every refresh keeps profile, classes, and alerts in sync.
        var student = profile is null
            ? StudentProfile.Empty
            : new StudentProfile(profile.FullName, profile.DisplayName, profile.StudentEmail,
                profile.StudentNumber, profile.College, profile.Program, profile.YearLevel,
                profile.Section, profile.AcademicTerm);
        var commute = profile is null
            ? CommuteLocationSettings.Empty
            : new CommuteLocationSettings(profile.HomeAddress, profile.HomeLatitude, profile.HomeLongitude,
                profile.SchoolAddress, profile.SchoolLatitude, profile.SchoolLongitude);
        state.ReplaceFromDatabase(profile?.DisplayName, profile?.Email, rows.Select(ToScheduleItem), student, commute);
    }

    public async Task SaveCommuteLocationsAsync(
        CommuteLocationSettings locations, DateOnly refreshDate, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var profile = await db.Profiles.SingleAsync(item => item.Id == profileId, cancellationToken);

        profile.HomeAddress = locations.HomeAddress;
        profile.HomeLatitude = locations.HomeLatitude;
        profile.HomeLongitude = locations.HomeLongitude;
        profile.SchoolAddress = locations.SchoolAddress;
        profile.SchoolLatitude = locations.SchoolLatitude;
        profile.SchoolLongitude = locations.SchoolLongitude;

        await db.SaveChangesAsync(cancellationToken);
        // A changed endpoint invalidates the in-memory estimate until the next route lookup.
        state.SetEstimatedTravelMinutes(null);
        await RefreshAsync(refreshDate, cancellationToken);
    }

    public async Task SaveImportedProfileAsync(
        StudentProfile student, DateOnly referenceDate, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var profile = await db.Profiles.SingleAsync(item => item.Id == profileId, cancellationToken);

        // Keep login email separate from the school email parsed from a COR.
        profile.FullName = student.FullName ?? profile.FullName;
        profile.StudentEmail = student.Email ?? profile.StudentEmail;
        profile.StudentNumber = student.StudentNumber ?? profile.StudentNumber;
        profile.College = student.College ?? profile.College;
        profile.Program = student.Program ?? profile.Program;
        profile.YearLevel = student.YearLevel ?? profile.YearLevel;
        profile.Section = student.Section ?? profile.Section;
        profile.AcademicTerm = student.AcademicTerm ?? profile.AcademicTerm;
        if (string.IsNullOrWhiteSpace(profile.DisplayName))
            profile.DisplayName = student.FirstName ?? student.FullName;

        await db.SaveChangesAsync(cancellationToken);
        await RefreshAsync(referenceDate, cancellationToken);
    }

    public async Task SaveScheduleItemAsync(
        ScheduleItem item, DateOnly date, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var start = CombineDateAndTime(date, item.StartTime);
        var end = CombineDateAndTime(date, item.EndTime);

        ScheduleRecord record;
        if (int.TryParse(item.Id, out var id))
        {
            record = await db.Schedule.SingleOrDefaultAsync(
                row => row.Id == id && row.ProfileId == profileId, cancellationToken)
                ?? throw new InvalidOperationException("That class no longer exists in the local database.");
        }
        else
        {
            record = new ScheduleRecord
            {
                ProfileId = profileId,
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
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var imported = items.ToList();

        foreach (var item in imported)
        {
            var existing = await db.Schedule.FirstOrDefaultAsync(row =>
                row.ProfileId == profileId && row.CourseCode == item.CourseCode
                && row.Title == item.Title && row.Section == item.Section,
                cancellationToken);
            var start = CombineDateAndTimeOrDefault(referenceDate, item.StartTime);
            var end = CombineDateAndTimeOrDefault(referenceDate, item.EndTime, start);
            var record = existing ?? new ScheduleRecord
            {
                ProfileId = profileId,
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
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var record = await db.Schedule.SingleOrDefaultAsync(
            row => row.Id == parsedId && row.ProfileId == profileId, cancellationToken);
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
