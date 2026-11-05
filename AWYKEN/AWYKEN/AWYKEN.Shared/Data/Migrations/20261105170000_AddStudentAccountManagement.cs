using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

public sealed partial class AddStudentAccountManagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "AccountStatus", table: "Profiles", type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Active");
        migrationBuilder.AddColumn<DateTime>(name: "LastActivityAt", table: "Profiles", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "RegisteredAt", table: "Profiles", type: "datetime2", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_Profiles_AccountStatus", table: "Profiles", column: "AccountStatus");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Profiles_AccountStatus", table: "Profiles");
        migrationBuilder.DropColumn(name: "AccountStatus", table: "Profiles");
        migrationBuilder.DropColumn(name: "LastActivityAt", table: "Profiles");
        migrationBuilder.DropColumn(name: "RegisteredAt", table: "Profiles");
    }
}
