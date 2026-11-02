using AWYKEN.Shared.Configuration;
using AWYKEN.Shared.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AWYKEN.Web.Data;

/// <summary>
/// Gives <c>dotnet ef</c> a DbContext while it scaffolds migrations. The provider must
/// match production (SQL Server), so it always builds a SQL Server context: it uses the
/// real connection from <c>.env</c> when present, otherwise a throwaway LocalDB string.
/// <c>dotnet ef migrations add</c> never opens a connection, so the fallback is unused
/// by the migration generator itself.
/// </summary>
public sealed class AwykenDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<AwykenDbContext>
{
    // Only parsed by dotnet-ef; never used to read or write real data.
    private const string PlaceholderConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=AWYKEN-DesignTime;Trusted_Connection=True;TrustServerCertificate=True";

    public AwykenDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AwykenDbContext>()
            .UseSqlServer(ResolveConnectionString())
            .Options;

        return new AwykenDbContext(options);
    }

    private static string ResolveConnectionString()
    {
        try
        {
            return AwykenDatabaseSettings.Load().ConnectionString;
        }
        catch (AwykenDatabaseConfigurationException)
        {
            // Makes "dotnet ef migrations add" work on a fresh clone with no .env yet.
            return PlaceholderConnectionString;
        }
    }
}
