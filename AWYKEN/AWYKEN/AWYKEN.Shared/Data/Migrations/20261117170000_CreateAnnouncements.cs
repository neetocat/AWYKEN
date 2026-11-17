using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AWYKEN.Shared.Data.Migrations;

public sealed partial class CreateAnnouncements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Some deployments already have this table but do not have this migration
        // recorded in __EFMigrationsHistory. Keep its existing data and finish the
        // migration bookkeeping instead of failing on CREATE TABLE.
        migrationBuilder.Sql("""
            IF OBJECT_ID(N'[dbo].[Announcements]', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[Announcements]
                (
                    [Id] int IDENTITY(1,1) NOT NULL,
                    [Title] nvarchar(160) NOT NULL,
                    [Body] nvarchar(max) NOT NULL,
                    [IsPublished] bit NOT NULL,
                    [CreatedAtUtc] datetime2 NOT NULL,
                    [UpdatedAtUtc] datetime2 NOT NULL,
                    [PublishedAtUtc] datetime2 NULL,
                    CONSTRAINT [PK_Announcements] PRIMARY KEY ([Id])
                );
            END;

            IF OBJECT_ID(N'[dbo].[Announcements]', N'U') IS NOT NULL
                AND COL_LENGTH(N'dbo.Announcements', N'PublishedAtUtc') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Announcements]
                    ADD [PublishedAtUtc] datetime2 NULL;
            END;

            IF OBJECT_ID(N'[dbo].[Announcements]', N'U') IS NOT NULL
                AND NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID(N'[dbo].[Announcements]')
                      AND [name] = N'IX_Announcements_IsPublished_PublishedAtUtc'
                )
            BEGIN
                CREATE INDEX [IX_Announcements_IsPublished_PublishedAtUtc]
                    ON [dbo].[Announcements] ([IsPublished], [PublishedAtUtc]);
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Announcements");
    }
}
