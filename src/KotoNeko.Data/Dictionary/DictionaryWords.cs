using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;

namespace KotoNeko.Data.Dictionary;

/// <summary>A dictionary word found for a spelling: what's needed to create vocabulary from it.</summary>
/// <param name="Written">The spelling looked up.</param>
/// <param name="Reading">Its main reading (kana; the spelling itself for kana-only words).</param>
/// <param name="OtherReadings">Other readings of this spelling of this word (今日: こんにち).</param>
/// <param name="Senses">Its senses that apply to this spelling and reading, main sense first.</param>
public sealed record DictionaryWordMatch(
    int WordId,
    string Written,
    string Reading,
    IReadOnlyList<string> OtherReadings,
    VerbClass VerbClass,
    IReadOnlyList<DictionarySense> Senses)
{
    /// <summary>The main sense's glosses: what fills a new item's meanings, like jisho's first sense.</summary>
    public IReadOnlyList<string> MainMeanings => Senses.Count > 0 ? Senses[0].Glosses : Array.Empty<string>();
}

/// <summary>Picks the dictionary words for a spelling, best first.</summary>
public static class DictionaryWords
{
    /// <summary>
    /// The words written <paramref name="written"/>, from its entries (all rows with
    /// that spelling) and the words' stored senses. A spelling shared by several
    /// words (生: なま, せい, …) gives several matches, the most common first.
    /// With <paramref name="reading"/>, only that reading of the spelling is matched.
    /// </summary>
    public static List<DictionaryWordMatch> Match(
        string written, IEnumerable<DictionaryEntry> entries, IReadOnlyDictionary<int, DictionaryMeaning> meanings,
        string? reading = null)
    {
        List<DictionaryWordMatch> result = new();
        bool kanaOnly = Kana.IsAllKana(written);
        string? wantedReading = reading is null ? null : Kana.ToHiragana(reading);

        foreach (IGrouping<int, DictionaryEntry> word in entries
                     .Where(e => e.Written == written && (wantedReading is null || e.Reading == wantedReading))
                     .GroupBy(e => e.WordId)
                     .OrderBy(g => g.Min(e => e.Rank))
                     .ThenBy(g => g.Min(e => e.Order)))
        {
            List<DictionaryEntry> readings = word.OrderBy(e => e.Rank).ThenBy(e => e.Order).ToList();
            string main = kanaOnly ? written : readings[0].Reading;
            List<string> others = kanaOnly
                ? new List<string>()
                : readings.Skip(1).Select(e => e.Reading).Where(r => r != main).Distinct().ToList();

            List<DictionarySense> senses = meanings.TryGetValue(word.Key, out DictionaryMeaning? m)
                ? DictionarySenses.Deserialize(m.Senses).Where(s => Applies(s, written, main, readings[0])).ToList()
                : new List<DictionarySense>();

            result.Add(new DictionaryWordMatch(word.Key, written, main, others, VerbClassOf(readings[0].Type), senses));
        }
        return result;
    }

    /// <summary>
    /// Whether a sense applies. Sense limits name the word's own spellings, so the
    /// する-verb made from a noun (勉強する from 勉強) is also checked as the noun.
    /// </summary>
    private static bool Applies(DictionarySense sense, string written, string reading, DictionaryEntry entry)
    {
        if (sense.AppliesTo(written, reading)) {
            return true;
        }
        return entry.Type == WordType.Suru
            && written.Length > 2 && written.EndsWith("する", StringComparison.Ordinal)
            && reading.Length > 2 && reading.EndsWith("する", StringComparison.Ordinal)
            && sense.AppliesTo(written[..^2], reading[..^2]);
    }

    private static VerbClass VerbClassOf(WordType type) =>
        (type & WordType.Ichidan) != 0 ? VerbClass.Ichidan
        : (type & WordType.Godan) != 0 ? VerbClass.Godan
        : (type & WordType.Suru) != 0 ? VerbClass.Suru
        : (type & WordType.Kuru) != 0 ? VerbClass.Kuru
        : VerbClass.None;
}
