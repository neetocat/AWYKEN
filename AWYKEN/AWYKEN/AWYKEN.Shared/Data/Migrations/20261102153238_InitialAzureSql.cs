using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialAzureSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Profiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PasswordHash = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    StudentEmail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StudentNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    College = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Program = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    YearLevel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Section = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    AcademicTerm = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    HomeAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    HomeLatitude = table.Column<double>(type: "float", nullable: true),
                    HomeLongitude = table.Column<double>(type: "float", nullable: true),
                    SchoolAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SchoolLatitude = table.Column<double>(type: "float", nullable: true),
                    SchoolLongitude = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Schedule",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CourseCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Instructor = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Mode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ColorCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MeetingLink = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Cfn = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Section = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Units = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    Days = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    MeetingTimes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AttendanceStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedule", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedule_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProfileId = table.Column<int>(type: "int", nullable: false),
                    ScheduleRecordId = table.Column<int>(type: "int", nullable: false),
                    ClassDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClassTimeText = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CreatedAtText = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    News = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AttendanceStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ImageData = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ImageContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CourseCode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CourseTitle = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Courses_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Courses_Schedule_ScheduleRecordId",
                        column: x => x.ScheduleRecordId,
                        principalTable: "Schedule",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Courses_ProfileId_ScheduleRecordId_ClassDate",
                table: "Courses",
                columns: new[] { "ProfileId", "ScheduleRecordId", "ClassDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_ScheduleRecordId",
                table: "Courses",
                column: "ScheduleRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_Email",
                table: "Profiles",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Schedule_ProfileId_StartsAt",
                table: "Schedule",
                columns: new[] { "ProfileId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Schedule_StartsAt",
                table: "Schedule",
                column: "StartsAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropTable(
                name: "Schedule");

            migrationBuilder.DropTable(
                name: "Profiles");
        }
    }
}
