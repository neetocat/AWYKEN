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
        return await db.ClassOfferings.AsNoTracking()
            .Where(offering => (includeArchived || !offering.IsArchived)
                && (termId == null || offering.TermId == termId))
            .Join(db.AcademicTerms.AsNoTracking(),
                offering => offering.TermId, term => term.Id, (offering, term) => new { offering, term })
            .Join(db.Subjects.AsNoTracking(),
                pair => pair.offering.SubjectId, subject => subject.Id,
                (pair, subject) => new ClassOfferingView(
                    pair.offering.Id,
                    pair.offering.TermId,
                    pair.term.SchoolYear,
                    pair.term.Semester,
                    subject.Id,
                    subject.Code,
                    subject.Title,
                    pair.offering.SectionCode,
                    pair.offering.Instructor,
                    pair.offering.Room,
                    pair.offering.Days,
                    pair.offering.StartTime,
                    pair.offering.EndTime,
                    pair.offering.Status,
                    pair.offering.IsArchived))
            .OrderByDescending(offering => offering.TermSchoolYear)
            .ThenBy(offering => offering.SubjectCode)
            .ThenBy(offering => offering.SectionCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> SaveOfferingAsync(
        ClassOfferingRecord offering, CancellationToken cancellationToken = default)
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
}
