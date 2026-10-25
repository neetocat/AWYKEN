using AWYKEN.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

[DbContext(typeof(AwykenDbContext))]
[Migration("20261025120000_AddCorScheduleFields")]
public partial class AddCorScheduleFields : Migration
{
    protected override void BuildTargetModel(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
#pragma warning disable 612, 618
        modelBuilder.HasAnnotation("ProductVersion", "10.0.13");
        modelBuilder.Entity("AWYKEN.Shared.Data.ScheduleRecord", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("Cfn").HasMaxLength(40).HasColumnType("TEXT");
            b.Property<string>("ColorCode").IsRequired().HasMaxLength(20).HasColumnType("TEXT");
            b.Property<string>("CourseCode").IsRequired().HasMaxLength(40).HasColumnType("TEXT");
            b.Property<string>("Days").HasMaxLength(40).HasColumnType("TEXT");
            b.Property<DateTime>("EndsAt").HasColumnType("TEXT");
            b.Property<string>("Instructor").IsRequired().HasMaxLength(120).HasColumnType("TEXT");
            b.Property<string>("Location").IsRequired().HasMaxLength(180).HasColumnType("TEXT");
            b.Property<string>("MeetingLink").HasMaxLength(500).HasColumnType("TEXT");
            b.Property<string>("MeetingTimes").HasMaxLength(300).HasColumnType("TEXT");
            b.Property<string>("Mode").IsRequired().HasMaxLength(30).HasColumnType("TEXT");
            b.Property<string>("Section").HasMaxLength(80).HasColumnType("TEXT");
            b.Property<DateTime>("StartsAt").HasColumnType("TEXT");
            b.Property<string>("Title").IsRequired().HasMaxLength(180).HasColumnType("TEXT");
            b.Property<decimal?>("Units").HasColumnType("TEXT");
            b.HasKey("Id");
            b.HasIndex("StartsAt");
            b.ToTable("Schedule", (string)null);
        });
        modelBuilder.Entity("AWYKEN.Shared.Data.UserProfileRecord", b =>
        {
            b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            b.Property<string>("DisplayName").HasMaxLength(100).HasColumnType("TEXT");
            b.Property<string>("Email").HasMaxLength(200).HasColumnType("TEXT");
            b.HasKey("Id");
            b.ToTable("Profiles", (string)null);
        });
#pragma warning restore 612, 618
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Cfn", table: "Schedule", type: "TEXT", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Section", table: "Schedule", type: "TEXT", maxLength: 80, nullable: true);
        migrationBuilder.AddColumn<decimal>(name: "Units", table: "Schedule", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Days", table: "Schedule", type: "TEXT", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "MeetingTimes", table: "Schedule", type: "TEXT", maxLength: 300, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Cfn", table: "Schedule");
        migrationBuilder.DropColumn(name: "Section", table: "Schedule");
        migrationBuilder.DropColumn(name: "Units", table: "Schedule");
        migrationBuilder.DropColumn(name: "Days", table: "Schedule");
        migrationBuilder.DropColumn(name: "MeetingTimes", table: "Schedule");
    }
}
