using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using KotoNeko.Data.Vocab;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>A word's position and dictionary form within a stored sentence.</summary>
public sealed record StoredWord(
    [property: JsonPropertyName("s")] int Start,
    [property: JsonPropertyName("n")] int Length,
    [property: JsonPropertyName("l")] string? Lemma,
    [property: JsonPropertyName("p")] bool IsParticle);

/// <summary>A sentence with its stored readings, ready to display or compare.</summary>
public sealed class SentenceView
{
    public SentenceView(string text, IReadOnlyList<FuriganaPiece> pieces, IReadOnlyList<StoredWord> words, bool isEdited)
    {
        Text = text;
        Pieces = pieces;
        Words = words;
        IsEdited = isEdited;
    }

    public string Text { get; }

    /// <summary>All readings, whether or not the reader needs them.</summary>
    public IReadOnlyList<FuriganaPiece> Pieces { get; }

    public IReadOnlyList<StoredWord> Words { get; }

    public bool IsEdited { get; }

    /// <summary>The readings in the editable bracket format.</summary>
    public string Bracketed => Furigana.Format(Pieces);

    /// <summary>The dictionary form of the word covering a position, if known.</summary>
    public string? LemmaAt(int position) =>
        Words.FirstOrDefault(w => position >= w.Start && position < w.Start + w.Length)?.Lemma;

    /// <summary>The pieces with readings only over words not in <paramref name="known"/>.</summary>
    public IReadOnlyList<FuriganaPiece> ForReader(ISet<string> known) =>
        Furigana.ForReader(Pieces, LemmaAt, known.Contains);

    /// <summary>Positions of single-kana particles (は, へ, を) in the text.</summary>
    public HashSet<int> ParticlePositions() =>
        Words.Where(w => w.IsParticle && w.Length == 1).Select(w => w.Start).ToHashSet();
}

/// <summary>
/// Sentence readings: worked out offline from the dictionary, stored per sentence,
/// and editable. Readings are generated in the background (at startup, after a
/// dictionary import, and when a sentence is saved), never while reviewing.
/// Nothing is generated until a dictionary is imported: readings from an empty
/// dictionary would leave most kanji bare.
/// </summary>
public sealed class SentenceReadingService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly ILogger<SentenceReadingService> _logger;
    private readonly object _lock = new();
    private Task? _backfill;
    private bool _backfillAgain;

    public SentenceReadingService(IDbContextFactory<KotoNekoDbContext> factory, ILogger<SentenceReadingService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    public static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    /// <summary>Stored readings for the given sentences, keyed by sentence text.</summary>
    public async Task<Dictionary<string, SentenceView>> GetManyAsync(IEnumerable<string> texts)
    {
        Dictionary<string, string> byHash = texts.Distinct().ToDictionary(Hash, t => t);
        if (byHash.Count == 0) {
            return new Dictionary<string, SentenceView>();
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<SentenceReading> rows = new();
        foreach (string[] chunk in byHash.Keys.Chunk(1000)) {
            string placeholders = string.Join(",", chunk.Select((_, i) => $"{{{i}}}"));
            rows.AddRange(await db.SentenceReadings
                .FromSqlRaw($"SELECT * FROM `SentenceReadings` WHERE `TextHash` IN ({placeholders})", chunk.Cast<object>().ToArray())
                .AsNoTracking()
                .ToListAsync());
        }

        Dictionary<string, SentenceView> result = new();
        foreach (SentenceReading row in rows) {
            if (row.Text == byHash.GetValueOrDefault(row.TextHash) && ToView(row) is SentenceView view) {
                result[row.Text] = view;
            }
        }
        return result;
    }

    public async Task<SentenceView?> GetAsync(string text) =>
        (await GetManyAsync(new[] { text })).GetValueOrDefault(text);

    /// <summary>
    /// Work out and store readings for any of these sentences that have none.
    /// False (and nothing stored) when no dictionary is imported.
    /// </summary>
    public async Task<bool> EnsureAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        List<string> wanted = texts.Where(t => !string.IsNullOrWhiteSpace(t) && t.Length <= SentenceReading.MaxTextLength)
            .Distinct().ToList();
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken);
        if (!await db.DictionaryImports.AnyAsync(cancellationToken)) {
            return false;
        }

        HashSet<string> have = (await GetManyAsync(wanted)).Keys.ToHashSet();
        SentenceReader reader = new(new DictionaryService(_factory));
        foreach (string text in wanted.Where(t => !have.Contains(t))) {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<SentenceWord> words = await reader.ReadAsync(text);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT IGNORE INTO `SentenceReadings` (`TextHash`, `Text`, `Furigana`, `Words`, `IsEdited`, `UpdatedAt`) " +
                "VALUES ({0}, {1}, {2}, {3}, 0, {4})",
                new object[] {
                    Hash(text), text, Furigana.Format(SentenceReader.ToFurigana(words)),
                    JsonSerializer.Serialize(Stored(words)), DateTime.UtcNow,
                },
                cancellationToken);
        }
        return true;
    }

    /// <summary>
    /// Save corrected readings in the bracket format. Returns an error message, or
    /// null when saved. The plain text must still be exactly the sentence.
    /// </summary>
    public async Task<string?> SaveEditedAsync(string text, string bracketed)
    {
        IReadOnlyList<FuriganaPiece>? pieces = Furigana.Parse(bracketed.Trim());
        if (pieces is null) {
            return "The brackets don't match up. Readings go in [ ] right after their kanji; a base mixing kanji and kana goes in { }.";
        }
        if (Furigana.PlainText(pieces) != text) {
            return "Only the readings can change here: with the brackets removed, the text must be exactly the sentence.";
        }

        // Keep the word boundaries from the generated readings, if there are any.
        SentenceView? current = await GetAsync(text);
        string words = JsonSerializer.Serialize(current?.Words ?? Array.Empty<StoredWord>());

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO `SentenceReadings` (`TextHash`, `Text`, `Furigana`, `Words`, `IsEdited`, `UpdatedAt`) " +
            "VALUES ({0}, {1}, {2}, {3}, 1, {4}) " +
            "ON DUPLICATE KEY UPDATE `Furigana` = VALUES(`Furigana`), `IsEdited` = 1, `UpdatedAt` = VALUES(`UpdatedAt`)",
            Hash(text), text, Furigana.Format(pieces), words, DateTime.UtcNow);
        return null;
    }

    /// <summary>Throw away corrections and generate the readings again.</summary>
    public async Task ResetAsync(string text)
    {
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            string hash = Hash(text);
            await db.SentenceReadings.Where(r => r.TextHash == hash).ExecuteDeleteAsync();
        }
        await EnsureAsync(new[] { text });
    }

    /// <summary>
    /// Words the reader knows well enough to read without furigana: the known-words
    /// pool (see <see cref="KnownWords"/>), except studied items set to show furigana,
    /// since those are being learned without their kanji reading.
    /// </summary>
    public async Task<HashSet<string>> KnownWordsAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        SrsStage threshold = (await SettingsService.LoadAsync(db)).KnownWordStage;
        List<string> known = await db.Vocabulary.AsNoTracking()
            .InPool(threshold)
            .Where(v => !(v.DisplayFurigana && v.Srs != null && v.Srs.Stage != SrsStage.Locked))
            .Select(v => v.Japanese)
            .ToListAsync();
        return known.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Fill in readings for every vocabulary context sentence that lacks them, in the
    /// background. Safe to call often: one run at a time, with at most one more queued.
    /// </summary>
    public void RequestBackfill()
    {
        lock (_lock) {
            if (_backfill is { IsCompleted: false }) {
                _backfillAgain = true;
                return;
            }
            _backfill = Task.Run(RunBackfillAsync);
        }
    }

    private async Task RunBackfillAsync()
    {
        while (true) {
            try {
                await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
                List<string> sentences = await db.Vocabulary.AsNoTracking()
                    .Where(v => v.ContextSentence != null && v.ContextSentence != "")
                    .Select(v => v.ContextSentence!)
                    .ToListAsync();

                // Readings for sentences no item uses any more are dead weight.
                HashSet<string> live = sentences.Select(Hash).ToHashSet();
                List<int> orphans = (await db.SentenceReadings.AsNoTracking()
                        .Select(r => new { r.Id, r.TextHash }).ToListAsync())
                    .Where(r => !live.Contains(r.TextHash)).Select(r => r.Id).ToList();
                foreach (int[] chunk in orphans.Chunk(1000)) {
                    await db.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM `SentenceReadings` WHERE `Id` IN ({string.Join(",", chunk)})");
                }

                if (await EnsureAsync(sentences)) {
                    _logger.LogInformation("Sentence readings are up to date for {Count} sentences.", sentences.Count);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Generating sentence readings failed.");
            }

            lock (_lock) {
                if (!_backfillAgain) {
                    return;
                }
                _backfillAgain = false;
            }
        }
    }

    private static List<StoredWord> Stored(IEnumerable<SentenceWord> words) =>
        words.Select(w => new StoredWord(w.Start, w.Text.Length, w.Lemma, w.IsParticle)).ToList();

    private static SentenceView? ToView(SentenceReading row)
    {
        IReadOnlyList<FuriganaPiece>? pieces = Furigana.Parse(row.Furigana);
        if (pieces is null) {
            return null;
        }
        List<StoredWord> words = JsonSerializer.Deserialize<List<StoredWord>>(row.Words) ?? new List<StoredWord>();
        return new SentenceView(row.Text, pieces, words, row.IsEdited);
    }
}

/// <summary>Starts the sentence-reading backfill when the app starts.</summary>
public sealed class SentenceReadingStartup(SentenceReadingService readings) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        readings.RequestBackfill();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
