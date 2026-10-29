using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Data;
using KotoNeko.Data.Dictionary;
using KotoNeko.Jisho;
using KotoNeko.Web.Components;
using KotoNeko.Web.Services;
using KotoNeko.Web.Services.Llm;
using KotoNeko.Core.Llm;
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
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<DictationService>();
builder.Services.AddScoped<DictionaryService>();

// Dictionary imports run in the background, outside any page; the only network
// use is downloading the dictionary release from GitHub.
builder.Services.AddHttpClient(DictionaryImportService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(10);
    client.DefaultRequestHeaders.Add("User-Agent", "KotoNeko/1.0 (personal study app)");
});
builder.Services.AddSingleton<DictionaryImportService>();

// The known-words pool; seeding downloads the JLPT word lists from GitHub.
builder.Services.AddHttpClient(KnownWordsService.HttpClientName, client =>
{
    client.Timeout = TimeSpan.FromMinutes(1);
    client.DefaultRequestHeaders.Add("User-Agent", "KotoNeko/1.0 (personal study app)");
});
builder.Services.AddScoped<KnownWordsService>();
builder.Services.AddScoped<PracticeSentenceService>();
builder.Services.AddScoped<GrammarService>();

// LLM providers. Keys come from appsettings.Development.json (git-ignored) or the
// environment, never tracked files; which provider and model each job uses is in
// appsettings.json (Llm:Jobs). Every call is logged in LlmCalls.
// Local models can be slow (a minute or more per answer), so the timeout is a setting.
builder.Services.AddHttpClient("llm", client =>
{
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("Llm:TimeoutSeconds", 300));
    client.DefaultRequestHeaders.Add("User-Agent", "KotoNeko/1.0 (personal study app)");
});
builder.Services.AddSingleton<ILlmProvider>(_ => new ClaudeProvider(
    builder.Configuration["Llm:Claude:ApiKey"] is { Length: > 0 } claudeKey ? claudeKey : builder.Configuration["ANTHROPIC_API_KEY"],
    builder.Configuration["Llm:Claude:Effort"]));
builder.Services.AddSingleton<ILlmProvider>(sp => new GeminiProvider(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm"),
    builder.Configuration["Llm:Gemini:ApiKey"] is { Length: > 0 } geminiKey ? geminiKey : builder.Configuration["GEMINI_API_KEY"]));
builder.Services.AddSingleton<ILlmProvider>(sp => new LmStudioProvider(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient("llm"),
    builder.Configuration["Llm:LmStudio:BaseUrl"]));
builder.Services.AddSingleton<LlmService>();
builder.Services.AddScoped<SentenceGenerationService>();

// Sentence readings (furigana) are generated in the background, never mid-review.
builder.Services.AddSingleton<SentenceReadingService>();
builder.Services.AddHostedService<SentenceReadingStartup>();
builder.Services.AddScoped(sp => new KanaKanjiConverter(sp.GetRequiredService<DictionaryService>()));

// Database backups into the repository's backups folder (Backup:Directory and
// Backup:Mysqldump override where they go and which mysqldump is used).
builder.Services.AddSingleton(sp => new BackupService(
    connectionString,
    builder.Configuration["Backup:Directory"] is { Length: > 0 } backupDirectory
        ? backupDirectory
        : KotoNeko.Data.Backup.BackupFiles.FindRepositoryFolder(builder.Environment.ContentRootPath),
    BackupService.FindMysqldump(builder.Configuration["Backup:Mysqldump"]),
    sp.GetRequiredService<ILogger<BackupService>>()));

string ttsApiKey = builder.Configuration["GoogleTtsApiKey"] ?? string.Empty;
builder.Services.AddSingleton<JapaneseTtsService>(_ => new JapaneseTtsService(ttsApiKey));

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

        // Back up before changing the schema of a database that already has data:
        // a migration that fails part-way can't be rolled back in MySQL. A failed
        // backup is logged loudly but doesn't stop the app from starting.
        if (db.Database.GetPendingMigrations().Any() && db.Database.GetAppliedMigrations().Any())
        {
            BackupService backups = app.Services.GetRequiredService<BackupService>();
            try
            {
                KotoNeko.Data.Backup.BackupFile backup = await backups.BackupNowAsync("before-migration");
                logger.LogInformation("Backed up to {File} before applying migrations.", backup.Path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "BACKUP BEFORE MIGRATION FAILED. Migrating anyway; back up by hand if you can.");
            }
        }

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

// One-off command: import the JMdict dictionary for offline kanji conversion, then exit.
//   KotoNeko.Web --import-jmdict "path/to/jmdict-eng-x.y.z.json"
if (app.Configuration["import-jmdict"] is { Length: > 0 } jmdictPath)
{
    IDbContextFactory<KotoNekoDbContext> factory = app.Services.GetRequiredService<IDbContextFactory<KotoNekoDbContext>>();
    await using KotoNekoDbContext db = await factory.CreateDbContextAsync();
    ImportStage? lastStage = null;
    ImportResult result = await JmdictImporter.ImportAsync(db, jmdictPath, new SyncProgress<ImportProgress>(p =>
    {
        if (p.Stage != lastStage) {
            lastStage = p.Stage;
            Console.WriteLine($"{p.Stage} …");
        }
    }));
    Console.WriteLine($"Imported {result.Entries:N0} entries (JMdict {result.Version}, {result.DictionaryDate}) " +
        $"in {result.Elapsed.TotalSeconds:N0}s, peak memory {result.PeakMemoryMb:N0} MB.");
    return;
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

app.MapGet("/api/vocab/{id:int}/audio/word", async (int id, IDbContextFactory<KotoNekoDbContext> factory) =>
{
    await using KotoNekoDbContext db = await factory.CreateDbContextAsync();
    VocabularyAudio? a = await db.VocabularyAudios.FirstOrDefaultAsync(x => x.VocabularyId == id);
    return a is { WordAudio.Length: > 0 }
        ? Results.File(a.WordAudio, "audio/mpeg")
        : Results.NotFound();
});

app.MapGet("/api/vocab/{id:int}/audio/sentence", async (int id, IDbContextFactory<KotoNekoDbContext> factory) =>
{
    await using KotoNekoDbContext db = await factory.CreateDbContextAsync();
    VocabularyAudio? a = await db.VocabularyAudios.FirstOrDefaultAsync(x => x.VocabularyId == id);
    return a?.SentenceAudio is { Length: > 0 } bytes
        ? Results.File(bytes, "audio/mpeg")
        : Results.NotFound();
});

app.MapGet("/api/practice-sentence/{id:int}/audio", async (int id, PracticeSentenceService sentences) =>
    await sentences.GetAudioAsync(id) is { Length: > 0 } bytes
        ? Results.File(bytes, "audio/mpeg")
        : Results.NotFound());

app.MapGet("/api/conjugation/{id:int}/audio", async (int id, IDbContextFactory<KotoNekoDbContext> factory) =>
{
    await using KotoNekoDbContext db = await factory.CreateDbContextAsync();
    ConjugatedAudio? a = await db.ConjugatedAudios.FirstOrDefaultAsync(x => x.ConjugationId == id);
    return a is { Audio.Length: > 0 }
        ? Results.File(a.Audio, "audio/mpeg")
        : Results.NotFound();
});

// Offline kana → kanji conversion for the JapaneseInput component. Fields that
// test kanji choice (dictation) send learn: false, so past choices neither
// reorder their candidates nor get recorded from them.
app.MapPost("/api/ime/convert", async (ImeRequest request, KanaKanjiConverter converter) =>
    Results.Ok(await converter.ConvertAsync(request.Kana ?? string.Empty, request.Learn ?? true)));

app.MapPost("/api/ime/candidates", async (ImeRequest request, KanaKanjiConverter converter) =>
    Results.Ok(await converter.CandidatesAsync(request.Kana ?? string.Empty, request.Learn ?? true)));

app.MapPost("/api/ime/learn", async (ImeChoicesRequest request, KanaKanjiConverter converter, DictionaryService dictionary) =>
{
    List<(string, string)> choices = new();
    foreach (ImeChoiceDto c in request.Choices ?? new List<ImeChoiceDto>()) {
        if (!string.IsNullOrEmpty(c.Reading) && !string.IsNullOrEmpty(c.Written)) {
            choices.Add(await converter.ResolveChoiceAsync(c.Reading, c.Written));
        }
    }
    await dictionary.SaveChoicesAsync(choices);
    return Results.NoContent();
});

app.Run();

/// <summary>Body of an IME conversion request.</summary>
internal sealed record ImeRequest(string? Kana, bool? Learn);

/// <summary>Spellings picked in one commit, one per converted segment.</summary>
internal sealed record ImeChoicesRequest(List<ImeChoiceDto>? Choices);

internal sealed record ImeChoiceDto(string? Reading, string? Written);

/// <summary>Reports progress on the calling thread, so console lines stay in order.</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
