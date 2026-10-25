using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLocalAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing schedule rows belonged to the one local Guest profile. Create
            // that profile before assigning the rows so this update preserves them.
            migrationBuilder.Sql("INSERT INTO Profiles (DisplayName, Email) SELECT NULL, NULL WHERE NOT EXISTS (SELECT 1 FROM Profiles WHERE Email IS NULL);");

            migrationBuilder.AddColumn<int>(
                name: "ProfileId",
                table: "Schedule",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Schedule SET ProfileId = (SELECT Id FROM Profiles WHERE Email IS NULL LIMIT 1);");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Profiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true,
                collation: "NOCASE",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordHash",
                table: "Profiles",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Schedule_ProfileId_StartsAt",
                table: "Schedule",
                columns: new[] { "ProfileId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_Email",
                table: "Profiles",
                column: "Email",
                unique: true,
                filter: "\"Email\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Schedule_Profiles_ProfileId",
                table: "Schedule",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Schedule_Profiles_ProfileId",
                table: "Schedule");

            migrationBuilder.DropIndex(
                name: "IX_Schedule_ProfileId_StartsAt",
                table: "Schedule");

            migrationBuilder.DropIndex(
                name: "IX_Profiles_Email",
                table: "Profiles");

            migrationBuilder.DropColumn(
                name: "ProfileId",
                table: "Schedule");

            migrationBuilder.DropColumn(
                name: "PasswordHash",
                table: "Profiles");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Profiles",
                type: "TEXT",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldMaxLength: 200,
                oldNullable: true,
                oldCollation: "NOCASE");
        }
    }
}
