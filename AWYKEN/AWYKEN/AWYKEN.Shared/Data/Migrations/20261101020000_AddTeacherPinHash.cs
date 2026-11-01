using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

[DbContext(typeof(AwykenDbContext))]
[Migration("20261101020000_AddTeacherPinHash")]
public partial class AddTeacherPinHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "TeacherPinHash",
            table: "Profiles",
            type: "TEXT",
            maxLength: 300,
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "TeacherPinHash", table: "Profiles");
}
