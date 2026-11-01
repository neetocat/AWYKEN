using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModelForPendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CourseNotes_ScheduleRecordId",
                table: "CourseNotes",
                column: "ScheduleRecordId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CourseNotes_ScheduleRecordId",
                table: "CourseNotes");
        }
    }
}
