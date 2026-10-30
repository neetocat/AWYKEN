using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

/// <summary>Stores notes, optional image attachments, timestamps, and attendance per class date.</summary>
[DbContext(typeof(AwykenDbContext))]
[Migration("20261030120000_AddCourseNotes")]
public partial class AddCourseNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CourseNotes",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER").Annotation("Sqlite:Autoincrement", true),
                ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                ScheduleRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                ClassDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedAtText = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Text = table.Column<string>(type: "TEXT", maxLength: 20000, nullable: false),
                AttendanceStatus = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                ImageData = table.Column<byte[]>(type: "BLOB", nullable: true),
                ImageContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                CourseCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                CourseTitle = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CourseNotes", x => x.Id);
                table.ForeignKey("FK_CourseNotes_Profiles_ProfileId", x => x.ProfileId,
                    "Profiles", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_CourseNotes_Schedule_ScheduleRecordId", x => x.ScheduleRecordId,
                    "Schedule", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_CourseNotes_ProfileId_ScheduleRecordId_ClassDate",
            "CourseNotes", new[] { "ProfileId", "ScheduleRecordId", "ClassDate" }, unique: true);

        // Preserve any notes saved by the earlier class editor, assigning them to the class start date.
        migrationBuilder.Sql("""
            INSERT INTO "CourseNotes" ("ProfileId", "ScheduleRecordId", "ClassDate", "CreatedAtText", "Text", "AttendanceStatus", "CourseCode", "CourseTitle")
            SELECT "ProfileId", "Id", datetime("StartsAt"), strftime('%Y-%m-%d %H:%M', "StartsAt"), COALESCE("Notes", ''), "AttendanceStatus", "CourseCode", "Title"
            FROM "Schedule"
            WHERE COALESCE("Notes", '') <> '' OR COALESCE("AttendanceStatus", '') <> '';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "CourseNotes");
}
