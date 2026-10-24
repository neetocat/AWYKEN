using AWYKEN.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AWYKEN.Web.Data;

/// <summary>
/// Provides a local SQLite configuration to dotnet-ef while it generates migrations.
/// It never uses or requires an Azure SQL connection string.
/// </summary>
public sealed class AwykenDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AwykenDbContext>
{
    public AwykenDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AwykenDbContext>()
            .UseSqlite("Data Source=awyken-design-time.db")
            .Options;

        return new AwykenDbContext(options);
    }
}
