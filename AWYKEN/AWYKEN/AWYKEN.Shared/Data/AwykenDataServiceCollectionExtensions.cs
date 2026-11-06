using AWYKEN.Shared.Configuration;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AWYKEN.Shared.Data;

/// <summary>
/// Registers the Azure SQL Database context and the shared data service so the Web host
/// and the MAUI app configure the cloud database in exactly one place.
/// </summary>
public static class AwykenDataServiceCollectionExtensions
{
    public static IServiceCollection AddAwykenData(
        this IServiceCollection services, AwykenDatabaseSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);

        // One factory per app: each operation gets a short-lived context, which keeps
        // connections pooled and avoids sharing a tracked context across Blazor pages.
        services.AddDbContextFactory<AwykenDbContext>(options =>
            options.UseSqlServer(settings.ConnectionString, sqlServer =>
            {
                sqlServer.CommandTimeout(settings.CommandTimeoutSeconds);
                // Azure SQL drops idle connections and throttles bursts, so retry transient faults.
                sqlServer.EnableRetryOnFailure(
                    maxRetryCount: 4,
                    maxRetryDelay: TimeSpan.FromSeconds(10),
                    errorNumbersToAdd: null);
                sqlServer.MigrationsAssembly(typeof(AwykenDbContext).Assembly.GetName().Name);
            }));

        // The service reports the resolved server/database when a query fails.
        services.AddSingleton(settings);
        services.AddScoped<IAwykenDataService, SqlServerAwykenDataService>();
        services.AddScoped<IAcademicCatalogService, AcademicCatalogService>();
        services.AddScoped<ISchoolCalendarService, SchoolCalendarService>();
        return services;
    }
}
