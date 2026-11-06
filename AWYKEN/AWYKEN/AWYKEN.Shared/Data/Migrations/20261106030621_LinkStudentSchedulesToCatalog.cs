using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkStudentSchedulesToCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClassOfferingId",
                table: "Schedule",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TermId",
                table: "Schedule",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Schedule",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schedule_ClassOfferingId",
                table: "Schedule",
                column: "ClassOfferingId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedule_TermId",
                table: "Schedule",
                column: "TermId");

            migrationBuilder.AddForeignKey(
                name: "FK_Schedule_AcademicTerms_TermId",
                table: "Schedule",
                column: "TermId",
                principalTable: "AcademicTerms",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Schedule_ClassOfferings_ClassOfferingId",
                table: "Schedule",
                column: "ClassOfferingId",
                principalTable: "ClassOfferings",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Schedule_AcademicTerms_TermId",
                table: "Schedule");

            migrationBuilder.DropForeignKey(
                name: "FK_Schedule_ClassOfferings_ClassOfferingId",
                table: "Schedule");

            migrationBuilder.DropIndex(
                name: "IX_Schedule_ClassOfferingId",
                table: "Schedule");

            migrationBuilder.DropIndex(
                name: "IX_Schedule_TermId",
                table: "Schedule");

            migrationBuilder.DropColumn(
                name: "ClassOfferingId",
                table: "Schedule");

            migrationBuilder.DropColumn(
                name: "TermId",
                table: "Schedule");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Schedule");
        }
    }
}
