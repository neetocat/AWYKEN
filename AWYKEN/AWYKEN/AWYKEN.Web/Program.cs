using AWYKEN.Shared.Services;
using AWYKEN.Shared.Data;
using AWYKEN.Web.Components;
using AWYKEN.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add device-specific services used by the AWYKEN.Shared project
builder.Services.AddSingleton<IFormFactor, FormFactor>();
builder.Services.AddScoped<AWYKEN.Shared.Services.DashboardState>();

// Keep the Web host's local database in the user's app-data folder, outside Git.
var databaseFolder = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AWYKEN");
Directory.CreateDirectory(databaseFolder);
var databasePath = Path.Combine(databaseFolder, "awyken.db");

builder.Services.AddDbContextFactory<AwykenDbContext>(options =>
    options.UseSqlite($"Data Source={databasePath}"));
builder.Services.AddScoped<IAwykenDataService, SqliteAwykenDataService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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
