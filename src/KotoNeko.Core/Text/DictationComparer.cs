using System.Text;

namespace KotoNeko.Core.Text;

/// <summary>How one stretch of a dictation compares with what was said.</summary>
public enum DiffKind
{
    /// <summary>Heard right.</summary>
    Same,
    /// <summary>Said, but not typed.</summary>
    Missing,
    /// <summary>Typed, but not said.</summary>
    Extra,
    /// <summary>Typed something else in its place.</summary>
    Wrong,
}

/// <summary>A stretch of the comparison: the expected kana and what was typed for it.</summary>
public sealed record DiffPart(DiffKind Kind, string Expected, string Typed);

/// <summary>
/// The result of comparing a dictation by sound. <see cref="Score"/> is the share
/// of the expected kana heard correctly (0–1). <see cref="ParticleNotes"/> lists
/// particles typed as they sound (わ for は): accepted, but worth knowing.
/// </summary>
public sealed record DictationComparison(
    double Score,
    int Errors,
    IReadOnlyList<DiffPart> Parts,
    IReadOnlyList<string> ParticleNotes);

/// <summary>A word where what was typed sounds right but is written differently.</summary>
public enum KanjiNoteKind
{
    /// <summary>Written in kana where the original uses kanji. Fine for dictation.</summary>
    WrittenInKana,
    /// <summary>A different kanji spelling for the same sound (合う for 会う).</summary>
    DifferentKanji,
}

public sealed record KanjiNote(KanjiNoteKind Kind, string Expected, string Typed, string Reading);

/// <summary>
/// Compares a dictation with the sentence that was played. The sound comparison
/// is done in hiragana with punctuation and spaces removed, so 食べる and たべる
/// both count; the written comparison is advisory and never affects the score.
/// </summary>
public static class DictationComparer
{
    // A particle written one way and pronounced another: は is heard as わ, and so on.
    private static readonly Dictionary<char, char> ParticleSounds = new() { ['は'] = 'わ', ['へ'] = 'え', ['を'] = 'お' };

    /// <summary>
    /// What a sentence sounds like, for <see cref="Compare"/>: its kana (normalised),
    /// and which of those kana are particles. <paramref name="particleChars"/> holds
    /// the positions in the written text of single-kana particles (は, へ, を).
    /// </summary>
    public static (string Kana, HashSet<int> Particles) Speech(IEnumerable<FuriganaPiece> pieces, ISet<int> particleChars)
    {
        StringBuilder kana = new();
        HashSet<int> particles = new();
        int position = 0;
        foreach (FuriganaPiece p in pieces) {
            if (p.Reading is not null) {
                kana.Append(Normalize(p.Reading));
            } else {
                for (int i = 0; i < p.Text.Length; i++) {
                    char c = Kana.ToHiragana(p.Text[i].ToString())[0];
                    if (!Kana.IsKana(c)) {
                        continue;
                    }
                    if (particleChars.Contains(position + i)) {
                        particles.Add(kana.Length);
                    }
                    kana.Append(c);
                }
            }
            position += p.Text.Length;
        }
        return (kana.ToString(), particles);
    }

    /// <summary>Hiragana with everything that isn't kana removed (punctuation, spaces, symbols).</summary>
    public static string Normalize(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char c in Kana.ToHiragana(text)) {
            if (Kana.IsKana(c)) {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Compare what was typed with what was said, both as kana.
    /// <paramref name="particleAt"/> marks positions in <paramref name="expected"/>
    /// (after normalising) that are particles, where は/へ/を may be typed as they sound.
    /// </summary>
    public static DictationComparison Compare(string expected, string typed, ISet<int>? particleAt = null)
    {
        string e = Normalize(expected);
        string t = Normalize(typed);
        particleAt ??= new HashSet<int>();

        bool Equal(int i, int j) =>
            e[i] == t[j] || (particleAt.Contains(i) && ParticleSounds.TryGetValue(e[i], out char sound) && sound == t[j]);

        // Edit distance, keeping the table to walk back the alignment.
        int[,] d = new int[e.Length + 1, t.Length + 1];
        for (int i = 0; i <= e.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= t.Length; j++) d[0, j] = j;
        for (int i = 1; i <= e.Length; i++) {
            for (int j = 1; j <= t.Length; j++) {
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + (Equal(i - 1, j - 1) ? 0 : 1));
            }
        }

        List<(DiffKind Kind, char? E, char? T)> steps = new();
        List<string> particleNotes = new();
        for (int i = e.Length, j = t.Length; i > 0 || j > 0;) {
            if (i > 0 && j > 0 && d[i, j] == d[i - 1, j - 1] + (Equal(i - 1, j - 1) ? 0 : 1)) {
                bool same = Equal(i - 1, j - 1);
                if (same && e[i - 1] != t[j - 1]) {
                    particleNotes.Add($"{e[i - 1]} as {t[j - 1]}");
                }
                steps.Add((same ? DiffKind.Same : DiffKind.Wrong, e[i - 1], t[j - 1]));
                i--; j--;
            } else if (i > 0 && d[i, j] == d[i - 1, j] + 1) {
                steps.Add((DiffKind.Missing, e[i - 1], null));
                i--;
            } else {
                steps.Add((DiffKind.Extra, null, t[j - 1]));
                j--;
            }
        }
        steps.Reverse();
        particleNotes.Reverse();

        // Merge single-character steps into runs of the same kind.
        List<DiffPart> parts = new();
        foreach (var (kind, ec, tc) in steps) {
            string es = ec?.ToString() ?? "";
            string ts = tc?.ToString() ?? "";
            if (parts.Count > 0 && parts[^1].Kind == kind) {
                DiffPart last = parts[^1];
                parts[^1] = last with { Expected = last.Expected + es, Typed = last.Typed + ts };
            } else {
                parts.Add(new DiffPart(kind, es, ts));
            }
        }

        int errors = d[e.Length, t.Length];
        double score = e.Length == 0 ? (t.Length == 0 ? 1 : 0) : Math.Max(0, 1.0 - (double)errors / e.Length);
        return new DictationComparison(score, errors, parts, particleNotes);
    }

    /// <summary>
    /// Compare how words were written, for words that were heard right: each word of
    /// the original with kanji is paired with a typed word of the same reading.
    /// Pairs written differently become notes; words not heard are left to the sound
    /// comparison.
    /// </summary>
    public static IReadOnlyList<KanjiNote> CompareWriting(
        IEnumerable<(string Text, string Reading)> expectedWords,
        IEnumerable<(string Text, string Reading)> typedWords)
    {
        List<(string Text, string Reading)> typed = typedWords
            .Select(w => (w.Text, Normalize(w.Reading)))
            .ToList();
        List<KanjiNote> notes = new();

        foreach ((string text, string reading) in expectedWords) {
            if (!text.Any(Kana.IsKanji)) {
                continue;
            }

            string key = Normalize(reading);
            int match = typed.FindIndex(w => w.Reading == key);
            if (match < 0) {
                continue;
            }

            string written = typed[match].Text;
            typed.RemoveAt(match);
            if (written == text) {
                continue;
            }
            notes.Add(new KanjiNote(
                written.Any(Kana.IsKanji) ? KanjiNoteKind.DifferentKanji : KanjiNoteKind.WrittenInKana,
                text, written, reading));
        }
        return notes;
    }
}
