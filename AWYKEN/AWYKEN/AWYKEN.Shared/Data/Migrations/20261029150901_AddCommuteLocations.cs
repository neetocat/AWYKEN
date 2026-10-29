using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommuteLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HomeAddress",
                table: "Profiles",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HomeLatitude",
                table: "Profiles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HomeLongitude",
                table: "Profiles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchoolAddress",
                table: "Profiles",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SchoolLatitude",
                table: "Profiles",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "SchoolLongitude",
                table: "Profiles",
                type: "REAL",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HomeAddress",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "HomeLatitude",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "HomeLongitude",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "SchoolAddress",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "SchoolLatitude",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "SchoolLongitude",
                table: "Profiles");
        }
    }
}
