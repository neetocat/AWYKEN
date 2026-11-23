using AWYKEN.Services;
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

            // The mobile app has no database connection or SQL credentials. All reads and
            // writes go to the server over HTTPS; only this public endpoint URL ships in the APK.
            var apiBaseUrl = LoadApiBaseUrl();
            var apiHttpClient = new HttpClient { BaseAddress = new Uri(apiBaseUrl, UriKind.Absolute) };
            apiHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AWYKEN-Mobile/1.0");
            builder.Services.AddSingleton(new MauiApiHttpClient(apiHttpClient));
            builder.Services.AddSingleton<IApiTokenStore, MauiApiTokenStore>();
            builder.Services.AddScoped<IAwykenDataService>(services => ApiAwykenDataService.Create(
                services.GetRequiredService<MauiApiHttpClient>().Client,
                services.GetRequiredService<IApiTokenStore>(),
                services.GetRequiredService<AWYKEN.Shared.Services.LocalAccountSession>(),
                services.GetRequiredService<AWYKEN.Shared.Services.DashboardState>()));
            builder.Services.AddScoped<IUmakCalendarService>(services => new ApiUmakCalendarService(
                services.GetRequiredService<MauiApiHttpClient>().Client));

            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
            builder.Logging.SetMinimumLevel(LogLevel.Information);
#endif

            return builder.Build();
        }

        private static string LoadApiBaseUrl()
        {
            var value = Environment.GetEnvironmentVariable("AWYKEN_API_BASE_URL");
            if (!string.IsNullOrWhiteSpace(value))
                return ValidateApiBaseUrl(value);

            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("api-base-url.txt")
                    .GetAwaiter().GetResult();
                using var reader = new StreamReader(stream);
                return ValidateApiBaseUrl(reader.ReadToEnd().Trim());
            }
            catch (Exception)
            {
                throw new InvalidOperationException(
                    "AWYKEN_API_BASE_URL is not configured. Set it to the deployed AWYKEN.Web HTTPS address in Resources/Raw/api-base-url.txt.");
            }
        }

        private static string ValidateApiBaseUrl(string value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || uri.Host.Contains("YOUR-AWYKEN-WEB-APP", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Set Resources/Raw/api-base-url.txt to the deployed AWYKEN.Web HTTPS address before building the mobile app.");
            return uri.AbsoluteUri.EndsWith('/') ? uri.AbsoluteUri : uri.AbsoluteUri + "/";
        }

    }
}
