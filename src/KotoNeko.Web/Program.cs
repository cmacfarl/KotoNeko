using KotoNeko.Data;
using KotoNeko.Jisho;
using KotoNeko.Web.Components;
using KotoNeko.Web.Services;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Connection string: appsettings has dummy values; override via the
// KOTONEKO_CONNECTION environment variable or user-secrets for the real one.
string connectionString =
    builder.Configuration["KOTONEKO_CONNECTION"]
    ?? Environment.GetEnvironmentVariable("KOTONEKO_CONNECTION")
    ?? builder.Configuration.GetConnectionString("KotoNeko")
    ?? throw new InvalidOperationException("No KotoNeko connection string configured.");

builder.Services.AddKotoNekoData(connectionString);

// jisho.org client (verb-class + reading detection).
builder.Services.AddHttpClient<JishoClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("User-Agent", "KotoNeko/1.0 (personal study app)");
});

builder.Services.AddScoped<SourceService>();
builder.Services.AddScoped<VocabularyService>();
builder.Services.AddScoped<ReviewService>();
builder.Services.AddScoped<DashboardService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

WebApplication app = builder.Build();

// Apply migrations on startup. If the database is unreachable (e.g. the
// connection string still has placeholder values) we log a clear hint rather
// than crashing silently.
using (IServiceScope scope = app.Services.CreateScope())
{
    IDbContextFactory<KotoNekoDbContext> factory =
        scope.ServiceProvider.GetRequiredService<IDbContextFactory<KotoNekoDbContext>>();
    ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        using KotoNekoDbContext db = factory.CreateDbContext();
        db.Database.Migrate();
        logger.LogInformation("Database is up to date.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Could not connect to / migrate the database. Set a valid connection string " +
            "in appsettings.json or the KOTONEKO_CONNECTION environment variable.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
