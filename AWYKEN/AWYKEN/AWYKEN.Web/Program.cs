using AWYKEN.Shared.Configuration;
using AWYKEN.Shared.Services;
using AWYKEN.Shared.Data;
using AWYKEN.Web.Components;
using AWYKEN.Web.Services;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRateLimiter(options => options.AddPolicy("auth", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
{
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));

builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<AWYKEN.Shared.Services.DashboardState>();
builder.Services.AddScoped<IPhoneNotificationService, NullPhoneNotificationService>();
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

// Azure SQL Database replaces the old local file database. Keys come from appsettings,
// user secrets, environment variables, or the repository's .env file (ignored by Git).
var databaseSettings = AwykenDatabaseSettings.Load(key => builder.Configuration[key]);
builder.Services.AddAwykenData(databaseSettings);

// API signing keys stay in the server's environment/App Service configuration, never the APK.
var apiTokenSecret = builder.Configuration["AWYKEN_API_TOKEN_SECRET"]
    ?? Environment.GetEnvironmentVariable("AWYKEN_API_TOKEN_SECRET");
byte[] apiTokenKey;
if (string.IsNullOrWhiteSpace(apiTokenSecret))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Set AWYKEN_API_TOKEN_SECRET in the web host configuration.");
    apiTokenKey = RandomNumberGenerator.GetBytes(32);
}
else
{
    apiTokenKey = Encoding.UTF8.GetBytes(apiTokenSecret);
    if (apiTokenKey.Length < 32)
        throw new InvalidOperationException("AWYKEN_API_TOKEN_SECRET must be at least 32 bytes.");
}
builder.Services.AddSingleton(serviceProvider => new ApiAccessTokenService(
    serviceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<AwykenDbContext>>(),
    apiTokenKey));

var app = builder.Build();

// Printed once at startup so a misconfigured .env is obvious in the console.
app.Logger.LogInformation(
    "AWYKEN database: {Database} on {Server} (settings from {Source}).",
    databaseSettings.InitialCatalog,
    databaseSettings.DataSource,
    databaseSettings.DotEnvFile ?? "environment variables / appsettings");

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();
app.UseRateLimiter();

app.MapAwykenApi();

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(AWYKEN.Shared._Imports).Assembly);

app.Run();
