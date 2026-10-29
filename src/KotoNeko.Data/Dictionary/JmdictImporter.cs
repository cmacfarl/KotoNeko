using System.Diagnostics;
using System.Text;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Data.Dictionary;

/// <summary>The steps of an import, for progress display.</summary>
public enum ImportStage
{
    Downloading,
    Reading,
    Writing,
    Done,
}

/// <summary>How far an import has got. <see cref="Total"/> is 0 when unknown.</summary>
public readonly record struct ImportProgress(ImportStage Stage, long Done, long Total);

/// <summary>What an import wrote.</summary>
public sealed record ImportResult(int Entries, string Version, string DictionaryDate, TimeSpan Elapsed, long PeakMemoryMb);

/// <summary>
/// Imports JMdict, in the JSON form published by jmdict-simplified
/// (github.com/scriptin/jmdict-simplified), into <see cref="DictionaryEntry"/> rows.
/// The file is streamed a word at a time. The import replaces whatever was there
/// before, in one transaction, so it can be re-run safely and the old dictionary
/// stays usable until the new one is complete. JMdict is © the Electronic
/// Dictionary Research and Development Group, used under CC BY-SA 4.0.
/// </summary>
public static class JmdictImporter
{
    public const string SourceName = "JMdict (EDRDG, CC BY-SA 4.0) via jmdict-simplified";

    private const int BatchSize = 1000;
    private const int ReadProgressEvery = 5000;

    // Order = word position × slots + spelling position, so spellings of one word stay in JMdict's order.
    private const int SpellingSlots = 32;

    // Spellings and readings the dictionary marks as search-only: never offered.
    private static readonly HashSet<string> SearchOnly = new() { "sK", "sk" };

    // Rare, outdated or irregular forms: offered, but after everything else.
    private static readonly HashSet<string> Discouraged = new() { "rK", "oK", "iK", "ik", "ok", "rk" };

    private static readonly HashSet<string> ParticleLikePos = new() { "prt", "aux", "aux-v", "aux-adj", "cop", "conj" };

    /// <summary>Read the file and replace the dictionary tables.</summary>
    public static async Task<ImportResult> ImportAsync(
        KotoNekoDbContext db, string path, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        Stopwatch timer = Stopwatch.StartNew();
        EntryBuilder builder = new();
        JmdictReader reader = new();

        await using (FileStream stream = File.OpenRead(path))
        {
            long total = stream.Length;
            progress?.Report(new ImportProgress(ImportStage.Reading, 0, total));
            await reader.ReadAsync(stream, word =>
            {
                builder.Add(word);
                if (builder.WordCount % ReadProgressEvery == 0)
                {
                    progress?.Report(new ImportProgress(ImportStage.Reading, stream.Position, total));
                }
            }, cancellationToken);
        }

        if (builder.WordCount == 0)
        {
            throw new InvalidDataException("No words were found: is this a jmdict-simplified JSON file?");
        }

        List<DictionaryEntry> rows = builder.Entries;
        progress?.Report(new ImportProgress(ImportStage.Writing, 0, rows.Count));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM `DictionaryEntries`;", cancellationToken);
        await db.Database.ExecuteSqlRawAsync("DELETE FROM `DictionaryImports`;", cancellationToken);
        // Readings worked out from the old dictionary are stale: drop them so they
        // are rebuilt from this one. Readings the user corrected are kept.
        await db.Database.ExecuteSqlRawAsync("DELETE FROM `SentenceReadings` WHERE `IsEdited` = 0;", cancellationToken);

        for (int i = 0; i < rows.Count; i += BatchSize)
        {
            await InsertBatchAsync(db, rows.GetRange(i, Math.Min(BatchSize, rows.Count - i)), cancellationToken);
            progress?.Report(new ImportProgress(ImportStage.Writing, Math.Min(i + BatchSize, rows.Count), rows.Count));
        }

        db.DictionaryImports.Add(new DictionaryImport
        {
            Source = SourceName,
            Version = reader.Version,
            DictionaryDate = reader.DictDate,
            ImportedAt = DateTime.UtcNow,
            EntryCount = rows.Count,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        progress?.Report(new ImportProgress(ImportStage.Done, rows.Count, rows.Count));
        long peakMb = Process.GetCurrentProcess().PeakWorkingSet64 / (1024 * 1024);
        return new ImportResult(rows.Count, reader.Version, reader.DictDate, timer.Elapsed, peakMb);
    }

    /// <summary>Turn dictionary words into rows (see <see cref="EntryBuilder"/>).</summary>
    public static List<DictionaryEntry> BuildEntries(IEnumerable<JmdictWord> words)
    {
        EntryBuilder builder = new();
        foreach (JmdictWord word in words)
        {
            builder.Add(word);
        }
        return builder.Entries;
    }

    /// <summary>
    /// Turns dictionary words into rows, one word at a time: one row per (reading,
    /// spelling) pair the dictionary allows, plus the kana spelling of each
    /// reading. Nouns that take する also get a する-verb row (勉強 → 勉強する) so
    /// they inflect as one word. A pair seen twice keeps its best rank.
    /// </summary>
    private sealed class EntryBuilder
    {
        private readonly Dictionary<(string Reading, string Written), DictionaryEntry> _best = new();

        public int WordCount { get; private set; }

        public List<DictionaryEntry> Entries => _best.Values.ToList();

        public void Add(JmdictWord word)
        {
            int order = WordCount++;
            List<string> pos = word.Sense.SelectMany(s => s.PartOfSpeech).Distinct().ToList();
            // Usually written in kana: judged on the main sense only, since a minor
            // sense marked "uk" (遭う in 会う's entry) says nothing about the word.
            bool uk = word.Sense.Count > 0 && word.Sense[0].Misc.Contains("uk");
            bool particle = pos.Count > 0 && pos.All(ParticleLikePos.Contains);
            WordType type = TypeOf(pos);
            bool takesSuru = pos.Contains("vs");

            List<JmdictForm> kanji = word.Kanji.Where(k => !k.Tags.Any(SearchOnly.Contains)).ToList();

            foreach (JmdictKana kana in word.Kana.Where(k => !k.Tags.Any(SearchOnly.Contains)))
            {
                string reading = Kana.ToHiragana(kana.Text);
                int kanaPenalty = kana.Tags.Any(Discouraged.Contains) ? 20 : 0;

                IEnumerable<JmdictForm> spellings = kana.AppliesToKanji.Contains("*")
                    ? kanji
                    : kanji.Where(k => kana.AppliesToKanji.Contains(k.Text));

                foreach (JmdictForm k in spellings)
                {
                    int rank = (k.Common && kana.Common ? 0 : 10)
                        + (k.Tags.Any(Discouraged.Contains) ? 20 : 0)
                        + kanaPenalty
                        + (uk ? 3 : 0);
                    // Within a word, JMdict lists the main spelling first (会う before 逢う).
                    int spellingOrder = order * SpellingSlots + Math.Min(kanji.IndexOf(k), SpellingSlots - 2);
                    Put(reading, k.Text, type, rank, spellingOrder, uk, particle);
                    if (takesSuru)
                    {
                        Put(reading + "する", k.Text + "する", WordType.Suru, rank, spellingOrder, uk, false);
                    }
                }

                // The kana spelling: the main one for katakana words and words usually in kana.
                int kanaRank = (kana.Common ? 0 : 10) + kanaPenalty + (kanji.Count > 0 && !uk ? 5 : 0);
                int kanaOrder = order * SpellingSlots + SpellingSlots - 1;
                Put(reading, kana.Text, type, kanaRank, kanaOrder, uk, particle);
                if (takesSuru)
                {
                    Put(reading + "する", kana.Text + "する", WordType.Suru, kanaRank, kanaOrder, uk, false);
                }
            }
        }

        private void Put(string reading, string written, WordType type, int rank, int order, bool uk, bool particle)
        {
            if (reading.Length == 0 || reading.Length > DictionaryEntry.MaxLength || written.Length > DictionaryEntry.MaxLength)
            {
                return;
            }

            if (_best.TryGetValue((reading, written), out DictionaryEntry? existing))
            {
                if (existing.Rank <= rank)
                {
                    existing.Type |= type;
                    return;
                }
                type |= existing.Type;
            }

            _best[(reading, written)] = new DictionaryEntry
            {
                Reading = reading,
                Written = written,
                Type = type,
                Rank = rank,
                Order = order,
                UsuallyKana = uk,
                ParticleLike = particle,
            };
        }
    }

    /// <summary>How a word inflects, from its JMdict part-of-speech codes.</summary>
    private static WordType TypeOf(IEnumerable<string> pos)
    {
        WordType type = WordType.None;
        foreach (string p in pos)
        {
            type |= p switch
            {
                "v1" or "v1-s" => WordType.Ichidan,
                "vk" => WordType.Kuru,
                "vs-i" or "vs-s" => WordType.Suru,
                "adj-i" or "adj-ix" => WordType.IAdjective,
                _ when p.StartsWith("v5", StringComparison.Ordinal) => WordType.Godan,
                _ => WordType.None,
            };
        }
        return type;
    }

    private static async Task InsertBatchAsync(KotoNekoDbContext db, List<DictionaryEntry> batch, CancellationToken cancellationToken)
    {
        StringBuilder sql = new("INSERT INTO `DictionaryEntries` (`Reading`, `Written`, `Type`, `Rank`, `Order`, `UsuallyKana`, `ParticleLike`) VALUES ");
        List<object> values = new(batch.Count * 7);
        for (int i = 0; i < batch.Count; i++)
        {
            DictionaryEntry e = batch[i];
            int p = values.Count;
            sql.Append(i == 0 ? "" : ",")
                .Append($"({{{p}}},{{{p + 1}}},{{{p + 2}}},{{{p + 3}}},{{{p + 4}}},{{{p + 5}}},{{{p + 6}}})");
            values.Add(e.Reading);
            values.Add(e.Written);
            values.Add((int)e.Type);
            values.Add(e.Rank);
            values.Add(e.Order);
            values.Add(e.UsuallyKana);
            values.Add(e.ParticleLike);
        }
        await db.Database.ExecuteSqlRawAsync(sql.ToString(), values, cancellationToken);
    }
}

// The parts of the jmdict-simplified JSON format the importer uses.

public sealed class JmdictWord
{
    [JsonPropertyName("kanji")] public List<JmdictForm> Kanji { get; set; } = new();
    [JsonPropertyName("kana")] public List<JmdictKana> Kana { get; set; } = new();
    [JsonPropertyName("sense")] public List<JmdictSense> Sense { get; set; } = new();
}

public class JmdictForm
{
    [JsonPropertyName("common")] public bool Common { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
}

public sealed class JmdictKana : JmdictForm
{
    [JsonPropertyName("appliesToKanji")] public List<string> AppliesToKanji { get; set; } = new();
}

public sealed class JmdictSense
{
    [JsonPropertyName("partOfSpeech")] public List<string> PartOfSpeech { get; set; } = new();
    [JsonPropertyName("misc")] public List<string> Misc { get; set; } = new();
}
