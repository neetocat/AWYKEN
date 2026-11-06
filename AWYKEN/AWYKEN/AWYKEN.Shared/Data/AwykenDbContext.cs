using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Shared.Data;

/// <summary>
/// Defines the Azure SQL Database tables used by the shared AWYKEN pages.
/// Rows are scoped by <see cref="UserProfileRecord.Id"/> so one account only ever
/// reads and writes its own profile, classes, and course notes.
/// </summary>
public sealed class AwykenDbContext(DbContextOptions<AwykenDbContext> options)
    : DbContext(options)
{
    public DbSet<UserProfileRecord> Profiles => Set<UserProfileRecord>();
    public DbSet<ScheduleRecord> Schedule => Set<ScheduleRecord>();
    public DbSet<CourseNoteRecord> Courses => Set<CourseNoteRecord>();
    public DbSet<StudentIncentiveRecord> StudentIncentives => Set<StudentIncentiveRecord>();
    public DbSet<AcademicTermRecord> AcademicTerms => Set<AcademicTermRecord>();
    public DbSet<SubjectRecord> Subjects => Set<SubjectRecord>();
    public DbSet<ClassOfferingRecord> ClassOfferings => Set<ClassOfferingRecord>();
    public DbSet<CalendarFeedRecord> CalendarFeeds => Set<CalendarFeedRecord>();
    public DbSet<CalendarEventRecord> CalendarEvents => Set<CalendarEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserProfileRecord>(entity =>
        {
            entity.ToTable("Profiles");
            entity.Property(profile => profile.DisplayName).HasMaxLength(100);
            // Emails are normalized to lower case in code, and Azure SQL keeps its
            // default case-insensitive collation, so no per-column collation is needed.
            entity.Property(profile => profile.Email).HasMaxLength(200);
            entity.Property(profile => profile.PasswordHash).HasMaxLength(300);
            entity.Property(profile => profile.TeacherPinHash).HasMaxLength(300);
            // Existing rows get 0 when the column is added, so nobody becomes admin by accident.
            entity.Property(profile => profile.IsAdmin).HasDefaultValue(false);
            entity.Property(profile => profile.FullName).HasMaxLength(150);
            entity.Property(profile => profile.StudentEmail).HasMaxLength(200);
            entity.Property(profile => profile.StudentNumber).HasMaxLength(30);
            entity.Property(profile => profile.College).HasMaxLength(200);
            entity.Property(profile => profile.Program).HasMaxLength(200);
            entity.Property(profile => profile.YearLevel).HasMaxLength(40);
            entity.Property(profile => profile.Section).HasMaxLength(80);
            entity.Property(profile => profile.AcademicTerm).HasMaxLength(80);
            entity.Property(profile => profile.StudentAddress).HasMaxLength(300);
            entity.Property(profile => profile.Guardian).HasMaxLength(150);
            entity.Property(profile => profile.Gender).HasMaxLength(40);
            entity.Property(profile => profile.DateOfBirth).HasMaxLength(40);
            entity.Property(profile => profile.HomeAddress).HasMaxLength(300);
            entity.Property(profile => profile.SchoolAddress).HasMaxLength(300);
            // One row per email, while the anonymous Guest profile keeps a null email.
            // The filter uses SQL Server bracket syntax rather than SQLite double quotes.
            entity.HasIndex(profile => profile.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
        });

        modelBuilder.Entity<ScheduleRecord>(entity =>
        {
            entity.ToTable("Schedule");
            entity.Property(item => item.CourseCode).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Location).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Instructor).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Mode).HasMaxLength(30).IsRequired();
            entity.Property(item => item.ColorCode).HasMaxLength(20).IsRequired();
            entity.Property(item => item.MeetingLink).HasMaxLength(500);
            entity.Property(item => item.Cfn).HasMaxLength(40);
            entity.Property(item => item.Section).HasMaxLength(80);
            entity.Property(item => item.Days).HasMaxLength(40);
            entity.Property(item => item.MeetingTimes).HasMaxLength(300);
            entity.Property(item => item.Notes).HasMaxLength(4000);
            entity.Property(item => item.AttendanceStatus).HasMaxLength(20);
            // Units are credit units such as 3.00; a fixed decimal avoids float rounding.
            entity.Property(item => item.Units).HasPrecision(5, 2);
            entity.HasIndex(item => item.StartsAt);
            entity.HasIndex(item => new { item.ProfileId, item.StartsAt });
            entity.HasOne<UserProfileRecord>()
                .WithMany()
                .HasForeignKey(item => item.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
            // Catalog links are optional and never cascade: archiving a class or term must
            // leave the student's own schedule row (and its notes) intact.
            entity.HasOne<AcademicTermRecord>().WithMany().HasForeignKey(item => item.TermId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ClassOfferingRecord>().WithMany().HasForeignKey(item => item.ClassOfferingId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(item => item.ClassOfferingId);
        });

        modelBuilder.Entity<CourseNoteRecord>(entity =>
        {
            // Each Courses row represents a single class occurrence on one date.
            entity.ToTable("Courses");
            entity.Property(note => note.CreatedAtText).HasMaxLength(80).IsRequired();
            entity.Property(note => note.ClassTimeText).HasMaxLength(80).IsRequired();
            // Long free text maps to nvarchar(max): SQL Server caps nvarchar(n) at 4000.
            entity.Property(note => note.Text).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(note => note.News).HasColumnType("nvarchar(max)");
            entity.Property(note => note.AttendanceStatus).HasMaxLength(20);
            entity.Property(note => note.ImageContentType).HasMaxLength(100);
            entity.Property(note => note.CourseCode).HasMaxLength(40).IsRequired();
            entity.Property(note => note.CourseTitle).HasMaxLength(180).IsRequired();
            entity.HasIndex(note => new { note.ProfileId, note.ScheduleRecordId, note.ClassDate }).IsUnique();
            // SQL Server allows only one cascade path to a table, so this foreign key is
            // NO ACTION while the schedule foreign key cascades. Deleting a class still
            // deletes its notes, and deleting a profile reaches them through Schedule rows.
            entity.HasOne<UserProfileRecord>().WithMany().HasForeignKey(note => note.ProfileId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ScheduleRecord>().WithMany().HasForeignKey(note => note.ScheduleRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StudentIncentiveRecord>(entity =>
        {
            entity.ToTable("StudentIncentives");
            entity.Property(item => item.CourseCode).HasMaxLength(40).IsRequired();
            entity.Property(item => item.CourseTitle).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Reason).HasMaxLength(300).IsRequired();
            entity.HasIndex(item => new { item.ProfileId, item.ScheduleRecordId, item.CreatedAt });
            // Same SQL Server restriction as Courses: only one cascade path may reach this
            // table, so the profile key is NO ACTION and the schedule key cascades.
            entity.HasOne<UserProfileRecord>().WithMany().HasForeignKey(item => item.ProfileId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ScheduleRecord>().WithMany().HasForeignKey(item => item.ScheduleRecordId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // --- Admin-owned reference data ------------------------------------------------
        // Nothing here belongs to a student, so rows are archived instead of deleted and
        // the student schedule keeps working regardless of catalog changes.

        modelBuilder.Entity<AcademicTermRecord>(entity =>
        {
            entity.ToTable("AcademicTerms");
            entity.Property(term => term.SchoolYear).HasMaxLength(20).IsRequired();
            entity.Property(term => term.Semester).HasMaxLength(20).IsRequired();
            // "2026-2027" + "1st" can exist only once.
            entity.HasIndex(term => new { term.SchoolYear, term.Semester }).IsUnique();
            // SQL Server allows a single current term through this filtered unique index.
            entity.HasIndex(term => term.IsCurrent).IsUnique().HasFilter("[IsCurrent] = 1");
        });

        modelBuilder.Entity<SubjectRecord>(entity =>
        {
            entity.ToTable("Subjects");
            entity.Property(subject => subject.Code).HasMaxLength(40).IsRequired();
            entity.Property(subject => subject.Title).HasMaxLength(180).IsRequired();
            entity.Property(subject => subject.Units).HasPrecision(5, 2);
            entity.Property(subject => subject.College).HasMaxLength(120);
            // Codes are stable identifiers, so they stay reserved even after archiving.
            entity.HasIndex(subject => subject.Code).IsUnique();
        });

        modelBuilder.Entity<ClassOfferingRecord>(entity =>
        {
            entity.ToTable("ClassOfferings");
            entity.Property(offering => offering.SectionCode).HasMaxLength(40).IsRequired();
            entity.Property(offering => offering.Instructor).HasMaxLength(120).IsRequired();
            entity.Property(offering => offering.Room).HasMaxLength(120).IsRequired();
            entity.Property(offering => offering.Days).HasMaxLength(40).IsRequired();
            entity.Property(offering => offering.Status).HasMaxLength(20).IsRequired();
            // One section of a subject per term.
            entity.HasIndex(offering => new { offering.TermId, offering.SubjectId, offering.SectionCode })
                .IsUnique();
            entity.HasIndex(offering => new { offering.TermId, offering.Days });
            // Reference rows are never hard-deleted, so block cascades instead of propagating them.
            entity.HasOne<AcademicTermRecord>().WithMany().HasForeignKey(offering => offering.TermId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<SubjectRecord>().WithMany().HasForeignKey(offering => offering.SubjectId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CalendarFeedRecord>(entity =>
        {
            entity.ToTable("CalendarFeeds");
            entity.Property(feed => feed.Name).HasMaxLength(120).IsRequired();
            entity.Property(feed => feed.Url).HasMaxLength(500).IsRequired();
            entity.Property(feed => feed.LastSyncMessage).HasMaxLength(300);
        });

        modelBuilder.Entity<CalendarEventRecord>(entity =>
        {
            entity.ToTable("CalendarEvents");
            entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Kind).HasMaxLength(30).IsRequired();
            entity.HasIndex(item => new { item.StartDate, item.EndDate });
            entity.HasIndex(item => new { item.FeedId, item.StartDate });
            // Deleting a feed leaves its imported entries in place; they are archived instead.
            entity.HasOne<CalendarFeedRecord>().WithMany().HasForeignKey(item => item.FeedId)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
