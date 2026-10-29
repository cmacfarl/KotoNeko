using System.Text;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Text;
using KotoNeko.Data.Dictionary;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Data.Vocab;

/// <summary>One row of a JLPT word list: a JMdict entry, how the list writes it, and its level.</summary>
public sealed record JlptListWord(int EntryNumber, string Kana, string Kanji, string Definition, JlptLevel Level)
{
    /// <summary>The spelling: the kanji form, or the kana for words written in kana.</summary>
    public string Written => Kanji.Length > 0 ? Kanji : Kana;
}

/// <summary>A word already in KotoNeko, as the seeder needs to see it.</summary>
public sealed record ExistingWord(int Id, string Japanese, string Reading, JlptLevel? Level);

/// <summary>What seeding will do: words to add, and existing words whose level to fill in.</summary>
public sealed class JlptSeedPlan
{
    public List<Vocabulary> New { get; } = new();

    /// <summary>Existing words getting a level, by vocabulary id.</summary>
    public Dictionary<int, JlptLevel> LevelUpdates { get; } = new();

    /// <summary>List words already in KotoNeko (their level may still be filled in).</summary>
    public int AlreadyThere { get; set; }

    /// <summary>New words not found in the dictionary, added from the list's own definition.</summary>
    public int NotInDictionary { get; set; }
}

/// <summary>What a seed run did.</summary>
public sealed record JlptSeedResult(int Added, int LevelsFilled, int AlreadyThere, int NotInDictionary);

/// <summary>
/// Jonathan Waller's JLPT vocabulary lists (tanos.co.uk, CC BY), as published with
/// JMdict entry numbers by stephenmk/yomitan-jlpt-vocab (CC BY-SA 4.0). Seeding adds
/// every listed word asleep, so it counts as known without entering lessons; N3
/// words also start as "do not use", for the user to pick out the ones they know.
/// </summary>
public static class JlptWordLists
{
    public const string SourceName = "JLPT word lists (tanos.co.uk)";

    public const string SourceNotes =
        "Jonathan Waller's JLPT vocabulary lists (tanos.co.uk, CC BY), matched to JMdict entries " +
        "by stephenmk/yomitan-jlpt-vocab on GitHub (CC BY-SA 4.0). Readings, meanings and verb " +
        "classes come from JMdict (EDRDG, CC BY-SA 4.0).";

    /// <summary>The commit the lists are downloaded from, so a seed is repeatable.</summary>
    public const string Commit = "b062d4e38c4bdd0950ae1d4ec55f04b176182e03";

    /// <summary>The levels KotoNeko seeds, easiest first.</summary>
    public static readonly IReadOnlyList<JlptLevel> SeededLevels = new[] { JlptLevel.N5, JlptLevel.N4, JlptLevel.N3 };

    /// <summary>No list is anywhere near this big; a larger download is rejected.</summary>
    public const int MaxFileSize = 2 * 1024 * 1024;

    private const string Header = "jmdict_seq,kana,kanji,waller_definition";

    // Matches Vocabulary.Japanese's column length.
    private const int MaxWordLength = 100;

    public static string DownloadUrl(JlptLevel level) =>
        $"https://raw.githubusercontent.com/stephenmk/yomitan-jlpt-vocab/{Commit}/original_data/n{(int)level}.csv";

    /// <summary>Whether a level's words start as "do not use": N3 and harder.</summary>
    public static bool StartsAsDoNotUse(JlptLevel level) => level <= JlptLevel.N3;

    /// <summary>Parse one list file (CSV with a header row).</summary>
    public static List<JlptListWord> Parse(string csv, JlptLevel level)
    {
        List<List<string>> rows = ReadCsv(csv);
        if (rows.Count == 0 || string.Join(",", rows[0]).Trim('﻿', ' ') != Header) {
            throw new InvalidDataException($"The {level} list doesn't start with the expected header ({Header}).");
        }

        List<JlptListWord> words = new();
        foreach (List<string> row in rows.Skip(1)) {
            if (row.Count == 1 && row[0].Length == 0) {
                continue;  // a blank line
            }
            if (row.Count < 4 || !int.TryParse(row[0], out int entry)) {
                throw new InvalidDataException($"A row of the {level} list isn't in the expected form: {string.Join(",", row)}");
            }
            string kana = row[1].Trim(), kanji = row[2].Trim();
            if (kana.Length == 0) {
                continue;
            }
            words.Add(new JlptListWord(entry, kana, kanji, row[3].Trim(), level));
        }
        return words;
    }

    /// <summary>
    /// Work out what seeding does, without touching the database. Rows sharing an
    /// entry and spelling become one word, with the other readings as alternates
    /// (行き: いき, ゆき). A word on several lists takes the easiest level. Words
    /// already in KotoNeko (same spelling and reading) are left alone, except that
    /// an empty or harder level is filled in.
    /// <para>
    /// Each word is found in the dictionary by, in turn: its entry number with the
    /// list's spelling; its entry number with the dictionary's usual spelling for
    /// that reading (the list's 明い is JMdict's rare spelling of 明るい); any word
    /// with the list's spelling and reading (entries renumbered since the list was
    /// made). Failing all three, the list's own definition is used.
    /// <paramref name="entries"/> must hold the rows of every listed entry number
    /// and of every listed spelling.
    /// </para>
    /// </summary>
    public static JlptSeedPlan Plan(
        IEnumerable<JlptListWord> words, IEnumerable<ExistingWord> existing,
        IEnumerable<DictionaryEntry> entries, IReadOnlyDictionary<int, DictionaryMeaning> meanings, DateTime now)
    {
        JlptSeedPlan plan = new();
        ILookup<string, ExistingWord> existingByWriting = existing.ToLookup(w => w.Japanese, StringComparer.Ordinal);
        List<DictionaryEntry> all = entries.DistinctBy(e => (e.WordId, e.Reading, e.Written)).ToList();
        ILookup<int, DictionaryEntry> entriesByWord = all.ToLookup(e => e.WordId);
        ILookup<string, DictionaryEntry> entriesByWriting = all.ToLookup(e => e.Written, StringComparer.Ordinal);
        HashSet<(string, string)> planned = new();

        var groups = words
            .Where(w => w.Written.Length <= MaxWordLength && w.Kana.Length <= MaxWordLength)
            .Select((w, i) => (Word: w, Index: i))
            .GroupBy(x => (x.Word.EntryNumber, x.Word.Written))
            .Select(g => new {
                g.Key.EntryNumber,
                g.Key.Written,
                Level = g.Max(x => x.Word.Level),
                Rows = g.OrderByDescending(x => x.Word.Level).ThenBy(x => x.Index).Select(x => x.Word).ToList(),
                First = g.Min(x => x.Index),
            })
            .OrderByDescending(g => g.Level)
            .ThenBy(g => g.First);

        foreach (var group in groups) {
            List<string> readings = group.Rows.Select(r => r.Kana).Distinct().ToList();
            HashSet<string> heard = readings.Select(Kana.ToHiragana).ToHashSet();
            (string written, DictionaryWordMatch? match) = Find(group.EntryNumber, group.Written, readings[0]);

            ExistingWord? already = existingByWriting[written].Concat(existingByWriting[group.Written])
                .FirstOrDefault(w => heard.Contains(Kana.ToHiragana(w.Reading)));
            if (already is not null) {
                plan.AlreadyThere++;
                JlptLevel? current = plan.LevelUpdates.TryGetValue(already.Id, out JlptLevel pending) ? pending : already.Level;
                if (current is null || current < group.Level) {
                    plan.LevelUpdates[already.Id] = group.Level;
                }
                continue;
            }
            if (!planned.Add((written, Kana.ToHiragana(readings[0])))) {
                continue;
            }

            bool doNotUse = StartsAsDoNotUse(group.Level);
            Vocabulary word;
            if (match is null) {
                plan.NotInDictionary++;
                word = new Vocabulary {
                    Japanese = written,
                    Reading = readings[0],
                    IsAsleep = true,
                    DoNotUse = doNotUse,
                    CreatedAt = now,
                };
            } else {
                // The list's reading, which Match keeps for kana words and kanji words alike.
                word = KnownWords.FromDictionary(match with { Reading = Kana.IsAllKana(written) ? written : Kana.ToHiragana(readings[0]) },
                    now, doNotUse);
            }

            if (word.Meanings.Count == 0) {
                word.Meanings.AddRange(DefinitionMeanings(group.Rows[0].Definition)
                    .Select((m, i) => new VocabularyMeaning { Text = m, SortOrder = i }));
            }
            word.AlternateReadings = string.Join(";", readings.Skip(1)
                .Select(r => Kana.IsAllKana(written) ? r : Kana.ToHiragana(r))
                .Where(r => r != word.Reading));
            word.JlptLevel = group.Level;
            plan.New.Add(word);
        }
        return plan;

        // The spelling to use and its dictionary word; the list's spelling and null if none is found.
        (string Written, DictionaryWordMatch? Match) Find(int entryNumber, string listed, string reading)
        {
            string hiragana = Kana.ToHiragana(reading);
            List<DictionaryEntry> ofWord = entriesByWord[entryNumber].ToList();
            if (DictionaryWords.Match(listed, ofWord, meanings, reading).FirstOrDefault() is { } exact) {
                return (listed, exact);
            }

            if (!Kana.IsAllKana(listed)
                && ofWord.Where(e => e.Reading == hiragana)
                       .OrderBy(e => e.Rank).ThenBy(e => e.Order).FirstOrDefault() is { } usual
                && DictionaryWords.Match(usual.Written, ofWord, meanings, reading).FirstOrDefault() is { } respelled) {
                return (usual.Written, respelled);
            }

            if (DictionaryWords.Match(listed, entriesByWriting[listed], meanings, reading).FirstOrDefault() is { } other) {
                return (listed, other);
            }
            return (listed, null);
        }
    }

    /// <summary>
    /// Add the lists' words to the database, in one transaction. Safe to run again:
    /// words added before are found and left alone.
    /// </summary>
    public static async Task<JlptSeedResult> SeedAsync(
        KotoNekoDbContext db, IReadOnlyList<JlptListWord> words, DateTime now, CancellationToken cancellationToken = default)
    {
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Every listed entry's rows, plus every row with a listed spelling (for
        // entries renumbered since the list was made), and those words' meanings.
        // Entry numbers and word ids are integers, so they can go straight into the SQL.
        List<DictionaryEntry> entries = new();
        foreach (int[] chunk in words.Select(w => w.EntryNumber).Distinct().Chunk(5000)) {
            entries.AddRange(await db.DictionaryEntries
                .FromSqlRaw($"SELECT * FROM `DictionaryEntries` WHERE `WordId` IN ({string.Join(",", chunk)})")
                .AsNoTracking().ToListAsync(cancellationToken));
        }
        foreach (string[] chunk in words.Select(w => w.Written).Distinct().Chunk(5000)) {
            string placeholders = string.Join(",", chunk.Select((_, i) => $"{{{i}}}"));
            entries.AddRange(await db.DictionaryEntries
                .FromSqlRaw($"SELECT * FROM `DictionaryEntries` WHERE `Written` IN ({placeholders})", chunk.Cast<object>().ToArray())
                .AsNoTracking().ToListAsync(cancellationToken));
        }

        Dictionary<int, DictionaryMeaning> meanings = new();
        foreach (int[] chunk in entries.Select(e => e.WordId).Distinct().Chunk(5000)) {
            foreach (DictionaryMeaning m in await db.DictionaryMeanings
                         .FromSqlRaw($"SELECT * FROM `DictionaryMeanings` WHERE `Id` IN ({string.Join(",", chunk)})")
                         .AsNoTracking().ToListAsync(cancellationToken)) {
                meanings[m.Id] = m;
            }
        }

        List<ExistingWord> existing = await db.Vocabulary.AsNoTracking()
            .Select(v => new ExistingWord(v.Id, v.Japanese, v.Reading, v.JlptLevel))
            .ToListAsync(cancellationToken);

        JlptSeedPlan plan = Plan(words, existing, entries, meanings, now);

        if (plan.LevelUpdates.Count > 0) {
            List<int> ids = plan.LevelUpdates.Keys.ToList();
            foreach (Vocabulary v in await db.Vocabulary.Where(v => ids.Contains(v.Id)).ToListAsync(cancellationToken)) {
                v.JlptLevel = plan.LevelUpdates[v.Id];
            }
        }

        if (plan.New.Count > 0) {
            SourceMaterial? source = await db.SourceMaterials.FirstOrDefaultAsync(s => s.Name == SourceName, cancellationToken);
            if (source is null) {
                source = new SourceMaterial { Name = SourceName, Kind = SourceKind.Web, Notes = SourceNotes, CreatedAt = now };
                db.SourceMaterials.Add(source);
            }
            foreach (Vocabulary v in plan.New) {
                v.SourceMaterial = source;
            }
            db.Vocabulary.AddRange(plan.New);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new JlptSeedResult(plan.New.Count, plan.LevelUpdates.Count, plan.AlreadyThere, plan.NotInDictionary);
    }

    /// <summary>The list's own definition as meanings ("to open, to become open").</summary>
    private static IEnumerable<string> DefinitionMeanings(string definition) =>
        definition.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(m => m.Length is > 0 and <= 300)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>A small CSV reader: commas, quoted fields with "" escapes, CRLF or LF lines.</summary>
    internal static List<List<string>> ReadCsv(string text)
    {
        List<List<string>> rows = new();
        List<string> row = new();
        StringBuilder field = new();
        bool quoted = false;

        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (quoted) {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') {
                    field.Append('"');
                    i++;
                } else if (c == '"') {
                    quoted = false;
                } else {
                    field.Append(c);
                }
                continue;
            }

            switch (c) {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }
        if (field.Length > 0 || row.Count > 0) {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
