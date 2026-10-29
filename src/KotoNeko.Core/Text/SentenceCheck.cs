namespace KotoNeko.Core.Text;

/// <summary>A word of a sentence, as the reading engine split it.</summary>
/// <param name="Lemma">Its dictionary form, or null when the dictionary didn't recognise it.</param>
public readonly record struct CheckWord(int Start, int Length, string? Lemma, bool IsParticle);

/// <summary>A word in a sentence that the user doesn't know (yet).</summary>
/// <param name="Lemma">Its dictionary form, or null when the dictionary didn't recognise it.</param>
public sealed record UnknownWord(int Start, int Length, string Text, string? Lemma);

/// <summary>How usable a sentence is for practising its word by ear.</summary>
/// <param name="KanaLength">Spoken length in kana (punctuation and spaces don't count).</param>
/// <param name="TargetStart">Where the word being practised is (−1 when it isn't found).</param>
/// <param name="Unknown">Words other than the target the user doesn't know.</param>
public sealed record SentenceCheckResult(int KanaLength, int TargetStart, int TargetLength, IReadOnlyList<UnknownWord> Unknown)
{
    public bool HasTarget => TargetStart >= 0;

    /// <summary>Every word besides the target is known.</summary>
    public bool Comprehensible => Unknown.Count == 0;

    /// <summary>Fit for a listening question: the target is in it, it's all known, and it's short.</summary>
    public bool UsableForListening => HasTarget && Comprehensible && KanaLength <= SentenceCheck.ListeningMaxKana;
}

/// <summary>
/// The comprehensibility check: in a practice sentence only the ear is being
/// tested, so every word but the target must be one the user knows. Particles,
/// punctuation and a few basic function words are always fine.
/// </summary>
public static class SentenceCheck
{
    /// <summary>The longest sentence a listening question plays, in spoken kana.</summary>
    public const int ListeningMaxKana = 20;

    // Function words every sentence leans on; the dictionary doesn't always tag them as particles.
    private static readonly HashSet<string> AlwaysKnown = new(StringComparer.Ordinal) {
        "する", "ある", "いる", "なる", "です", "だ", "ます", "ない", "こと", "もの", "の", "ん", "よう", "そう",
    };

    /// <summary>
    /// "Is this word known?" for a set of known words: by spelling, or, for a word
    /// written in kana, by a known word's reading (わかる for 分かる).
    /// </summary>
    public static Func<string, bool> Knows(IEnumerable<(string Written, string Reading)> words)
    {
        HashSet<string> spellings = new(StringComparer.Ordinal);
        HashSet<string> sounds = new(StringComparer.Ordinal);
        foreach ((string written, string reading) in words) {
            spellings.Add(written);
            sounds.Add(Kana.ToHiragana(reading));
        }
        return word => spellings.Contains(word) || (Kana.IsAllKana(word) && sounds.Contains(Kana.ToHiragana(word)));
    }

    /// <summary>A sentence's spoken length: the kana of its readings, normalised as dictation compares them.</summary>
    public static int KanaLength(IEnumerable<FuriganaPiece> pieces) =>
        DictationComparer.Normalize(Furigana.KanaText(pieces)).Length;

    /// <summary>
    /// Where <paramref name="target"/> is in the sentence: a word whose dictionary
    /// form is the target (so 読んだ is found for 読む), else the target as written.
    /// </summary>
    public static (int Start, int Length) FindTarget(string text, IEnumerable<CheckWord> words, string target)
    {
        foreach (CheckWord w in words) {
            if (w.Lemma == target) {
                return (w.Start, w.Length);
            }
        }
        int at = text.IndexOf(target, StringComparison.Ordinal);
        return at >= 0 ? (at, target.Length) : (-1, 0);
    }

    /// <summary>
    /// Check a sentence for practising <paramref name="target"/>.
    /// <paramref name="isKnown"/> says whether the user knows a word, by dictionary form.
    /// Unrecognised runs of kana or kanji count as one unknown word each.
    /// </summary>
    public static SentenceCheckResult Check(
        string text, IReadOnlyList<FuriganaPiece> pieces, IReadOnlyList<CheckWord> words, string target,
        Func<string, bool> isKnown)
    {
        (int targetStart, int targetLength) = FindTarget(text, words, target);
        int targetEnd = targetStart + targetLength;

        List<UnknownWord> unknown = new();
        foreach (CheckWord w in words.OrderBy(w => w.Start)) {
            if (targetStart >= 0 && w.Start < targetEnd && w.Start + w.Length > targetStart) {
                continue;
            }
            string piece = SafeSlice(text, w.Start, w.Length);
            if (w.IsParticle || !piece.Any(c => Kana.IsKana(c) || Kana.IsKanji(c))) {
                continue;
            }
            if (w.Lemma is string lemma && (lemma == target || AlwaysKnown.Contains(lemma) || isKnown(lemma))) {
                continue;
            }

            // Consecutive unrecognised characters (an unknown loanword, say) are one word.
            if (w.Lemma is null && unknown.Count > 0 && unknown[^1] is { Lemma: null } last
                && last.Start + last.Length == w.Start) {
                unknown[^1] = last with { Length = last.Length + w.Length, Text = last.Text + piece };
                continue;
            }
            unknown.Add(new UnknownWord(w.Start, w.Length, piece, w.Lemma));
        }

        return new SentenceCheckResult(KanaLength(pieces), targetStart, targetLength, unknown);
    }

    private static string SafeSlice(string text, int start, int length) =>
        start < 0 || start >= text.Length ? string.Empty : text.Substring(start, Math.Min(length, text.Length - start));
}

/// <summary>
/// The dictation length ladder: how long a sentence may be for full dictation.
/// It starts at 15 kana, moves up a rung after a run of scores of 90% or more,
/// and back down after repeated misses. Only full (not partial) attempts count.
/// </summary>
public static class DictationLadder
{
    /// <summary>The rungs, in spoken kana.</summary>
    public static readonly IReadOnlyList<int> Rungs = new[] { 15, 20, 25, 30, 40, 50 };

    /// <summary>Consecutive scores of at least <see cref="GoodScore"/> to move up.</summary>
    public const int UpAfter = 5;
    public const double GoodScore = 0.9;

    /// <summary>Consecutive scores below <see cref="MissScore"/> to move down.</summary>
    public const int DownAfter = 3;
    public const double MissScore = 0.6;

    /// <summary>The current limit, from full attempts' scores, oldest first.</summary>
    public static int Level(IEnumerable<double> scoresOldestFirst)
    {
        int rung = 0, good = 0, missed = 0;
        foreach (double score in scoresOldestFirst) {
            good = score >= GoodScore ? good + 1 : 0;
            missed = score < MissScore ? missed + 1 : 0;
            if (good >= UpAfter && rung < Rungs.Count - 1) {
                rung++;
                good = 0;
            } else if (missed >= DownAfter && rung > 0) {
                rung--;
                missed = 0;
            }
        }
        return Rungs[rung];
    }
}
