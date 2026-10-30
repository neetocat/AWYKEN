using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

[DbContext(typeof(AwykenDbContext))]
[Migration("20261031100000_AddCourseNoteClassTime")]
public partial class AddCourseNoteClassTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "ClassTimeText", table: "CourseNotes",
            type: "TEXT", maxLength: 80, nullable: false, defaultValue: "");
        migrationBuilder.Sql("""
            UPDATE "CourseNotes"
            SET "ClassTimeText" = COALESCE(
                (SELECT strftime('%H:%M', "StartsAt") || ' – ' || strftime('%H:%M', "EndsAt")
                 FROM "Schedule" WHERE "Schedule"."Id" = "CourseNotes"."ScheduleRecordId"), '');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "ClassTimeText", table: "CourseNotes");
}
