using System.Data.Common;
using System.Globalization;
using AWYKEN.Shared.Configuration;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AWYKEN.Shared.Data;

/// <summary>
/// Reads and writes the shared Azure SQL Database for all shared pages. Every query is
/// scoped to the signed-in profile (or the anonymous Guest profile) so the cloud rows
/// behave like the previous per-installation local database.
/// </summary>
public sealed class SqlServerAwykenDataService(
    IDbContextFactory<AwykenDbContext> contextFactory,
    DashboardState state,
    LocalAccountSession accountSession,
    AwykenDatabaseSettings settings) : IAwykenDataService
{
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private bool _initialized;

    public bool IsAuthenticated => accountSession.IsAuthenticated;
    public async Task<bool> HasTeacherPinAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        return await db.Profiles.AsNoTracking().Where(profile => profile.Id == profileId)
            .Select(profile => profile.TeacherPinHash != null).SingleAsync(cancellationToken);
    }

    public async Task<string?> CreateTeacherPinAsync(string pin, string confirmation,
        CancellationToken cancellationToken = default)
    {
        if (pin.Length != 4 || !pin.All(char.IsAsciiDigit))
            return "Create a four-digit numeric password.";
        if (!string.Equals(pin, confirmation, StringComparison.Ordinal))
            return "The passwords do not match.";

        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var profile = await db.Profiles.SingleAsync(item => item.Id == profileId, cancellationToken);
        if (!string.IsNullOrEmpty(profile.TeacherPinHash))
            return "A teacher password has already been created for this profile.";

        profile.TeacherPinHash = await Task.Run(() => LocalPasswordHasher.Hash(pin), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<bool> ValidateTeacherPinAsync(string pin, CancellationToken cancellationToken = default)
    {
        if (pin.Length != 4 || !pin.All(char.IsAsciiDigit))
            return false;
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var hash = await db.Profiles.AsNoTracking().Where(profile => profile.Id == profileId)
            .Select(profile => profile.TeacherPinHash).SingleAsync(cancellationToken);
        return await Task.Run(() => LocalPasswordHasher.Verify(pin, hash), cancellationToken);
    }

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
            if (settings.ApplyMigrationsOnStartup)
                await MigrateAsync(db, cancellationToken);

            // Keep an anonymous Guest profile for visitors even after accounts are added.
            if (!await db.Profiles.AnyAsync(profile => profile.Email == null, cancellationToken))
            {
                db.Profiles.Add(new UserProfileRecord { DisplayName = null, Email = null });
                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                }
                catch (DbUpdateException)
                {
                    // A second client may have inserted the Guest row at the same moment;
                    // that is harmless as long as one anonymous row exists now.
                    db.ChangeTracker.Clear();
                    if (!await db.Profiles.AnyAsync(profile => profile.Email == null, cancellationToken))
                        throw;
                }
            }

            // Optional shared administrator from .env; created once, then it lives in the database.
            await EnsureDefaultAdminAsync(db, cancellationToken);

            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    /// <summary>
    /// Creates the shared administrator described by AWYKEN_DEFAULT_ADMIN_EMAIL /
    /// AWYKEN_DEFAULT_ADMIN_PASSWORD when it does not exist yet. An existing account is only
    /// promoted, never re-passworded, so a password already stored is never overwritten.
    /// Removing the keys stops seeding but leaves the account in place.
    /// </summary>
    private async Task EnsureDefaultAdminAsync(AwykenDbContext db, CancellationToken cancellationToken)
    {
        if (!settings.HasDefaultAdmin)
            return;

        var email = settings.DefaultAdminEmail!;
        var existing = await db.Profiles
            .SingleOrDefaultAsync(profile => profile.Email == email, cancellationToken);

        if (existing is null)
        {
            var password = settings.DefaultAdminPassword!;
            var hash = await Task.Run(() => LocalPasswordHasher.Hash(password), cancellationToken);
            db.Profiles.Add(new UserProfileRecord
            {
                DisplayName = string.IsNullOrWhiteSpace(settings.DefaultAdminName)
                    ? "Administrator"
                    : settings.DefaultAdminName.Trim(),
                Email = email,
                IsAdmin = true,
                PasswordHash = hash
            });

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Another client seeded the same account first; promote it instead.
                db.ChangeTracker.Clear();
                var seeded = await db.Profiles
                    .SingleOrDefaultAsync(profile => profile.Email == email, cancellationToken);
                if (seeded is null)
                    throw;
                if (!seeded.IsAdmin)
                {
                    seeded.IsAdmin = true;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            return;
        }

        if (!existing.IsAdmin)
        {
            existing.IsAdmin = true;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Applies pending migrations through EF Core and turns transport failures into a
    /// message that names the server, the database, and the usual Azure causes.
    /// </summary>
    private async Task MigrateAsync(AwykenDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        catch (RetryLimitExceededException exception)
        {
            // The execution strategy already retried; report the newest underlying failure.
            var cause = exception.InnerException ?? exception;
            throw new InvalidOperationException(BuildUnreachableMessage(cause), cause);
        }
        catch (DbException exception)
        {
            throw new InvalidOperationException(BuildUnreachableMessage(exception), exception);
        }
    }

    private string BuildUnreachableMessage(Exception cause) =>
        $"AWYKEN could not reach the Azure SQL database '{settings.InitialCatalog}' on " +
        $"'{settings.DataSource}'. Check the AZURE_SQL_* values in .env (or the environment), " +
        "the Azure SQL firewall rule for this network, and that the database still exists. " +
        $"Underlying error: {cause.Message}";

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
            // Signing up with an address from AWYKEN_ADMIN_EMAILS creates an admin directly.
            IsAdmin = IsConfiguredAdmin(normalizedEmail),
            PasswordHash = await Task.Run(() => LocalPasswordHasher.Hash(password), cancellationToken),
            AccountStatus = "Active",
            RegisteredAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        try
        {
            db.Profiles.Add(profile);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique index on Email catches a sign-up that raced another device.
            return "An account with that email already exists. Log in instead.";
        }

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
        // Tracked, so an account listed in AWYKEN_ADMIN_EMAILS can be promoted in place.
        var profile = await db.Profiles
            .SingleOrDefaultAsync(item => item.Email == normalizedEmail, cancellationToken);
        if (profile is null || !await Task.Run(() => LocalPasswordHasher.Verify(password, profile.PasswordHash), cancellationToken))
            return "Email or password is incorrect.";

        if (!profile.IsAdmin && IsConfiguredAdmin(normalizedEmail))
        {
            profile.IsAdmin = true;
        }
        if (!string.Equals(profile.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
            return profile.AccountStatus.Equals("Suspended", StringComparison.OrdinalIgnoreCase)
                ? "This account is suspended. Contact your school administrator."
                : "This account is deactivated. Contact your school administrator.";

        profile.LastActivityAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        accountSession.SignIn(profile.Id);
        await RefreshAsync(DateOnly.FromDateTime(DateTime.Today), cancellationToken);
        return null;
    }

    public async Task<bool> IsAdminAsync(CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (accountSession.ProfileId is not int profileId)
            return false;

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Profiles.AsNoTracking()
            .Where(profile => profile.Id == profileId)
            .Select(profile => profile.IsAdmin)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private bool IsConfiguredAdmin(string normalizedEmail) =>
        settings.AdminEmails.Contains(normalizedEmail, StringComparer.OrdinalIgnoreCase);

    public async Task SignOutAsync(DateOnly refreshDate, CancellationToken cancellationToken = default)
    {
        accountSession.SignOut();
        await RefreshAsync(refreshDate, cancellationToken);
    }

    public async Task<bool> AccountExistsAsync(string email, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        var normalizedEmail = NormalizeEmail(email);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Profiles.AsNoTracking()
            .AnyAsync(profile => profile.Email == normalizedEmail, cancellationToken);
    }

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
        {
            var status = await db.Profiles.AsNoTracking().Where(profile => profile.Id == signedInProfileId)
                .Select(profile => new { profile.Email, profile.AccountStatus }).SingleOrDefaultAsync(cancellationToken);
            if (status?.Email is not null && !string.Equals(status.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
                accountSession.SignOut();
            else if (status is not null)
                return signedInProfileId;
        }

        // The lowest id keeps the Guest row stable even if an old database holds more than one.
        return await db.Profiles.Where(profile => profile.Email == null)
            .OrderBy(profile => profile.Id)
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
        if (profile?.Email is not null && string.Equals(profile.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var activityAt = DateTime.UtcNow;
            await db.Profiles.Where(item => item.Id == profileId).ExecuteUpdateAsync(update => update.SetProperty(item => item.LastActivityAt, activityAt), cancellationToken);
        }

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
        if (profile?.Email is not null && string.Equals(profile.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var activityAt = DateTime.UtcNow;
            await db.Profiles.Where(item => item.Id == profileId).ExecuteUpdateAsync(update => update.SetProperty(item => item.LastActivityAt, activityAt), cancellationToken);
        }

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

    public async Task RefreshMonthAsync(DateOnly monthStart, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var profile = await db.Profiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == profileId, cancellationToken);
        if (profile?.Email is not null && string.Equals(profile.AccountStatus, "Active", StringComparison.OrdinalIgnoreCase))
        {
            var activityAt = DateTime.UtcNow;
            await db.Profiles.Where(item => item.Id == profileId).ExecuteUpdateAsync(update => update.SetProperty(item => item.LastActivityAt, activityAt), cancellationToken);
        }

        var start = monthStart.ToDateTime(TimeOnly.MinValue);
        var end = monthStart.AddMonths(1).ToDateTime(TimeOnly.MinValue);
        var allRows = await db.Schedule.AsNoTracking()
            .Where(item => item.ProfileId == profileId)
            .ToListAsync(cancellationToken);
        // Load all recurring rows plus one-time rows falling inside this month.
        var rows = allRows
            .Where(item => item.Days is not null || (item.StartsAt >= start && item.StartsAt < end))
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
                profile.Section, profile.AcademicTerm, profile.StudentAddress, profile.Guardian,
                profile.Gender, profile.DateOfBirth);
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
        profile.StudentAddress = student.Address ?? profile.StudentAddress;
        profile.Guardian = student.Guardian ?? profile.Guardian;
        profile.Gender = student.Gender ?? profile.Gender;
        profile.DateOfBirth = student.DateOfBirth ?? profile.DateOfBirth;
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
                ?? throw new InvalidOperationException("That class no longer exists in the database.");
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
        record.Days = NormalizeDays(item.Days)
            ?? (item.ScheduledDate is null ? $"|{date.DayOfWeek.ToString()[..3]}|" : null);
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

        // Importing a COR replaces the complete saved timetable for this profile.
        // The deletes and inserts are queued together and applied by one SaveChangesAsync
        // call, which EF sends inside a single transaction. An explicit BeginTransaction
        // would be rejected by the retrying execution strategy configured in AddAwykenData.
        var currentSchedule = await db.Schedule
            .Where(row => row.ProfileId == profileId)
            .ToListAsync(cancellationToken);
        db.Schedule.RemoveRange(currentSchedule);

        foreach (var item in imported)
        {
            var start = CombineDateAndTimeOrDefault(referenceDate, item.StartTime);
            var end = CombineDateAndTimeOrDefault(referenceDate, item.EndTime, start);
            var record = new ScheduleRecord
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

    public async Task SaveCourseNoteAsync(string scheduleId, DateOnly classDate, string text, string? news,
        string createdAtText, string? attendanceStatus, byte[]? imageData, string? imageContentType,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (!int.TryParse(scheduleId, out var parsedId))
            throw new InvalidOperationException("Save the class before adding a note.");

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var schedule = await db.Schedule.SingleAsync(
            row => row.Id == parsedId && row.ProfileId == profileId, cancellationToken);
        var note = await db.Courses.SingleOrDefaultAsync(row =>
            row.ProfileId == profileId && row.ScheduleRecordId == parsedId
            && row.ClassDate == classDate.ToDateTime(TimeOnly.MinValue), cancellationToken);

        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(news)
            && string.IsNullOrWhiteSpace(attendanceStatus) && imageData is null)
        {
            if (note is not null)
            {
                db.Courses.Remove(note);
                await db.SaveChangesAsync(cancellationToken);
            }
            return;
        }

        if (note is null)
        {
            note = new CourseNoteRecord
            {
                ProfileId = profileId,
                ScheduleRecordId = parsedId,
                ClassDate = classDate.ToDateTime(TimeOnly.MinValue),
                ClassTimeText = $"{schedule.StartsAt.ToString("h:mm tt", CultureInfo.InvariantCulture)} – {schedule.EndsAt.ToString("h:mm tt", CultureInfo.InvariantCulture)}",
                CreatedAtText = createdAtText,
                CourseCode = schedule.CourseCode,
                CourseTitle = schedule.Title
            };
            db.Courses.Add(note);
        }

        note.Text = text;
        note.News = news;
        note.CreatedAtText = createdAtText;
        note.ClassTimeText = $"{schedule.StartsAt.ToString("h:mm tt", CultureInfo.InvariantCulture)} – {schedule.EndsAt.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
        note.AttendanceStatus = attendanceStatus;
        note.CourseCode = schedule.CourseCode;
        note.CourseTitle = schedule.Title;
        note.ImageData = imageData;
        note.ImageContentType = imageContentType;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CourseNoteRecord>> GetCourseNotesAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        return await db.Courses.AsNoTracking()
            .Where(note => note.ProfileId == profileId)
            .OrderByDescending(note => note.ClassDate)
            .ThenByDescending(note => note.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScheduleItem>> GetAllScheduleItemsAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var rows = await db.Schedule.AsNoTracking().Where(row => row.ProfileId == profileId)
            .OrderBy(row => row.StartsAt).ToListAsync(cancellationToken);
        return rows.Select(ToScheduleItem).ToList();
    }

    public async Task<CourseNoteRecord?> GetCourseNoteAsync(string scheduleId, DateOnly classDate,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        if (!int.TryParse(scheduleId, out var parsedId)) return null;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        return await db.Courses.AsNoTracking().SingleOrDefaultAsync(note =>
            note.ProfileId == profileId && note.ScheduleRecordId == parsedId
            && note.ClassDate == classDate.ToDateTime(TimeOnly.MinValue), cancellationToken);
    }

    public async Task UpdateCourseNoteAsync(int noteId, string text, string? news, string createdAtText,
        string? attendanceStatus, byte[]? imageData, string? imageContentType,
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var note = await db.Courses.SingleOrDefaultAsync(
            row => row.Id == noteId && row.ProfileId == profileId, cancellationToken)
            ?? throw new InvalidOperationException("That course note could not be found.");

        // Update this occurrence row only; its course, date, and scheduled time remain attached.
        note.Text = text;
        note.News = news;
        note.CreatedAtText = createdAtText;
        note.AttendanceStatus = attendanceStatus;
        note.ImageData = imageData;
        note.ImageContentType = imageContentType;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StudentIncentiveRecord>> GetStudentIncentivesAsync(
        int scheduleRecordId, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        return await db.StudentIncentives.AsNoTracking()
            .Where(item => item.ProfileId == profileId && item.ScheduleRecordId == scheduleRecordId)
            .OrderByDescending(item => item.CreatedAt).ToListAsync(cancellationToken);
    }

    public async Task AddStudentIncentiveAsync(int scheduleRecordId, string teacherPin, int points,
        string reason, CancellationToken cancellationToken = default)
    {
        if (!await ValidateTeacherPinAsync(teacherPin, cancellationToken))
            throw new UnauthorizedAccessException("The teacher password is incorrect.");
        if (points < 1 || points > 1000)
            throw new ArgumentOutOfRangeException(nameof(points), "Points must be between 1 and 1000.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 300)
            throw new ArgumentException("Enter a reason of up to 300 characters.", nameof(reason));

        await InitializeAsync(cancellationToken);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var profileId = await GetCurrentProfileIdAsync(db, cancellationToken);
        var schedule = await db.Schedule.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == scheduleRecordId && row.ProfileId == profileId, cancellationToken)
            ?? throw new InvalidOperationException("That subject is no longer on this COR.");
        db.StudentIncentives.Add(new StudentIncentiveRecord
        {
            ProfileId = profileId,
            ScheduleRecordId = schedule.Id,
            CourseCode = schedule.CourseCode,
            CourseTitle = schedule.Title,
            Points = points,
            Reason = reason.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
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
            string.Equals(row.ColorCode, "#5268ca", StringComparison.OrdinalIgnoreCase)
                ? ScheduleColorPalette.ForRecordId(row.Id)
                : row.ColorCode,
            row.MeetingLink,
            row.Cfn,
            row.Section,
            row.Units,
            row.Days?.Replace("|", ", ").Trim(' ', ','),
            row.MeetingTimes,
            row.Days is null ? DateOnly.FromDateTime(row.StartsAt) : null,
            row.Notes,
            row.AttendanceStatus);
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
