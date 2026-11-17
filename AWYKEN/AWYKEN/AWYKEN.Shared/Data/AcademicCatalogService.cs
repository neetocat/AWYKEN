using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Shared.Data;

/// <summary>
/// Reads and writes the admin-owned catalog. Rows are archived rather than deleted so that
/// existing class offerings and student schedules keep pointing at something meaningful.
/// Every mutation re-checks the admin role on the server, independent of the page guard.
/// </summary>
public sealed class AcademicCatalogService(
    IDbContextFactory<AwykenDbContext> contextFactory,
    LocalAccountSession accountSession) : IAcademicCatalogService
{
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];
    private static readonly string[] Semesters = ["1st", "2nd", "Summer"];
    private static readonly Regex SchoolYearPattern = new(@"^\d{4}-\d{4}$", RegexOptions.Compiled);

    // ---------------------------------------------------------------- terms

    public async Task<IReadOnlyList<AcademicTermView>> GetTermsAsync(
        bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AcademicTerms.AsNoTracking()
            .Where(term => includeArchived || !term.IsArchived)
            .OrderByDescending(term => term.SchoolYear)
            .ThenBy(term => term.Semester)
            .Select(term => new AcademicTermView(
                term.Id, term.SchoolYear, term.Semester, term.StartDate, term.EndDate,
                term.IsCurrent, term.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> SaveTermAsync(
        AcademicTermRecord term, CancellationToken cancellationToken = default)
    {
        var schoolYear = term.SchoolYear.Trim();
        var semester = term.Semester.Trim();

        if (!SchoolYearPattern.IsMatch(schoolYear))
            return "Enter a school year in the form 2026-2027.";
        if (!Semesters.Contains(semester))
            return "Choose 1st, 2nd, or Summer.";
        if (term.EndDate <= term.StartDate)
            return "The end date must be after the start date.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        if (await db.AcademicTerms.AnyAsync(
                row => row.Id != term.Id && row.SchoolYear == schoolYear && row.Semester == semester,
                cancellationToken))
            return "That term already exists.";

        AcademicTermRecord row;
        if (term.Id == 0)
        {
            row = new AcademicTermRecord { IsCurrent = false };
            db.AcademicTerms.Add(row);
        }
        else
        {
            row = await db.AcademicTerms.SingleOrDefaultAsync(item => item.Id == term.Id, cancellationToken)
                ?? throw new InvalidOperationException("That term no longer exists.");
        }

        row.SchoolYear = schoolYear;
        row.Semester = semester;
        row.StartDate = term.StartDate;
        row.EndDate = term.EndDate;
        row.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SetCurrentTermAsync(int termId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var term = await db.AcademicTerms.SingleOrDefaultAsync(item => item.Id == termId, cancellationToken);
        if (term is null)
            return "That term no longer exists.";
        if (term.IsArchived)
            return "Restore the term before making it current.";
        if (term.IsCurrent)
            return null;

        // Clear the old current term first: the filtered unique index allows only one.
        await db.AcademicTerms
            .Where(row => row.IsCurrent && row.Id != termId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsCurrent, false), cancellationToken);

        term.IsCurrent = true;
        term.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ArchiveTermAsync(
        int termId, bool archived, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var term = await db.AcademicTerms.SingleOrDefaultAsync(item => item.Id == termId, cancellationToken);
        if (term is null)
            return "That term no longer exists.";

        term.IsArchived = archived;
        if (archived)
            term.IsCurrent = false;
        term.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // ------------------------------------------------------------- subjects

    public async Task<IReadOnlyList<SubjectView>> GetSubjectsAsync(
        bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Subjects.AsNoTracking()
            .Where(subject => includeArchived || !subject.IsArchived)
            .OrderBy(subject => subject.Code)
            .Select(subject => new SubjectView(
                subject.Id, subject.Code, subject.Title, subject.Units, subject.College, subject.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> SaveSubjectAsync(
        SubjectRecord subject, CancellationToken cancellationToken = default)
    {
        var code = subject.Code.Trim().ToUpperInvariant();
        var title = subject.Title.Trim();

        if (code.Length == 0)
            return "Enter a subject code.";
        if (title.Length == 0)
            return "Enter a subject title.";
        if (subject.Units is < 0 or > 30)
            return "Units must be between 0 and 30.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        if (await db.Subjects.AnyAsync(
                row => row.Id != subject.Id && row.Code == code, cancellationToken))
            return $"A subject with the code {code} already exists.";

        SubjectRecord row;
        if (subject.Id == 0)
        {
            row = new SubjectRecord();
            db.Subjects.Add(row);
        }
        else
        {
            row = await db.Subjects.SingleOrDefaultAsync(item => item.Id == subject.Id, cancellationToken)
                ?? throw new InvalidOperationException("That subject no longer exists.");
        }

        row.Code = code;
        row.Title = title;
        row.Units = subject.Units;
        row.College = string.IsNullOrWhiteSpace(subject.College) ? null : subject.College.Trim();
        row.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ArchiveSubjectAsync(
        int subjectId, bool archived, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var subject = await db.Subjects.SingleOrDefaultAsync(item => item.Id == subjectId, cancellationToken);
        if (subject is null)
            return "That subject no longer exists.";

        subject.IsArchived = archived;
        subject.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // ------------------------------------------------------------ offerings

    public async Task<IReadOnlyList<ClassOfferingView>> GetOfferingsAsync(
        int? termId = null, bool includeArchived = true, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Ordering happens on the joined tables, before the projection. Ordering a query whose
        // select already builds a record is not translatable by EF Core.
        return await (
                from offering in db.ClassOfferings.AsNoTracking()
                join term in db.AcademicTerms.AsNoTracking() on offering.TermId equals term.Id
                join subject in db.Subjects.AsNoTracking() on offering.SubjectId equals subject.Id
                where (includeArchived || !offering.IsArchived)
                    && (termId == null || offering.TermId == termId)
                orderby term.SchoolYear descending, term.Semester, subject.Code, offering.SectionCode
                select new ClassOfferingView(
                    offering.Id,
                    offering.TermId,
                    term.SchoolYear,
                    term.Semester,
                    subject.Id,
                    subject.Code,
                    subject.Title,
                    offering.SectionCode,
                    offering.Instructor,
                    offering.Room,
                    offering.Days,
                    offering.StartTime,
                    offering.EndTime,
                    offering.Status,
                    offering.IsArchived))
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> SaveOfferingAsync(
        ClassOfferingRecord offering, bool applyToLinkedStudents = false,
        CancellationToken cancellationToken = default)
    {
        var section = offering.SectionCode.Trim().ToUpperInvariant();
        var instructor = offering.Instructor.Trim();
        var room = offering.Room.Trim();
        var status = offering.Status.Trim();
        var days = NormalizeDays(offering.Days);

        if (offering.TermId == 0 || offering.SubjectId == 0)
            return "Choose a term and a subject.";
        if (section.Length == 0)
            return "Enter a section code.";
        if (instructor.Length == 0)
            return "Enter the instructor.";
        if (room.Length == 0)
            return "Enter the room.";
        if (days is null)
            return "Select at least one day.";
        if (offering.EndTime <= offering.StartTime)
            return "The end time must be after the start time.";
        if (!ClassOfferingStatuses.All.Contains(status))
            return "Choose Draft, Published, or Cancelled.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        if (!await db.AcademicTerms.AnyAsync(item => item.Id == offering.TermId, cancellationToken))
            return "That term no longer exists.";
        if (!await db.Subjects.AnyAsync(item => item.Id == offering.SubjectId, cancellationToken))
            return "That subject no longer exists.";

        if (await db.ClassOfferings.AnyAsync(
                row => row.Id != offering.Id && row.TermId == offering.TermId
                    && row.SubjectId == offering.SubjectId && row.SectionCode == section,
                cancellationToken))
            return "That section already exists for this subject in the chosen term.";

        ClassOfferingRecord row;
        if (offering.Id == 0)
        {
            row = new ClassOfferingRecord();
            db.ClassOfferings.Add(row);
        }
        else
        {
            row = await db.ClassOfferings.SingleOrDefaultAsync(item => item.Id == offering.Id, cancellationToken)
                ?? throw new InvalidOperationException("That class no longer exists.");
        }

        row.TermId = offering.TermId;
        row.SubjectId = offering.SubjectId;
        row.SectionCode = section;
        row.Instructor = instructor;
        row.Room = room;
        row.Days = days;
        row.StartTime = offering.StartTime;
        row.EndTime = offering.EndTime;
        row.Status = status;
        row.UpdatedAt = DateTime.UtcNow;

        // Optionally push the official values onto every student row linked to this class.
        // The rows keep their own identity, so course notes and incentives stay attached.
        if (applyToLinkedStudents && row.Id != 0)
        {
            var subject = await db.Subjects.AsNoTracking()
                .SingleAsync(item => item.Id == row.SubjectId, cancellationToken);
            var linkedRows = await db.Schedule
                .Where(item => item.ClassOfferingId == row.Id)
                .ToListAsync(cancellationToken);
            var meetingTimes = FormatMeetingTimes(row.StartTime, row.EndTime);

            foreach (var scheduleRow in linkedRows)
            {
                ApplyOfferingToSchedule(scheduleRow, row, subject, meetingTimes);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ArchiveOfferingAsync(
        int offeringId, bool archived, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var offering = await db.ClassOfferings.SingleOrDefaultAsync(
            item => item.Id == offeringId, cancellationToken);
        if (offering is null)
            return "That class no longer exists.";

        offering.IsArchived = archived;
        offering.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    // ----------------------------------------------------------- student harvest

    public async Task<IReadOnlyList<CollectedCourseView>> GetCollectedCoursesAsync(
        int? termId = null, bool onlyUnmatched = false, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.Schedule.AsNoTracking()
            .Where(row => termId == null || row.TermId == termId)
            .Select(row => new
            {
                row.ProfileId,
                row.CourseCode,
                row.Title,
                row.Section,
                row.Instructor,
                row.Location,
                row.Days,
                row.MeetingTimes,
                row.Units,
                row.TermId,
                row.ClassOfferingId
            })
            .ToListAsync(cancellationToken);

        // The catalog is small, so matching runs in memory with the same normalization the
        // grouping uses. Doing it in SQL would need a computed column for every comparison.
        var subjects = await db.Subjects.AsNoTracking()
            .Select(subject => new { subject.Id, subject.Code })
            .ToListAsync(cancellationToken);
        var subjectByKey = subjects
            .GroupBy(subject => NormalizeKey(subject.Code))
            .Where(group => group.Key.Length > 0)
            .ToDictionary(group => group.Key, group => group.First().Id);

        var offerings = await db.ClassOfferings.AsNoTracking()
            .Select(offering => new { offering.Id, offering.TermId, offering.SubjectId, offering.SectionCode })
            .ToListAsync(cancellationToken);

        var termNames = (await db.AcademicTerms.AsNoTracking()
                .Select(term => new { term.Id, term.SchoolYear, term.Semester })
                .ToListAsync(cancellationToken))
            .ToDictionary(term => term.Id, term => $"{term.SchoolYear} · {term.Semester}");

        var collected = new List<CollectedCourseView>();
        foreach (var group in rows.GroupBy(row => new
                 {
                     Code = NormalizeKey(row.CourseCode),
                     Section = NormalizeKey(row.Section),
                     row.TermId
                 }))
        {
            if (group.Key.Code.Length == 0)
                continue;

            var rowsInGroup = group.ToList();
            var subjectId = subjectByKey.TryGetValue(group.Key.Code, out var matchedSubject)
                ? matchedSubject
                : (int?)null;

            int? offeringId = null;
            if (subjectId is int subjectKey)
            {
                var termKey = group.Key.TermId;
                // A row without a term still shows the newest matching class so it can be
                // linked; linking writes the offering's term back onto the row.
                offeringId = offerings
                    .Where(offering => offering.SubjectId == subjectKey
                        && NormalizeKey(offering.SectionCode) == group.Key.Section
                        && (termKey == null || offering.TermId == termKey))
                    .OrderByDescending(offering => offering.TermId)
                    .Select(offering => (int?)offering.Id)
                    .FirstOrDefault();
            }

            collected.Add(new CollectedCourseView(
                MostCommon(rowsInGroup.Select(row => row.CourseCode)) ?? "",
                MostCommon(rowsInGroup.Select(row => row.Section)),
                MostCommon(rowsInGroup.Select(row => row.Title)) ?? "",
                MostCommon(rowsInGroup.Select(row => row.Instructor)) ?? "—",
                MostCommon(rowsInGroup.Select(row => row.Location)) ?? "—",
                MostCommon(rowsInGroup.Select(row => row.Days)) ?? "",
                MostCommon(rowsInGroup.Select(row => row.MeetingTimes)),
                MostCommonUnits(rowsInGroup.Select(row => row.Units)),
                group.Key.TermId,
                group.Key.TermId is int id && termNames.TryGetValue(id, out var name) ? name : null,
                rowsInGroup.Select(row => row.ProfileId).Distinct().Count(),
                rowsInGroup.Count,
                rowsInGroup.Count(row => row.ClassOfferingId is not null),
                subjectId,
                offeringId));
        }

        return collected
            .Where(course => !onlyUnmatched || !course.HasOffering)
            .OrderByDescending(course => course.StudentCount)
            .ThenBy(course => course.CourseCode)
            .ThenBy(course => course.Section)
            .ToList();
    }

    public async Task<string?> PromoteCourseAsync(
        CollectedCourseView course, int termId, CancellationToken cancellationToken = default)
    {
        if (termId == 0)
            return "Choose the term the new class belongs to.";

        var code = NormalizeKey(course.CourseCode);
        if (code.Length == 0)
            return "This course has no course code to promote.";

        var days = NormalizeDays(course.Days);
        if (days is null)
            return "This course has no meeting days, so it cannot become a class automatically.";

        if (!TryParseMeetingTimes(course.MeetingTimes, out var start, out var end))
            return $"The meeting time '{course.MeetingTimes}' could not be read. Create this class by hand instead.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var term = await db.AcademicTerms.SingleOrDefaultAsync(item => item.Id == termId, cancellationToken);
        if (term is null)
            return "That term no longer exists.";
        if (term.IsArchived)
            return "That term is archived. Restore it or choose another term.";

        var title = string.IsNullOrWhiteSpace(course.Title) ? course.CourseCode.Trim() : course.Title.Trim();
        var units = course.Units ?? 3m;

        var subjects = await db.Subjects.ToListAsync(cancellationToken);
        var subject = subjects.FirstOrDefault(item => NormalizeKey(item.Code) == code);
        if (subject is null)
        {
            subject = new SubjectRecord
            {
                Code = course.CourseCode.Trim().ToUpperInvariant(),
                Title = title,
                Units = units,
                UpdatedAt = DateTime.UtcNow
            };
            db.Subjects.Add(subject);
            await db.SaveChangesAsync(cancellationToken);
        }

        // "—" normalizes to an empty key, so a group without a section still matches its class.
        var section = string.IsNullOrWhiteSpace(course.Section)
            ? "—"
            : course.Section.Trim().ToUpperInvariant();

        // Idempotent: promoting the same group twice must not duplicate the class.
        if (await db.ClassOfferings.AnyAsync(
                row => row.TermId == termId && row.SubjectId == subject.Id && row.SectionCode == section,
                cancellationToken))
            return null;

        db.ClassOfferings.Add(new ClassOfferingRecord
        {
            TermId = termId,
            SubjectId = subject.Id,
            SectionCode = section,
            Instructor = string.IsNullOrWhiteSpace(course.Instructor) || course.Instructor == "—"
                ? "TBA"
                : course.Instructor.Trim(),
            Room = string.IsNullOrWhiteSpace(course.Room) || course.Room == "—"
                ? "TBA"
                : course.Room.Trim(),
            Days = days,
            StartTime = start,
            EndTime = end,
            Status = ClassOfferingStatuses.Draft,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> LinkCourseAsync(
        CollectedCourseView course, int? termId, CancellationToken cancellationToken = default)
    {
        if (course.OfferingId is not int offeringId)
            return "Promote this course into the catalog before linking students.";

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (!await IsAdminAsync(db, cancellationToken))
            return NotAnAdmin;

        var offering = await db.ClassOfferings
            .SingleOrDefaultAsync(item => item.Id == offeringId, cancellationToken)
            ?? throw new InvalidOperationException("That class no longer exists.");
        var subject = await db.Subjects
            .SingleAsync(item => item.Id == offering.SubjectId, cancellationToken);

        // Only untouched rows are claimed; a row already linked to another class is left alone.
        var candidates = await db.Schedule
            .Where(row => row.ClassOfferingId == null || row.ClassOfferingId == offeringId)
            .ToListAsync(cancellationToken);

        var code = NormalizeKey(course.CourseCode);
        var section = NormalizeKey(course.Section);
        var targetTerm = course.TermId ?? termId;
        var affected = candidates
            .Where(row => NormalizeKey(row.CourseCode) == code
                && NormalizeKey(row.Section) == section
                && (targetTerm is null || row.TermId is null || row.TermId == targetTerm))
            .ToList();

        if (affected.Count == 0)
            return "No unlinked student classes match this course any more.";

        var meetingTimes = FormatMeetingTimes(offering.StartTime, offering.EndTime);
        foreach (var scheduleRow in affected)
        {
            ApplyOfferingToSchedule(scheduleRow, offering, subject, meetingTimes);
        }

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(int Linked, int Diverged)> GetOfferingImpactAsync(
        int offeringId, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        var offering = await db.ClassOfferings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == offeringId, cancellationToken);
        if (offering is null)
            return (0, 0);

        var subject = await db.Subjects.AsNoTracking()
            .SingleAsync(item => item.Id == offering.SubjectId, cancellationToken);
        var rows = await db.Schedule.AsNoTracking()
            .Where(item => item.ClassOfferingId == offeringId)
            .ToListAsync(cancellationToken);
        var meetingTimes = FormatMeetingTimes(offering.StartTime, offering.EndTime);

        return (rows.Count, rows.Count(row => !MatchesOffering(row, offering, subject, meetingTimes)));
    }

    // ---------------------------------------------------------------- helpers

    private const string NotAnAdmin = "Only an administrator can change academic records.";
    private async Task<bool> IsAdminAsync(AwykenDbContext db, CancellationToken cancellationToken)
    {
        if (accountSession.ProfileId is not int profileId)
            return false;

        return await db.Profiles.AsNoTracking()
            .Where(profile => profile.Id == profileId)
            .Select(profile => profile.IsAdmin)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Turns "Mon, Wed" into the stored form "|Mon|Wed|"; null when nothing is valid.</summary>
    private static string? NormalizeDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var names = value
            .Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length >= 3 ? part[..3] : part)
            .Select(part => DayNames.FirstOrDefault(day =>
                string.Equals(day, part, StringComparison.OrdinalIgnoreCase)))
            .Where(day => day is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return names.Length == 0 ? null : $"|{string.Join('|', names)}|";
    }

    /// <summary>Uppercases and drops punctuation/spaces so "CC 101" and "cc-101" group together.</summary>
    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var buffer = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                buffer.Append(char.ToUpperInvariant(character));
        }

        return buffer.ToString();
    }

    private static string? MostCommon(IEnumerable<string?> values) =>
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();

    private static decimal? MostCommonUnits(IEnumerable<decimal?> values) =>
        values
            .Where(value => value.HasValue)
            .GroupBy(value => value!.Value)
            .OrderByDescending(group => group.Count())
            .Select(group => (decimal?)group.Key)
            .FirstOrDefault();

    private static string FormatMeetingTimes(TimeOnly start, TimeOnly end) =>
        $"{DateTime.Today.Add(start.ToTimeSpan()):h:mm tt} – {DateTime.Today.Add(end.ToTimeSpan()):h:mm tt}";

    /// <summary>Reads "9:00 AM – 10:30 AM" (or "9:00-10:30") into two times.</summary>
    private static bool TryParseMeetingTimes(string? value, out TimeOnly start, out TimeOnly end)
    {
        start = default;
        end = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = Regex.Split(value.Trim(), @"\s*(?:–|—|-|to)\s*", RegexOptions.IgnoreCase);
        if (parts.Length != 2)
            return false;

        return TryParseTime(parts[0], out start)
            && TryParseTime(parts[1], out end)
            && end > start;
    }

    private static bool TryParseTime(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value.Trim(),
            ["h:mm tt", "hh:mm tt", "h:mmtt", "H:mm", "HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    /// <summary>Copies the official class values onto a student's own row, keeping its Id.</summary>
    private static void ApplyOfferingToSchedule(
        ScheduleRecord scheduleRow, ClassOfferingRecord offering, SubjectRecord subject, string meetingTimes)
    {
        scheduleRow.CourseCode = subject.Code;
        scheduleRow.Title = subject.Title;
        scheduleRow.Section = offering.SectionCode;
        scheduleRow.Instructor = offering.Instructor;
        scheduleRow.Location = offering.Room;
        scheduleRow.Days = offering.Days;
        scheduleRow.MeetingTimes = meetingTimes;
        scheduleRow.Units = subject.Units;
        scheduleRow.TermId = offering.TermId;
        scheduleRow.ClassOfferingId = offering.Id;
        scheduleRow.UpdatedAt = DateTime.UtcNow;
    }

    private static bool MatchesOffering(
        ScheduleRecord row, ClassOfferingRecord offering, SubjectRecord subject, string meetingTimes) =>
        string.Equals(row.CourseCode, subject.Code, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Title, subject.Title, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Section, offering.SectionCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Instructor, offering.Instructor, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Location, offering.Room, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.Days, offering.Days, StringComparison.OrdinalIgnoreCase)
        && string.Equals(row.MeetingTimes, meetingTimes, StringComparison.OrdinalIgnoreCase)
        && row.Units == subject.Units
        && row.TermId == offering.TermId;
}
