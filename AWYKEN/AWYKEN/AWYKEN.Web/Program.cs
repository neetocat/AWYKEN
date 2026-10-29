using AWYKEN.Shared.Services;
using AWYKEN.Shared.Data;
using AWYKEN.Web.Components;
using AWYKEN.Web.Services;
using Microsoft.EntityFrameworkCore;

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

// Keep the Web host's local database in the user's app-data folder, outside Git.
var databaseFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AWYKEN");
Directory.CreateDirectory(databaseFolder);
var databasePath = Path.Combine(databaseFolder, "awyken.db");

builder.Services.AddDbContextFactory<AwykenDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<IAwykenDataService, SqliteAwykenDataService>();

var app = builder.Build();

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
