using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Text;

/// <summary>How a dictation session mixes words' sentences with grammar sentences (Phase 6b).</summary>
public static class DictationMix
{
    /// <summary>The grammar sentences wanted in a session of <paramref name="count"/>: up to a third.</summary>
    public static int GrammarShare(int count) => Math.Max(0, count) / 3;

    /// <summary>The casual versions wanted in a session of <paramref name="count"/>: up to a fifth.</summary>
    public static int CasualShare(int count) => Math.Max(0, count) / 5;

    /// <summary>Your own sentences first (compositions, then written by hand), drafted examples last.</summary>
    public static int SourceOrder(GrammarExampleSource source) => source switch {
        GrammarExampleSource.Composition => 0,
        GrammarExampleSource.Yours => 1,
        _ => 2,
    };

    /// <summary>
    /// The two lists as one, in their own orders, with the second spread evenly through
    /// the first (words, grammar, words, words, grammar… rather than all grammar at the end).
    /// </summary>
    public static List<T> Spread<T>(IReadOnlyList<T> main, IReadOnlyList<T> spread)
    {
        List<T> result = new(main.Count + spread.Count);
        int total = main.Count + spread.Count;
        int m = 0;
        int s = 0;
        for (int i = 0; i < total; i++) {
            // Place the next spread item once its even share of the positions so far is due.
            bool takeSpread = s < spread.Count && (m >= main.Count || (long)(s + 1) * total <= (long)(i + 1) * spread.Count);
            result.Add(takeSpread ? spread[s++] : main[m++]);
        }
        return result;
    }
}
