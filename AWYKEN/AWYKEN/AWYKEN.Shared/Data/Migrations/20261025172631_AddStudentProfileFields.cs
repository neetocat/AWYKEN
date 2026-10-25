using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcademicTerm",
                table: "Profiles",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "College",
                table: "Profiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FullName",
                table: "Profiles",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Program",
                table: "Profiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "Profiles",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StudentNumber",
                table: "Profiles",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "YearLevel",
                table: "Profiles",
                type: "TEXT",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcademicTerm",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "College",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "FullName",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Program",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "StudentNumber",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "YearLevel",
                table: "Profiles");
        }
    }
}
