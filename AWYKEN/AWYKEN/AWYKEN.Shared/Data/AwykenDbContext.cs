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
            entity.Property(profile => profile.FullName).HasMaxLength(150);
            entity.Property(profile => profile.StudentEmail).HasMaxLength(200);
            entity.Property(profile => profile.StudentNumber).HasMaxLength(30);
            entity.Property(profile => profile.College).HasMaxLength(200);
            entity.Property(profile => profile.Program).HasMaxLength(200);
            entity.Property(profile => profile.YearLevel).HasMaxLength(40);
            entity.Property(profile => profile.Section).HasMaxLength(80);
            entity.Property(profile => profile.AcademicTerm).HasMaxLength(80);
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
    }
}
