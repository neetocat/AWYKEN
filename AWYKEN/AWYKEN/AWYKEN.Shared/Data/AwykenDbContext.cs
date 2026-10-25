using Microsoft.EntityFrameworkCore;

namespace AWYKEN.Shared.Data;

/// <summary>Defines the SQLite tables used by the shared AWYKEN pages.</summary>
public sealed class AwykenDbContext(DbContextOptions<AwykenDbContext> options)
    : DbContext(options)
{
    public DbSet<UserProfileRecord> Profiles => Set<UserProfileRecord>();
    public DbSet<ScheduleRecord> Schedule => Set<ScheduleRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserProfileRecord>(entity =>
        {
            entity.ToTable("Profiles");
            entity.Property(profile => profile.DisplayName).HasMaxLength(100);
            entity.Property(profile => profile.Email).HasMaxLength(200).UseCollation("NOCASE");
            entity.Property(profile => profile.PasswordHash).HasMaxLength(300);
            entity.HasIndex(profile => profile.Email).IsUnique().HasFilter("\"Email\" IS NOT NULL");
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
            entity.HasIndex(item => item.StartsAt);
            entity.HasIndex(item => new { item.ProfileId, item.StartsAt });
            entity.HasOne<UserProfileRecord>()
                .WithMany()
                .HasForeignKey(item => item.ProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
