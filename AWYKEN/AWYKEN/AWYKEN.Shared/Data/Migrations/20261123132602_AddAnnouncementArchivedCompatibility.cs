using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

public sealed partial class AddAnnouncementArchivedCompatibility : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[Announcements]', N'U') IS NULL
                THROW 51000, 'The Announcements table is missing.', 1;

            IF COL_LENGTH(N'dbo.Announcements', N'IsArchived') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Announcements]
                    ADD [IsArchived] bit NOT NULL
                        CONSTRAINT [DF_Announcements_IsArchived_20261123132602] DEFAULT (0);
            END
            ELSE
            BEGIN
                UPDATE [dbo].[Announcements]
                    SET [IsArchived] = 0
                    WHERE [IsArchived] IS NULL;

                IF EXISTS
                (
                    SELECT 1 FROM sys.columns
                    WHERE [object_id] = OBJECT_ID(N'[dbo].[Announcements]')
                      AND [name] = N'IsArchived' AND [is_nullable] = 1
                )
                    ALTER TABLE [dbo].[Announcements]
                        ALTER COLUMN [IsArchived] bit NOT NULL;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.default_constraints dc
                    INNER JOIN sys.columns c
                        ON c.[object_id] = dc.[parent_object_id]
                       AND c.[column_id] = dc.[parent_column_id]
                    WHERE dc.[parent_object_id] = OBJECT_ID(N'[dbo].[Announcements]')
                      AND c.[name] = N'IsArchived'
                )
                    ALTER TABLE [dbo].[Announcements]
                        ADD CONSTRAINT [DF_Announcements_IsArchived_20261123132602]
                        DEFAULT (0) FOR [IsArchived];
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Keep this compatibility column for databases shared with older app versions.
    }
}