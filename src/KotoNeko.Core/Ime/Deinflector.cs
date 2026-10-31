namespace KotoNeko.Core.Ime;

/// <summary>
/// How a dictionary word inflects. Also used as a set of possibilities while
/// deinflecting, so the values are flags.
/// </summary>
[Flags]
public enum WordType
{
    /// <summary>Does not inflect (nouns, particles, adverbs, ...).</summary>
    None = 0,
    /// <summary>Ichidan verb (食べる).</summary>
    Ichidan = 1,
    /// <summary>Godan verb (書く); the row comes from the last kana.</summary>
    Godan = 2,
    /// <summary>来る and compounds ending in 来る.</summary>
    Kuru = 4,
    /// <summary>する and する-verbs (勉強する).</summary>
    Suru = 8,
    /// <summary>い-adjective (高い).</summary>
    IAdjective = 16,
    /// <summary>Intermediate only: a te-form, as left by ている → て.</summary>
    TeForm = 32,
}

/// <summary>One way an inflected kana string could have come from a dictionary form.</summary>
/// <param name="DictionaryForm">The candidate dictionary form, in hiragana.</param>
/// <param name="Types">Which word types that dictionary form would have to be.</param>
public readonly record struct Deinflection(string DictionaryForm, WordType Types);

/// <summary>
/// Turns inflected kana back into possible dictionary forms (たべなかった → たべる,
/// a い-adjective step then an ichidan step). Rules are suffix replacements that
/// chain: a rule applies to the original text, or to a form whose types overlap
/// the rule's input types. Results are only possibilities; the caller keeps the
/// ones a dictionary entry of a matching type confirms.
/// </summary>
public static class Deinflector
{
    private const int MaxSteps = 6;

    private readonly record struct Rule(string From, string To, WordType In, WordType Out);

    /// <summary>All dictionary forms the text could deinflect to, not including itself.</summary>
    public static IReadOnlyList<Deinflection> Deinflect(string kana)
    {
        List<Deinflection> results = new();
        HashSet<(string, WordType)> seen = new();
        Queue<(string Term, WordType Types, bool Initial, int Steps)> queue = new();
        queue.Enqueue((kana, WordType.None, true, 0));

        while (queue.Count > 0)
        {
            var (term, types, initial, steps) = queue.Dequeue();
            if (steps >= MaxSteps)
            {
                continue;
            }

            foreach (Rule rule in Rules)
            {
                if (!term.EndsWith(rule.From, StringComparison.Ordinal)
                    || (!initial && (rule.In & types) == 0))
                {
                    continue;
                }

                string next = term[..^rule.From.Length] + rule.To;
                if (next.Length == 0 || !seen.Add((next, rule.Out)))
                {
                    continue;
                }

                // An intermediate te-form is not a dictionary form; keep going from it.
                if (rule.Out != WordType.TeForm)
                {
                    results.Add(new Deinflection(next, rule.Out));
                }
                queue.Enqueue((next, rule.Out, false, steps + 1));
            }
        }

        return results;
    }

    /// <summary>
    /// Apply to <paramref name="written"/> the same change that turns
    /// <paramref name="dictionaryKana"/> into <paramref name="inflectedKana"/>
    /// (読む + よむ → よんだ gives 読んだ). Null when the change does not fall in the
    /// written form's kana ending.
    /// </summary>
    public static string? Reinflect(string written, string dictionaryKana, string inflectedKana)
    {
        int common = 0;
        while (common < dictionaryKana.Length && common < inflectedKana.Length
            && dictionaryKana[common] == inflectedKana[common])
        {
            common++;
        }

        string dictTail = dictionaryKana[common..];
        string inflectedTail = inflectedKana[common..];

        if (written.EndsWith(dictTail, StringComparison.Ordinal))
        {
            return written[..^dictTail.Length] + inflectedTail;
        }

        // 来る: the kanji covers く/き/こ, so the kana change starts inside it.
        if (dictTail.EndsWith("くる", StringComparison.Ordinal)
            && written.EndsWith("来る", StringComparison.Ordinal)
            && inflectedTail.Length > 0 && inflectedTail[0] is 'き' or 'こ' or 'く')
        {
            string head = dictTail[..^2];
            string writtenHead = written[..^2];
            if (inflectedTail.StartsWith(head, StringComparison.Ordinal))
            {
                return writtenHead + head + "来" + inflectedTail[(head.Length + 1)..];
            }
        }

        return null;
    }

    // Polite endings that attach to the masu stem.
    private static readonly string[] Polite = { "ます", "ました", "ません", "ませんでした", "ましょう", "まして" };

    // Godan rows: dictionary ending, a-, i-, e-, o-stem kana, te-form, ta-form.
    private static readonly (string U, string A, string I, string E, string O, string Te, string Ta)[] GodanRows =
    {
        ("う", "わ", "い", "え", "お", "って", "った"),
        ("く", "か", "き", "け", "こ", "いて", "いた"),
        ("ぐ", "が", "ぎ", "げ", "ご", "いで", "いだ"),
        ("す", "さ", "し", "せ", "そ", "して", "した"),
        ("つ", "た", "ち", "て", "と", "って", "った"),
        ("ぬ", "な", "に", "ね", "の", "んで", "んだ"),
        ("ぶ", "ば", "び", "べ", "ぼ", "んで", "んだ"),
        ("む", "ま", "み", "め", "も", "んで", "んだ"),
        ("る", "ら", "り", "れ", "ろ", "って", "った"),
    };

    // Declared after the tables it is built from: static fields initialise in order.
    private static readonly Rule[] Rules = BuildRules();

    private static Rule[] BuildRules()
    {
        const WordType V1 = WordType.Ichidan, V5 = WordType.Godan, VK = WordType.Kuru,
            VS = WordType.Suru, Adj = WordType.IAdjective, Te = WordType.TeForm, Any = WordType.None;
        List<Rule> r = new();

        // Past tense, plus the conditional (たら) and listing (たり) forms built on it.
        void Past(string from, string to, WordType output)
        {
            r.Add(new(from, to, Any, output));
            r.Add(new(from + "ら", to, Any, output));
            r.Add(new(from + "り", to, Any, output));
        }

        // A verb class: its endings are given relative to the stem that replaces `dict`.
        void Verb(string dict, WordType type, string ta, string te, string nai, string masuStem,
            string vol, string ba, string[] imperative, string[] makesIchidan)
        {
            Past(ta, dict, type);
            r.Add(new(te, dict, Te, type));
            r.Add(new(nai, dict, Adj, type));
            r.Add(new(masuStem + "たい", dict, Adj, type));
            foreach (string p in Polite)
            {
                r.Add(new(masuStem + p, dict, Any, type));
            }
            r.Add(new(vol, dict, Any, type));
            r.Add(new(ba, dict, Any, type));
            foreach (string i in imperative)
            {
                r.Add(new(i, dict, Any, type));
            }
            // Negative imperative: the dictionary form plus な (食べるな).
            r.Add(new(dict + "な", dict, Any, type));
            // Potential, passive, causative and causative-passive are ichidan verbs.
            foreach (string derived in makesIchidan)
            {
                r.Add(new(derived, dict, V1, type));
            }
        }

        Verb("る", V1, "た", "て", "ない", "", "よう", "れば", new[] { "ろ", "よ" },
            new[] { "られる", "れる", "させる", "させられる" });

        foreach (var g in GodanRows)
        {
            Verb(g.U, V5, g.Ta, g.Te, g.A + "ない", g.I, g.O + "う", g.E + "ば", new[] { g.E },
                new[] { g.E + "る", g.A + "れる", g.A + "せる", g.A + "せられる", g.A + "される" });
        }

        // 行く's te- and ta-forms are irregular.
        Past("いった", "いく", V5);
        r.Add(new("いって", "いく", Te, V5));

        Verb("くる", VK, "きた", "きて", "こない", "き", "こよう", "くれば", new[] { "こい" },
            new[] { "こられる", "これる", "こさせる", "こさせられる" });

        Verb("する", VS, "した", "して", "しない", "し", "しよう", "すれば", new[] { "しろ", "せよ" },
            new[] { "できる", "される", "させる", "させられる" });

        // い-adjectives.
        Past("かった", "い", Adj);
        r.Add(new("くない", "い", Adj, Adj));
        r.Add(new("くて", "い", Te, Adj));
        r.Add(new("ければ", "い", Any, Adj));
        r.Add(new("く", "い", Any, Adj));

        // Progressive ている / てる (and the で variants): an ichidan verb on a te-form.
        r.Add(new("ている", "て", V1, Te));
        r.Add(new("てる", "て", V1, Te));
        r.Add(new("でいる", "で", V1, Te));
        r.Add(new("でる", "で", V1, Te));

        // Casual contractions on a te-form, conjugating as godan verbs themselves:
        // ちゃう/じゃう for てしまう/でしまう (忘れちゃった), とく/どく for ておく/でおく (読んどいた).
        r.Add(new("ちゃう", "て", V5, Te));
        r.Add(new("じゃう", "で", V5, Te));
        r.Add(new("とく", "て", V5, Te));
        r.Add(new("どく", "で", V5, Te));

        // なきゃ / なくちゃ for なければ / なくては (行かなきゃ): back to the negative.
        r.Add(new("なきゃ", "ない", Any, Adj));
        r.Add(new("なくちゃ", "ない", Any, Adj));

        return r.ToArray();
    }
}
