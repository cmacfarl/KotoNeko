using KotoNeko.Core.Text;

namespace KotoNeko.Core.Ime;

/// <summary>A word found in a sentence.</summary>
/// <param name="Start">Position of the word's first character in the sentence.</param>
/// <param name="Text">The word as it appears (inflected if it was).</param>
/// <param name="Reading">Its reading in hiragana when it contains kanji; null for kana, punctuation and unknown kanji.</param>
/// <param name="Lemma">Its dictionary form (読む for 読んだ), or null when not recognised.</param>
/// <param name="IsParticle">The word is a particle (は, へ, を, ...), which matters for how it sounds.</param>
public sealed record SentenceWord(int Start, string Text, string? Reading, string? Lemma, bool IsParticle = false);

/// <summary>
/// Works out readings for written Japanese, offline, from the dictionary: the
/// sentence is split into words by the same minimum-cost path as the converter,
/// matching spellings (and inflected spellings) instead of readings. Each word
/// takes the reading of its best match: the user's own vocabulary first, then
/// common forms. Ambiguous words (今日, 方) can get the wrong reading, which is
/// why stored readings are editable.
/// </summary>
public sealed class SentenceReader
{
    private const double KnownWordCost = 1.0;
    private const double UncommonWordCost = 0.3;

    // Ranks below this are common words. The kana spelling of a common kanji word
    // ranks a little lower than its kanji (5), but in kana text it is still a
    // common word: しんぶん must beat a split like し + んぶん.
    private const int UncommonRank = 10;
    private const double UnknownCharCost = 5.0;
    private const double PunctuationCost = 0.1;
    // Particles follow words; one at the very start (に|ほんの) is almost never right.
    private const double ParticleFirstCost = 0.5;
    // An inflected match (解こう from 解く) costs a little more than a word found as
    // is, so と|こうえん beats とこう|えん when the split is otherwise a tie.
    private const double InflectedCost = 0.05;

    private readonly IDictionaryLookup _lookup;

    public SentenceReader(IDictionaryLookup lookup)
    {
        _lookup = lookup;
    }

    /// <summary>Split <paramref name="text"/> into words with readings.</summary>
    public async Task<IReadOnlyList<SentenceWord>> ReadAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) {
            return Array.Empty<SentenceWord>();
        }

        Dictionary<(int, int), Match> spans = await MatchSpansAsync(text);

        int n = text.Length;
        double[] cost = new double[n + 1];
        int[] from = new int[n + 1];
        Array.Fill(cost, double.MaxValue);
        cost[0] = 0;

        for (int end = 1; end <= n; end++) {
            for (int start = Math.Max(0, end - KanaKanjiConverter.MaxWordLength); start < end; start++) {
                if (cost[start] == double.MaxValue) {
                    continue;
                }

                double step;
                if (spans.TryGetValue((start, end), out Match m)) {
                    step = KnownWordCost + (m.Word.Rank >= UncommonRank && !m.Word.FromVocabulary ? UncommonWordCost : 0)
                        + (m.Inflected ? InflectedCost : 0)
                        + (start == 0 && m.Word.ParticleLike ? ParticleFirstCost : 0);
                } else if (end - start == 1) {
                    step = IsWordChar(text[start]) ? UnknownCharCost : PunctuationCost;
                } else {
                    continue;
                }

                if (cost[start] + step < cost[end]) {
                    cost[end] = cost[start] + step;
                    from[end] = start;
                }
            }
        }

        List<SentenceWord> words = new();
        for (int end = n; end > 0; end = from[end]) {
            int start = from[end];
            string piece = text[start..end];
            if (spans.TryGetValue((start, end), out Match m)) {
                bool hasKanji = piece.Any(Kana.IsKanji);
                words.Add(new SentenceWord(start, piece, hasKanji ? m.Reading : null, m.Lemma, m.Word.ParticleLike));
            } else {
                words.Add(new SentenceWord(start, piece, null, null));
            }
        }
        words.Reverse();
        return words;
    }

    /// <summary>
    /// The sentence as furigana pieces: each word's reading lined up over its own
    /// kanji. Kanji with no known reading are left bare.
    /// </summary>
    public static IReadOnlyList<FuriganaPiece> ToFurigana(IEnumerable<SentenceWord> words)
    {
        List<FuriganaPiece> pieces = new();
        foreach (SentenceWord w in words) {
            if (w.Reading is null) {
                pieces.Add(new FuriganaPiece(w.Text, null));
            } else {
                pieces.AddRange(Furigana.Align(w.Text, w.Reading));
            }
        }
        return pieces;
    }

    private static bool IsWordChar(char c) => Kana.IsKana(c) || Kana.IsKanji(c);

    private readonly record struct Match(DictionaryWord Word, string Reading, string Lemma, bool Inflected = false);

    /// <summary>A spelling the text could be: the dictionary form and the types it would need.</summary>
    private readonly record struct Form(string Written, WordType Types, string? KuruKana, int KuruAt);

    /// <summary>The best dictionary match for every stretch of the text that is a word.</summary>
    private async Task<Dictionary<(int, int), Match>> MatchSpansAsync(string text)
    {
        Dictionary<(int, int), List<Form>> forms = new();
        HashSet<string> spellings = new(StringComparer.Ordinal);

        for (int start = 0; start < text.Length; start++) {
            for (int end = start + 1; end <= Math.Min(text.Length, start + KanaKanjiConverter.MaxWordLength); end++) {
                string piece = text[start..end];
                if (!piece.All(IsWordChar)) {
                    break;
                }

                List<Form> options = new() { new Form(piece, WordType.None, null, -1) };
                foreach (Deinflection d in Deinflector.Deinflect(piece)) {
                    options.Add(new Form(d.DictionaryForm, d.Types, null, -1));
                }

                // 来る: the kanji stands for く/き/こ, which the kana rules can't see.
                // Try each sound in its place; a match must end in 来る and be a kuru verb.
                int kuru = piece.LastIndexOf('来');
                if (kuru >= 0) {
                    foreach (char sound in "きこく") {
                        string kana = piece[..kuru] + sound + piece[(kuru + 1)..];
                        foreach (Deinflection d in Deinflector.Deinflect(kana)) {
                            if ((d.Types & WordType.Kuru) != 0 && d.DictionaryForm.EndsWith("くる", StringComparison.Ordinal)
                                && d.DictionaryForm.Length - 2 == kuru) {
                                options.Add(new Form(piece[..kuru] + "来る", WordType.Kuru, kana[kuru..], kuru));
                            }
                        }
                    }
                }

                forms[(start, end)] = options;
                foreach (Form f in options) {
                    spellings.Add(f.Written);
                }
            }
        }

        IReadOnlyList<DictionaryWord> words = await _lookup.LookupWrittenAsync(spellings);
        ILookup<string, DictionaryWord> byWritten = words.ToLookup(w => w.Written, StringComparer.Ordinal);

        Dictionary<(int, int), Match> spans = new();
        foreach (((int start, int end), List<Form> options) in forms) {
            string piece = text[start..end];
            List<Match> matches = new();
            foreach (Form f in options) {
                bool asWritten = f.Types == WordType.None;
                foreach (DictionaryWord word in byWritten[f.Written]) {
                    if (asWritten) {
                        matches.Add(new Match(word, word.Reading, word.Written));
                    } else if ((word.Type & f.Types) == 0) {
                        continue;
                    } else if (f.KuruKana is not null) {
                        // The reading up to 来 is unchanged; from 来 on, it is the kana tried.
                        if (word.Reading.EndsWith("くる", StringComparison.Ordinal)) {
                            matches.Add(new Match(word, word.Reading[..^2] + f.KuruKana, word.Written, Inflected: true));
                        }
                    } else if (Deinflector.Reinflect(word.Reading, word.Written, piece) is string reading) {
                        matches.Add(new Match(word, reading, word.Written, Inflected: true));
                    }
                }
            }

            if (matches.Count > 0) {
                spans[(start, end)] = matches
                    .OrderByDescending(m => m.Word.FromVocabulary)
                    .ThenBy(m => m.Inflected)
                    .ThenBy(m => m.Word.Rank)
                    .ThenBy(m => m.Word.Order)
                    .First();
            }
        }
        return spans;
    }
}
