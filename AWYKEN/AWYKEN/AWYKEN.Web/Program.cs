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
builder.Services.AddScoped<AWYKEN.Shared.Services.CorScheduleImporter>();

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
