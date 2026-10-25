using AWYKEN.Services;
using AWYKEN.Shared.Data;
using AWYKEN.Shared.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AWYKEN
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            // Add device-specific services used by the AWYKEN.Shared project
            builder.Services.AddSingleton<IFormFactor, FormFactor>();
            builder.Services.AddScoped<AWYKEN.Shared.Services.DashboardState>();
            builder.Services.AddScoped<AWYKEN.Shared.Services.LocalAccountSession>();
            builder.Services.AddScoped<AWYKEN.Shared.Services.CorScheduleImporter>();

            // Store this installation's database in its private platform app-data folder.
            var databasePath = Path.Combine(FileSystem.AppDataDirectory, "awyken.db");
            builder.Services.AddDbContextFactory<AwykenDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddScoped<IAwykenDataService, SqliteAwykenDataService>();

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
