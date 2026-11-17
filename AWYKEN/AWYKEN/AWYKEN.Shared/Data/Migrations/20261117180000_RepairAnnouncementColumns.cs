using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

public sealed partial class RepairAnnouncementColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[Announcements]', N'U') IS NULL
                THROW 51000, 'The Announcements table is missing.', 1;

            IF COL_LENGTH(N'dbo.Announcements', N'Id') IS NULL
                ALTER TABLE [dbo].[Announcements]
                    ADD [Id] int IDENTITY(1,1) NOT NULL;

            IF COL_LENGTH(N'dbo.Announcements', N'Title') IS NULL
                ALTER TABLE [dbo].[Announcements]
                    ADD [Title] nvarchar(160) NOT NULL
                        CONSTRAINT [DF_Announcements_Title_20261117180000] DEFAULT (N'');

            IF COL_LENGTH(N'dbo.Announcements', N'Body') IS NULL
                ALTER TABLE [dbo].[Announcements]
                    ADD [Body] nvarchar(max) NOT NULL
                        CONSTRAINT [DF_Announcements_Body_20261117180000] DEFAULT (N'');

            IF COL_LENGTH(N'dbo.Announcements', N'IsPublished') IS NULL
                ALTER TABLE [dbo].[Announcements]
                    ADD [IsPublished] bit NOT NULL
                        CONSTRAINT [DF_Announcements_IsPublished_20261117180000] DEFAULT (0);

            IF COL_LENGTH(N'dbo.Announcements', N'CreatedAtUtc') IS NULL
                ALTER TABLE [dbo].[Announcements]
                    ADD [CreatedAtUtc] datetime2 NOT NULL
                        CONSTRAINT [DF_Announcements_CreatedAtUtc_20261117180000] DEFAULT (SYSUTCDATETIME());

            IF COL_LENGTH(N'dbo.Announcements', N'UpdatedAtUtc') IS NULL
                ALTER TABLE [dbo].[Announcements]
                    ADD [UpdatedAtUtc] datetime2 NOT NULL
                        CONSTRAINT [DF_Announcements_UpdatedAtUtc_20261117180000] DEFAULT (SYSUTCDATETIME());

            IF COL_LENGTH(N'dbo.Announcements', N'PublishedAtUtc') IS NULL
                ALTER TABLE [dbo].[Announcements]
                    ADD [PublishedAtUtc] datetime2 NULL;

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'[dbo].[Announcements]')
                  AND [name] = N'IX_Announcements_IsPublished_PublishedAtUtc'
            )
                CREATE INDEX [IX_Announcements_IsPublished_PublishedAtUtc]
                    ON [dbo].[Announcements] ([IsPublished], [PublishedAtUtc]);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Columns are added conditionally because this table can predate the migration.
        // Keep them on downgrade so a rollback cannot discard announcements or their data.
    }
}
