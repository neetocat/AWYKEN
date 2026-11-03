using AWYKEN.Shared.Configuration;
using AWYKEN.Shared.Services;
using AWYKEN.Shared.Data;
using AWYKEN.Web.Components;
using AWYKEN.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

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
builder.Services.AddSingleton<IUmakCalendarService, UmakCalendarService>();

// Azure SQL Database replaces the old local file database. Keys come from appsettings,
// user secrets, environment variables, or the repository's .env file (ignored by Git).
var databaseSettings = AwykenDatabaseSettings.Load(key => builder.Configuration[key]);
builder.Services.AddAwykenData(databaseSettings);

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

app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(
        typeof(AWYKEN.Shared._Imports).Assembly);

app.Run();
