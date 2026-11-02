using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

[DbContext(typeof(AwykenDbContext))]
[Migration("20261102120000_AddCorStudentDetails")]
public partial class AddCorStudentDetails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "StudentAddress", table: "Profiles", type: "TEXT", maxLength: 300, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Guardian", table: "Profiles", type: "TEXT", maxLength: 150, nullable: true);
        migrationBuilder.AddColumn<string>(name: "Gender", table: "Profiles", type: "TEXT", maxLength: 40, nullable: true);
        migrationBuilder.AddColumn<string>(name: "DateOfBirth", table: "Profiles", type: "TEXT", maxLength: 40, nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StudentAddress", table: "Profiles");
        migrationBuilder.DropColumn(name: "Guardian", table: "Profiles");
        migrationBuilder.DropColumn(name: "Gender", table: "Profiles");
        migrationBuilder.DropColumn(name: "DateOfBirth", table: "Profiles");
    }
}
