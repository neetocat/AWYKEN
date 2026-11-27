using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

[DbContext(typeof(AwykenDbContext))]
[Migration("20261127210000_AddScheduleAndCommuteReminders")]
public partial class AddScheduleAndCommuteReminders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ReminderEnabled",
            table: "Schedule",
            type: "bit",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "LeaveReminderEnabled",
            table: "Profiles",
            type: "bit",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReminderEnabled",
            table: "Schedule");

        migrationBuilder.DropColumn(
            name: "LeaveReminderEnabled",
            table: "Profiles");
    }
}
