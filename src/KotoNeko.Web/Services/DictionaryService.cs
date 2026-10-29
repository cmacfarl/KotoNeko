using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>
/// Looks words up in the imported dictionary and in the user's own vocabulary, for
/// the offline kana → kanji converter. Works with an empty dictionary too: the
/// converter then offers the user's vocabulary plus hiragana and katakana.
/// </summary>
public class DictionaryService : IDictionaryLookup
{
    // MySQL takes up to 65,535 parameters; stay well under it.
    private const int ChunkSize = 5000;

    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public DictionaryService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>The current import, or null if no dictionary has been imported.</summary>
    public async Task<DictionaryImport?> GetImportAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.DictionaryImports.AsNoTracking().OrderByDescending(i => i.ImportedAt).FirstOrDefaultAsync();
    }

    public async Task<IReadOnlyList<DictionaryWord>> LookupAsync(IReadOnlyCollection<string> readings)
    {
        if (readings.Count == 0) {
            return Array.Empty<DictionaryWord>();
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        HashSet<string> wanted = new(readings, StringComparer.Ordinal);
        List<DictionaryWord> words = new();

        foreach (string[] chunk in wanted.Chunk(ChunkSize)) {
            string placeholders = string.Join(",", chunk.Select((_, i) => $"{{{i}}}"));
            List<DictionaryEntry> rows = await db.DictionaryEntries
                .FromSqlRaw($"SELECT * FROM `DictionaryEntries` WHERE `Reading` IN ({placeholders})", chunk.Cast<object>().ToArray())
                .AsNoTracking()
                .ToListAsync();

            // Re-check in C#: the comparison must be exact whatever the column collation.
            words.AddRange(rows
                .Where(r => wanted.Contains(r.Reading))
                .Select(r => new DictionaryWord(r.Reading, r.Written, r.Type, r.Rank, r.Order, r.UsuallyKana, r.ParticleLike)));
        }

        // The user's own words are always known, even ones the dictionary lacks.
        var vocabulary = await db.Vocabulary
            .AsNoTracking()
            .Select(v => new { v.Japanese, v.Reading, v.VerbClass })
            .ToListAsync();
        foreach (var v in vocabulary) {
            string reading = Kana.ToHiragana(v.Reading);
            if (wanted.Contains(reading)) {
                words.Add(new DictionaryWord(reading, v.Japanese, TypeOf(v.VerbClass), Rank: 0, Order: int.MaxValue, FromVocabulary: true));
            }
        }

        return await WithPastChoicesAsync(db, wanted, words);
    }

    public async Task<IReadOnlyList<DictionaryWord>> LookupWrittenAsync(IReadOnlyCollection<string> spellings)
    {
        if (spellings.Count == 0) {
            return Array.Empty<DictionaryWord>();
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        HashSet<string> wanted = new(spellings, StringComparer.Ordinal);
        List<DictionaryWord> words = new();

        foreach (string[] chunk in wanted.Chunk(ChunkSize)) {
            string placeholders = string.Join(",", chunk.Select((_, i) => $"{{{i}}}"));
            List<DictionaryEntry> rows = await db.DictionaryEntries
                .FromSqlRaw($"SELECT * FROM `DictionaryEntries` WHERE `Written` IN ({placeholders})", chunk.Cast<object>().ToArray())
                .AsNoTracking()
                .ToListAsync();
            words.AddRange(rows
                .Where(r => wanted.Contains(r.Written))
                .Select(r => new DictionaryWord(r.Reading, r.Written, r.Type, r.Rank, r.Order, r.UsuallyKana, r.ParticleLike)));
        }

        // The user's own words, with the reading they learned: these win.
        var vocabulary = await db.Vocabulary
            .AsNoTracking()
            .Select(v => new { v.Japanese, v.Reading, v.VerbClass })
            .ToListAsync();
        foreach (var v in vocabulary.Where(v => wanted.Contains(v.Japanese))) {
            words.Add(new DictionaryWord(Kana.ToHiragana(v.Reading), v.Japanese, TypeOf(v.VerbClass),
                Rank: 0, Order: int.MaxValue, FromVocabulary: true));
        }

        return words;
    }

    /// <summary>
    /// Mark the words the user picked before. A past choice no word explains (a
    /// katakana spelling, say) is added as a plain word of its own.
    /// </summary>
    private static async Task<List<DictionaryWord>> WithPastChoicesAsync(
        KotoNekoDbContext db, HashSet<string> wanted, List<DictionaryWord> words)
    {
        Dictionary<(string, string), DateTime> chosen = new();
        foreach (string[] chunk in wanted.Chunk(ChunkSize)) {
            string placeholders = string.Join(",", chunk.Select((_, i) => $"{{{i}}}"));
            List<ImeChoice> rows = await db.ImeChoices
                .FromSqlRaw($"SELECT * FROM `ImeChoices` WHERE `Reading` IN ({placeholders})", chunk.Cast<object>().ToArray())
                .AsNoTracking()
                .ToListAsync();
            foreach (ImeChoice c in rows.Where(c => wanted.Contains(c.Reading))) {
                chosen[(c.Reading, c.Written)] = c.LastChosenAt;
            }
        }

        if (chosen.Count == 0) {
            return words;
        }

        List<DictionaryWord> result = words
            .Select(w => chosen.TryGetValue((w.Reading, w.Written), out DateTime at) ? w with { LastChosen = at } : w)
            .ToList();
        HashSet<(string, string)> known = words.Select(w => (w.Reading, w.Written)).ToHashSet();
        foreach (((string reading, string written), DateTime at) in chosen) {
            if (!known.Contains((reading, written))) {
                result.Add(new DictionaryWord(reading, written, WordType.None, Rank: 50, Order: int.MaxValue, LastChosen: at));
            }
        }
        return result;
    }

    /// <summary>Remember spellings the user picked, so they come first next time.</summary>
    public async Task SaveChoicesAsync(IEnumerable<(string Reading, string Written)> choices)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        DateTime now = DateTime.UtcNow;
        foreach ((string reading, string written) in choices.Distinct()) {
            if (reading.Length == 0 || reading.Length > DictionaryEntry.MaxLength
                || written.Length == 0 || written.Length > DictionaryEntry.MaxLength) {
                continue;
            }

            ImeChoice? row = await db.ImeChoices.FirstOrDefaultAsync(c => c.Reading == reading && c.Written == written);
            if (row is null) {
                db.ImeChoices.Add(new ImeChoice { Reading = reading, Written = written, Count = 1, LastChosenAt = now });
            } else {
                row.Count++;
                row.LastChosenAt = now;
            }
        }
        await db.SaveChangesAsync();
    }

    public async Task<int> CountChoicesAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.ImeChoices.CountAsync();
    }

    /// <summary>Forget every remembered choice: candidates go back to dictionary order.</summary>
    public async Task ForgetChoicesAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        await db.ImeChoices.ExecuteDeleteAsync();
    }

    private static WordType TypeOf(VerbClass verbClass) => verbClass switch
    {
        VerbClass.Ichidan => WordType.Ichidan,
        VerbClass.Godan => WordType.Godan,
        VerbClass.Suru => WordType.Suru,
        VerbClass.Kuru => WordType.Kuru,
        _ => WordType.None,
    };
}
