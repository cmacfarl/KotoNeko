using KotoNeko.Core.Text;

namespace KotoNeko.Core.Ime;

/// <summary>A dictionary word as the converter sees it.</summary>
/// <param name="Reading">Reading in hiragana: the lookup key.</param>
/// <param name="Written">How it is written (kanji, kana, or a mix).</param>
/// <param name="Type">How it inflects.</param>
/// <param name="Rank">Lower is better: common forms first, rare and outdated forms last.</param>
/// <param name="Order">Stable tie-break within a rank.</param>
/// <param name="UsuallyKana">The dictionary says this word is usually written in kana.</param>
/// <param name="ParticleLike">Only a particle, auxiliary, copula or conjunction: write it in kana.</param>
/// <param name="LastChosen">When the user last picked this spelling for this reading, if ever.</param>
/// <param name="FromVocabulary">One of the user's own vocabulary items rather than a dictionary entry.</param>
public sealed record DictionaryWord(
    string Reading,
    string Written,
    WordType Type,
    int Rank,
    int Order,
    bool UsuallyKana = false,
    bool ParticleLike = false,
    DateTime? LastChosen = null,
    bool FromVocabulary = false);

/// <summary>Looks up words by reading. Implemented over the imported dictionary.</summary>
public interface IDictionaryLookup
{
    /// <summary>
    /// Every word whose hiragana reading is one of <paramref name="readings"/>, with
    /// <see cref="DictionaryWord.LastChosen"/> filled in from the user's past choices.
    /// A past choice that is in no dictionary (say, a katakana spelling) comes back
    /// as a word of its own.
    /// </summary>
    Task<IReadOnlyList<DictionaryWord>> LookupAsync(IReadOnlyCollection<string> readings);

    /// <summary>Every word written exactly as one of <paramref name="spellings"/> (dictionary and vocabulary).</summary>
    Task<IReadOnlyList<DictionaryWord>> LookupWrittenAsync(IReadOnlyCollection<string> spellings);
}

/// <summary>One segment of a conversion: the kana it covers and what it could be written as.</summary>
/// <param name="Reading">The kana for this segment, exactly as typed.</param>
/// <param name="Candidates">Ways to write it, best first. Never empty: kana is always offered.</param>
public sealed record ConversionSegment(string Reading, IReadOnlyList<string> Candidates);

/// <summary>
/// Offline kana → kanji conversion. A typed kana run is split into segments by a
/// minimum-cost path through every dictionary word (including inflected forms)
/// that matches a stretch of it, then each segment gets its candidate spellings.
/// Readings are compared in hiragana, so こーひー finds コーヒー.
/// </summary>
public sealed class KanaKanjiConverter
{
    /// <summary>Longest stretch of kana tried as one word.</summary>
    public const int MaxWordLength = 16;

    // Path costs. Every segment costs something, so fewer, longer words win; a
    // single kana that is no word at all is a last resort.
    private const double KnownWordCost = 1.0;
    private const double UncommonWordCost = 0.3;
    private const double UnknownKanaCost = 5.0;
    // Particles follow words; one at the very start (に|ほんの) is almost never right.
    private const double ParticleFirstCost = 0.5;
    // An inflected match (解こう from 解く) costs a little more than a word found as
    // is, so と|こうえん beats とこう|えん when the split is otherwise a tie.
    private const double InflectedCost = 0.05;

    private readonly IDictionaryLookup _lookup;

    public KanaKanjiConverter(IDictionaryLookup lookup)
    {
        _lookup = lookup;
    }

    /// <summary>Split <paramref name="kana"/> into segments and offer candidates for each.</summary>
    /// <param name="usePastChoices">
    /// Put spellings the user picked before first. Off for fields where the choice
    /// itself is being tested (dictation), so the default never gives it away.
    /// </param>
    public async Task<IReadOnlyList<ConversionSegment>> ConvertAsync(string kana, bool usePastChoices = true)
    {
        if (string.IsNullOrEmpty(kana)) {
            return Array.Empty<ConversionSegment>();
        }

        string key = Kana.ToHiragana(kana);
        Dictionary<(int, int), List<Match>> spans = await MatchSpansAsync(key);

        // Cheapest path from the start to each position.
        int n = key.Length;
        double[] cost = new double[n + 1];
        int[] from = new int[n + 1];
        Array.Fill(cost, double.MaxValue);
        cost[0] = 0;

        for (int end = 1; end <= n; end++) {
            for (int start = Math.Max(0, end - MaxWordLength); start < end; start++) {
                if (cost[start] == double.MaxValue) {
                    continue;
                }

                double step;
                if (spans.TryGetValue((start, end), out List<Match>? matches)) {
                    step = KnownWordCost + (matches.Min(m => m.Word.Rank) > 0 ? UncommonWordCost : 0)
                        + (matches.All(m => m.Inflected) ? InflectedCost : 0)
                        + (start == 0 && matches.All(m => m.Word.ParticleLike) ? ParticleFirstCost : 0);
                } else if (end - start == 1) {
                    step = UnknownKanaCost;
                } else {
                    continue;
                }

                if (cost[start] + step < cost[end]) {
                    cost[end] = cost[start] + step;
                    from[end] = start;
                }
            }
        }

        List<ConversionSegment> segments = new();
        for (int end = n; end > 0; end = from[end]) {
            int start = from[end];
            spans.TryGetValue((start, end), out List<Match>? matches);
            segments.Add(new ConversionSegment(kana[start..end], Candidates(kana[start..end], matches, usePastChoices)));
        }
        segments.Reverse();
        return segments;
    }

    /// <summary>Candidates for <paramref name="kana"/> taken as one segment.</summary>
    public async Task<IReadOnlyList<string>> CandidatesAsync(string kana, bool usePastChoices = true)
    {
        if (string.IsNullOrEmpty(kana)) {
            return Array.Empty<string>();
        }

        string key = Kana.ToHiragana(kana);
        Dictionary<(int, int), List<Match>> spans = await MatchSpansAsync(key, wholeOnly: true);
        spans.TryGetValue((0, key.Length), out List<Match>? matches);
        return Candidates(kana, matches, usePastChoices);
    }

    /// <summary>
    /// The word to remember when the user writes <paramref name="kana"/> as
    /// <paramref name="written"/>: the dictionary word behind it, so choosing 読んだ
    /// teaches 読む and よみます benefits too. A spelling no dictionary word
    /// explains (a katakana choice, say) is remembered as typed.
    /// </summary>
    public async Task<(string Reading, string Written)> ResolveChoiceAsync(string kana, string written)
    {
        string key = Kana.ToHiragana(kana);
        Dictionary<(int, int), List<Match>> spans = await MatchSpansAsync(key, wholeOnly: true);
        if (spans.TryGetValue((0, key.Length), out List<Match>? matches)) {
            foreach (Match m in matches) {
                if (string.Equals(m.Written, written, StringComparison.Ordinal)) {
                    return (m.Word.Reading, m.Word.Written);
                }
            }
        }
        return (key, written);
    }

    /// <summary>A dictionary word matched against a stretch of input, and how it is written there.</summary>
    private readonly record struct Match(DictionaryWord Word, string Written, bool Inflected = false);

    /// <summary>
    /// Every stretch [start, end) of the key that is a dictionary word, as typed or
    /// once deinflected, with the matching words. One lookup covers them all.
    /// </summary>
    private async Task<Dictionary<(int, int), List<Match>>> MatchSpansAsync(string key, bool wholeOnly = false)
    {
        Dictionary<(int, int), List<Deinflection>> forms = new();
        HashSet<string> readings = new(StringComparer.Ordinal);

        for (int start = 0; start < key.Length; start++) {
            for (int end = start + 1; end <= Math.Min(key.Length, start + MaxWordLength); end++) {
                if (wholeOnly && (start != 0 || end != key.Length)) {
                    continue;
                }

                string piece = key[start..end];
                List<Deinflection> options = new() { new Deinflection(piece, WordType.None) };
                options.AddRange(Deinflector.Deinflect(piece));
                forms[(start, end)] = options;
                foreach (Deinflection d in options) {
                    readings.Add(d.DictionaryForm);
                }
            }
        }

        IReadOnlyList<DictionaryWord> words = await _lookup.LookupAsync(readings);
        ILookup<string, DictionaryWord> byReading = words.ToLookup(w => w.Reading, StringComparer.Ordinal);

        Dictionary<(int, int), List<Match>> spans = new();
        foreach (((int start, int end), List<Deinflection> options) in forms) {
            string piece = key[start..end];
            List<Match> matches = new();
            foreach (Deinflection d in options) {
                bool asTyped = d.Types == WordType.None;
                foreach (DictionaryWord word in byReading[d.DictionaryForm]) {
                    if (asTyped) {
                        matches.Add(new Match(word, word.Written));
                    } else if ((word.Type & d.Types) != 0
                        && Deinflector.Reinflect(word.Written, word.Reading, piece) is string written) {
                        matches.Add(new Match(word, written, Inflected: true));
                    }
                }
            }

            if (matches.Count > 0) {
                spans[(start, end)] = matches;
            }
        }
        return spans;
    }

    /// <summary>
    /// Order a segment's candidates: spellings the user chose before (most recent
    /// first), then dictionary spellings by rank, then the typed kana and its other
    /// script. Without a past choice, the typed kana goes first for particles and
    /// single kana. Words usually written in kana need no special case: their kana
    /// spelling already ranks first (ある before 有る, コーヒー before 珈琲).
    /// </summary>
    private static IReadOnlyList<string> Candidates(string typed, List<Match>? matches, bool usePastChoices)
    {
        string hiragana = Kana.ToHiragana(typed);
        string katakana = Kana.ToKatakana(typed);

        DateTime? Chosen(Match m) => usePastChoices ? m.Word.LastChosen : null;

        List<Match> ordered = (matches ?? new List<Match>())
            .OrderByDescending(m => Chosen(m) ?? DateTime.MinValue)
            .ThenBy(m => m.Word.Rank)
            .ThenBy(m => m.Word.Order)
            .ToList();

        bool kanaFirst = (ordered.Count == 0 || Chosen(ordered[0]) is null)
            && (typed.Length == 1 || ordered.Count == 0 || ordered.All(m => m.Word.ParticleLike));

        List<string> result = new();
        void Add(string s) {
            if (!result.Contains(s, StringComparer.Ordinal)) {
                result.Add(s);
            }
        }

        if (kanaFirst) {
            Add(typed);
        }
        foreach (Match m in ordered) {
            Add(m.Written);
        }
        Add(typed);
        Add(hiragana);
        Add(katakana);
        return result;
    }
}
