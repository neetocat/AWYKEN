using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

[DbContext(typeof(AwykenDbContext))]
[Migration("20261101010000_AddStudentIncentives")]
public partial class AddStudentIncentives : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StudentIncentives",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                ScheduleRecordId = table.Column<int>(type: "INTEGER", nullable: false),
                CourseCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                CourseTitle = table.Column<string>(type: "TEXT", maxLength: 180, nullable: false),
                Points = table.Column<int>(type: "INTEGER", nullable: false),
                Reason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudentIncentives", x => x.Id);
                table.ForeignKey("FK_StudentIncentives_Profiles_ProfileId", x => x.ProfileId,
                    "Profiles", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_StudentIncentives_Schedule_ScheduleRecordId", x => x.ScheduleRecordId,
                    "Schedule", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_StudentIncentives_ProfileId_ScheduleRecordId_CreatedAt",
            "StudentIncentives", new[] { "ProfileId", "ScheduleRecordId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_StudentIncentives_ScheduleRecordId",
            "StudentIncentives", "ScheduleRecordId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "StudentIncentives");
}
