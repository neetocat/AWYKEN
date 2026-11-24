using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations
{
    public partial class AddCourseReminders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "News",
                table: "Courses",
                newName: "Reminder");

            migrationBuilder.AddColumn<bool>(
                name: "RemindMe",
                table: "Courses",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemindMe",
                table: "Courses");

            migrationBuilder.RenameColumn(
                name: "Reminder",
                table: "Courses",
                newName: "News");
        }
    }
}
