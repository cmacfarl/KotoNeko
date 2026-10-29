using KotoNeko.Core.Domain;
using KotoNeko.Core.Srs;
using KotoNeko.Data;
using KotoNeko.Data.Dictionary;
using KotoNeko.Data.Vocab;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>Which words the Known words page lists.</summary>
public enum KnownWordsStatus
{
    All,
    InPool,
    DoNotUse,
    AsleepUnstudied,
    Studying,
}

/// <summary>A word as the Known words page shows it.</summary>
public sealed record KnownWordRow(
    int Id, string Japanese, string Reading, string Meaning, JlptLevel? Level,
    bool DoNotUse, bool IsAsleep, SrsStage? Stage, bool InPool, string? Source);

/// <summary>How many words count as known, and how many are held back.</summary>
public sealed record KnownWordsSummary(int InPool, int DoNotUse, int Total, IReadOnlyDictionary<JlptLevel, int> ByLevel);

/// <summary>
/// The known-words pool (see <see cref="KnownWords"/>): what's in it, marking words
/// "do not use", waking words to study, "I know this", and seeding the JLPT lists.
/// </summary>
public sealed class KnownWordsService
{
    public const string HttpClientName = "jlpt-lists";

    // One seed at a time, across every page and circuit.
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly IHttpClientFactory _http;
    private readonly DictionaryService _dictionary;
    private readonly JapaneseTtsService _tts;
    private readonly VocabularyService _vocabulary;
    private readonly ILogger<KnownWordsService> _logger;

    public KnownWordsService(
        IDbContextFactory<KotoNekoDbContext> factory, IHttpClientFactory http, DictionaryService dictionary,
        JapaneseTtsService tts, VocabularyService vocabulary, ILogger<KnownWordsService> logger)
    {
        _factory = factory;
        _http = http;
        _dictionary = dictionary;
        _tts = tts;
        _vocabulary = vocabulary;
        _logger = logger;
    }

    public static bool IsSeeding => SeedLock.CurrentCount == 0;

    public async Task<KnownWordsSummary> GetSummaryAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        StudySettings settings = await SettingsService.LoadAsync(db);
        int inPool = await db.Vocabulary.InPool(settings.KnownWordStage).CountAsync();
        int doNotUse = await db.Vocabulary.CountAsync(v => v.DoNotUse);
        int total = await db.Vocabulary.CountAsync();
        Dictionary<JlptLevel, int> byLevel = await db.Vocabulary
            .Where(v => v.JlptLevel != null)
            .GroupBy(v => v.JlptLevel!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        return new KnownWordsSummary(inPool, doNotUse, total, byLevel);
    }

    /// <summary>The words matching the filters, easiest level first. <paramref name="level"/> null means any; <paramref name="noLevel"/> means only words on no list.</summary>
    public async Task<List<KnownWordRow>> ListAsync(JlptLevel? level, bool noLevel, KnownWordsStatus status, string? search)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        SrsStage threshold = (await SettingsService.LoadAsync(db)).KnownWordStage;

        IQueryable<Vocabulary> query = db.Vocabulary.AsNoTracking();
        if (noLevel) {
            query = query.Where(v => v.JlptLevel == null);
        } else if (level is not null) {
            query = query.Where(v => v.JlptLevel == level);
        }
        query = status switch {
            KnownWordsStatus.InPool => query.InPool(threshold),
            KnownWordsStatus.DoNotUse => query.Where(v => v.DoNotUse),
            KnownWordsStatus.AsleepUnstudied => query.Where(v => v.IsAsleep && (v.Srs == null || v.Srs.Stage == SrsStage.Locked)),
            KnownWordsStatus.Studying => query.Where(v => v.Srs != null && v.Srs.Stage != SrsStage.Locked),
            _ => query,
        };
        if (!string.IsNullOrWhiteSpace(search)) {
            string term = search.Trim();
            query = query.Where(v => v.Japanese.Contains(term) || v.Reading.Contains(term) || v.Meanings.Any(m => m.Text.Contains(term)));
        }

        List<Vocabulary> words = await query
            .Include(v => v.Srs)
            .Include(v => v.Meanings)
            .Include(v => v.SourceMaterial)
            .OrderByDescending(v => v.JlptLevel)
            .ThenBy(v => v.Reading)
            .ToListAsync();

        return words.Select(v => new KnownWordRow(
                v.Id, v.Japanese, v.Reading, v.PrimaryMeaning, v.JlptLevel, v.DoNotUse, v.IsAsleep,
                v.Srs?.Stage, KnownWords.IsInPool(v, threshold), v.SourceMaterial?.Name))
            .ToList();
    }

    public async Task SetDoNotUseAsync(IReadOnlyCollection<int> ids, bool doNotUse)
    {
        if (ids.Count == 0) {
            return;
        }
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        foreach (int[] chunk in ids.Chunk(1000)) {
            foreach (Vocabulary v in await db.Vocabulary.Where(v => chunk.Contains(v.Id)).ToListAsync()) {
                v.DoNotUse = doNotUse;
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Wake a word so it enters lessons, generating its word audio (at the default
    /// speed) if it has none. Returns a note when the audio couldn't be made.
    /// </summary>
    public async Task<string?> StudyAsync(int id)
    {
        await _vocabulary.SetAsleepAsync(id, false);

        Vocabulary? word = await _vocabulary.GetAsync(id);
        if (word is null || word.HasWordAudio) {
            return null;
        }
        try {
            await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
            SpeechSpeed speed = (await SettingsService.LoadAsync(db)).SpeechSpeed;
            byte[] audio = (await _tts.SynthesiseAsync(word.Japanese, speed)).Audio;
            await _vocabulary.SaveAudioAsync(id, audio, word.Audio?.SentenceAudio);
            return null;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Couldn't generate audio for {Word}.", word.Japanese);
            return $"{word.Japanese} will be in your lessons, but its audio couldn't be generated ({ex.Message}). Fetch it from its edit page.";
        }
    }

    /// <summary>
    /// "I know this": the word counts as known from now on. An existing word has
    /// "do not use" cleared; a new one is created asleep from the dictionary.
    /// Returns what happened, for display.
    /// </summary>
    public async Task<string> MarkKnownAsync(string written)
    {
        written = written.Trim();
        if (written.Length == 0) {
            return "Type the word first.";
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<Vocabulary> existing = await db.Vocabulary.Where(v => v.Japanese == written).ToListAsync();
        existing = existing.Where(v => v.Japanese == written).ToList();  // exact, whatever the collation
        if (existing.Count > 0) {
            foreach (Vocabulary v in existing) {
                v.DoNotUse = false;
            }
            await db.SaveChangesAsync();
            SrsStage threshold = (await SettingsService.LoadAsync(db)).KnownWordStage;
            bool studying = existing.Any(v => !v.IsAsleep);
            return studying
                ? $"{written} is already in your vocabulary and can now be used; it counts as known once it reaches {SrsSchedule.DisplayName(threshold)}."
                : $"{written} now counts as known.";
        }

        if (await _dictionary.NeedsReimportAsync(DictionaryImport.MeaningsFormatVersion)) {
            return "The imported dictionary predates stored meanings. Re-import it from Settings, then try again.";
        }
        List<DictionaryWordMatch> matches = await _dictionary.FindWordsAsync(written);
        if (matches.Count == 0) {
            return $"{written} isn't in the dictionary. Add it from the Vocabulary page instead.";
        }

        Vocabulary word = KnownWords.FromDictionary(matches[0], DateTime.UtcNow);
        db.Vocabulary.Add(word);
        await db.SaveChangesAsync();
        string others = matches.Count > 1 ? $" (the most common of {matches.Count} words written this way)" : "";
        return $"Added {written} ({word.Reading}, {word.PrimaryMeaning}){others} as a known word, asleep.";
    }

    /// <summary>
    /// Download the N5, N4 and N3 lists from GitHub (one pinned commit) and add their
    /// words. Null when a seed is already running.
    /// </summary>
    public async Task<JlptSeedResult?> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await SeedLock.WaitAsync(0, cancellationToken)) {
            return null;
        }
        try {
            if (await _dictionary.GetImportAsync() is null) {
                throw new InvalidOperationException("Import the dictionary first (Settings): the lists' readings and meanings come from it.");
            }
            if (await _dictionary.NeedsReimportAsync(DictionaryImport.EntryNumbersFormatVersion)) {
                throw new InvalidOperationException("The imported dictionary is from an older version of KotoNeko. Re-import it from Settings, then try again.");
            }

            HttpClient client = _http.CreateClient(HttpClientName);
            List<JlptListWord> words = new();
            foreach (JlptLevel level in JlptWordLists.SeededLevels) {
                string csv = await DownloadAsync(client, JlptWordLists.DownloadUrl(level), cancellationToken);
                words.AddRange(JlptWordLists.Parse(csv, level));
            }

            await using KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken);
            JlptSeedResult result = await JlptWordLists.SeedAsync(db, words, DateTime.UtcNow, cancellationToken);
            _logger.LogInformation("Seeded the JLPT lists: {Added} added, {Filled} levels filled in, {There} already there, {Missing} not in the dictionary.",
                result.Added, result.LevelsFilled, result.AlreadyThere, result.NotInDictionary);
            return result;
        } finally {
            SeedLock.Release();
        }
    }

    private static async Task<string> DownloadAsync(HttpClient client, string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > JlptWordLists.MaxFileSize) {
            throw new InvalidDataException("A word list download is larger than any list should be.");
        }
        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (body.Length > JlptWordLists.MaxFileSize) {
            throw new InvalidDataException("A word list download is larger than any list should be.");
        }
        return System.Text.Encoding.UTF8.GetString(body);
    }
}
