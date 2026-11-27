using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

[DbContext(typeof(AwykenDbContext))]
[Migration("20261127213200_AddConsentGivenAtUtc")]
public partial class AddConsentGivenAtUtc : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ConsentGivenAtUtc",
            table: "Profiles",
            type: "datetime2",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ConsentGivenAtUtc",
            table: "Profiles");
    }
}
