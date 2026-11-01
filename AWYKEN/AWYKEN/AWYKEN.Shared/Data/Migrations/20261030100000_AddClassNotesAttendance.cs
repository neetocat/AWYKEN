using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

/// <summary>Adds editable notes and attendance status to each saved class.</summary>
[DbContext(typeof(AwykenDbContext))]
[Migration("20261030100000_AddClassNotesAttendance")]
public partial class AddClassNotesAttendance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Notes", table: "Schedule", type: "TEXT", maxLength: 4000, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "AttendanceStatus", table: "Schedule", type: "TEXT", maxLength: 20, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Notes", table: "Schedule");
        migrationBuilder.DropColumn(name: "AttendanceStatus", table: "Schedule");
    }
}
