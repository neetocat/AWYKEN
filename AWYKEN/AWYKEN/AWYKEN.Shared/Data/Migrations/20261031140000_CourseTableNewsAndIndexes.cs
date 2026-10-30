using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

/// <summary>Names the dated course occurrence table Courses and adds per-day news.</summary>
[DbContext(typeof(AwykenDbContext))]
[Migration("20261031140000_CourseTableNewsAndIndexes")]
public partial class CourseTableNewsAndIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameTable(name: "CourseNotes", newName: "Courses");
        migrationBuilder.DropIndex(name: "IX_CourseNotes_ScheduleRecordId", table: "Courses");
        migrationBuilder.DropIndex(name: "IX_CourseNotes_ProfileId_ScheduleRecordId_ClassDate", table: "Courses");
        migrationBuilder.CreateIndex(name: "IX_Courses_ScheduleRecordId", table: "Courses", column: "ScheduleRecordId");
        migrationBuilder.CreateIndex(name: "IX_Courses_ProfileId_ScheduleRecordId_ClassDate", table: "Courses",
            columns: new[] { "ProfileId", "ScheduleRecordId", "ClassDate" }, unique: true);
        migrationBuilder.AddColumn<string>(name: "News", table: "Courses", type: "TEXT", maxLength: 10000, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "News", table: "Courses");
        migrationBuilder.DropIndex(name: "IX_Courses_ScheduleRecordId", table: "Courses");
        migrationBuilder.DropIndex(name: "IX_Courses_ProfileId_ScheduleRecordId_ClassDate", table: "Courses");
        migrationBuilder.RenameTable(name: "Courses", newName: "CourseNotes");
        migrationBuilder.CreateIndex(name: "IX_CourseNotes_ScheduleRecordId", table: "CourseNotes", column: "ScheduleRecordId");
        migrationBuilder.CreateIndex(name: "IX_CourseNotes_ProfileId_ScheduleRecordId_ClassDate", table: "CourseNotes",
            columns: new[] { "ProfileId", "ScheduleRecordId", "ClassDate" }, unique: true);
    }
}
