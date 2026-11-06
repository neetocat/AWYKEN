using AWYKEN.Services;
using AWYKEN.Shared.Configuration;
using AWYKEN.Shared.Data;
using AWYKEN.Shared.Services;
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
            // Identify AWYKEN when calling public OpenStreetMap services, as their policies require.
            builder.Services.AddSingleton(_ =>
            {
                var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AWYKEN/1.0 (+https://github.com/neetocat/AWYKEN)");
                return client;
            });
            builder.Services.AddSingleton<ICommuteMapService, OpenStreetMapCommuteService>();
            builder.Services.AddSingleton<ICurrentWeatherService, OpenMeteoWeatherService>();
            builder.Services.AddSingleton<IUmakCalendarService, AWYKEN.Shared.Data.DatabaseCalendarService>();

            // Azure SQL Database replaces the old on-device SQLite file. The Azure keys live
            // in a .env file that Git ignores, found in this order: the repository root (or
            // any parent of the build output), this app's private app-data folder, then a
            // .env packaged as a raw asset for device testing.
            var databaseSettings = AwykenDatabaseSettings.Load(
                hostConfiguration: null,
                extraValues: LoadPackagedDotEnv(),
                additionalRoots: FileSystem.AppDataDirectory);
            builder.Services.AddAwykenData(databaseSettings);

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }

        /// <summary>
        /// Reads a <c>.env</c> shipped as a MAUI raw asset (<c>Resources/Raw/.env</c>) so a
        /// developer can run on an emulator or phone without shell access to the repository.
        /// A <c>.env</c> in the app-data folder takes precedence over the packaged copy.
        /// </summary>
        private static IReadOnlyDictionary<string, string> LoadPackagedDotEnv()
        {
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync(DotEnv.FileName).GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                return DotEnv.Parse(reader.ReadToEnd());
            }
            catch (Exception exception) when (exception is FileNotFoundException
                or DirectoryNotFoundException or IOException or NotSupportedException or ArgumentException)
            {
                // No packaged .env is the normal case; the repository or app-data file wins.
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
