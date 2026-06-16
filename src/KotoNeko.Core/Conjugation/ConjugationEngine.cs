using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Conjugation;

/// <summary>
/// Generates Japanese verb conjugations in kana from a dictionary-form reading and
/// a verb class. Covers the seven forms we quiz on (past, te-form, potential,
/// passive, causative, causative-passive, imperative) in affirmative and negative,
/// including the common irregulars 行く, ある, する and 来る.
/// </summary>
public static class ConjugationEngine
{
    private static readonly ConjugationForm[] AllForms =
    {
        ConjugationForm.Past,
        ConjugationForm.TeForm,
        ConjugationForm.Potential,
        ConjugationForm.Passive,
        ConjugationForm.Causative,
        ConjugationForm.CausativePassive,
        ConjugationForm.Imperative,
    };

    // Godan euphonic mappings: last kana -> (a-stem, i-stem, e-stem, te, ta).
    private static readonly Dictionary<char, GodanRow> Godan = new()
    {
        ['う'] = new GodanRow('わ', 'い', 'え', "って", "った"),
        ['く'] = new GodanRow('か', 'き', 'け', "いて", "いた"),
        ['ぐ'] = new GodanRow('が', 'ぎ', 'げ', "いで", "いだ"),
        ['す'] = new GodanRow('さ', 'し', 'せ', "して", "した"),
        ['つ'] = new GodanRow('た', 'ち', 'て', "って", "った"),
        ['ぬ'] = new GodanRow('な', 'に', 'ね', "んで", "んだ"),
        ['ぶ'] = new GodanRow('ば', 'び', 'べ', "んで", "んだ"),
        ['む'] = new GodanRow('ま', 'み', 'め', "んで", "んだ"),
        ['る'] = new GodanRow('ら', 'り', 'れ', "って", "った"),
    };

    /// <summary>
    /// Generate all 14 conjugations for the given reading and class. Returns an
    /// empty list for non-verbs or when the reading cannot be conjugated.
    /// </summary>
    public static IReadOnlyList<ConjugationResult> Generate(string reading, VerbClass verbClass)
    {
        if (verbClass == VerbClass.None || string.IsNullOrWhiteSpace(reading))
        {
            return Array.Empty<ConjugationResult>();
        }

        string dict = reading.Trim();
        List<ConjugationResult> results = new(AllForms.Length * 2);

        foreach (ConjugationForm form in AllForms)
        {
            string? affirmative = Conjugate(dict, verbClass, form, Polarity.Affirmative);
            string? negative = Conjugate(dict, verbClass, form, Polarity.Negative);

            if (affirmative is not null)
            {
                results.Add(new ConjugationResult(form, Polarity.Affirmative, affirmative));
            }

            if (negative is not null)
            {
                results.Add(new ConjugationResult(form, Polarity.Negative, negative));
            }
        }

        return results;
    }

    private static string? Conjugate(string dict, VerbClass verbClass, ConjugationForm form, Polarity polarity)
    {
        return verbClass switch
        {
            VerbClass.Ichidan => Ichidan(dict, form, polarity),
            VerbClass.Godan => GodanVerb(dict, form, polarity),
            VerbClass.Suru => Suru(dict, form, polarity),
            VerbClass.Kuru => Kuru(dict, form, polarity),
            _ => null,
        };
    }

    private static string Ichidan(string dict, ConjugationForm form, Polarity polarity)
    {
        // base = dictionary form without the trailing る.
        string b = dict.Length > 0 ? dict[..^1] : dict;

        return (form, polarity) switch
        {
            (ConjugationForm.Past, Polarity.Affirmative) => b + "た",
            (ConjugationForm.Past, Polarity.Negative) => b + "なかった",
            (ConjugationForm.TeForm, Polarity.Affirmative) => b + "て",
            (ConjugationForm.TeForm, Polarity.Negative) => b + "なくて",
            (ConjugationForm.Potential, Polarity.Affirmative) => b + "られる",
            (ConjugationForm.Potential, Polarity.Negative) => b + "られない",
            (ConjugationForm.Passive, Polarity.Affirmative) => b + "られる",
            (ConjugationForm.Passive, Polarity.Negative) => b + "られない",
            (ConjugationForm.Causative, Polarity.Affirmative) => b + "させる",
            (ConjugationForm.Causative, Polarity.Negative) => b + "させない",
            (ConjugationForm.CausativePassive, Polarity.Affirmative) => b + "させられる",
            (ConjugationForm.CausativePassive, Polarity.Negative) => b + "させられない",
            (ConjugationForm.Imperative, Polarity.Affirmative) => b + "ろ",
            (ConjugationForm.Imperative, Polarity.Negative) => dict + "な",
            _ => dict,
        };
    }

    private static string GodanVerb(string dict, ConjugationForm form, Polarity polarity)
    {
        char last = dict[^1];
        if (!Godan.TryGetValue(last, out GodanRow row))
        {
            // Not a recognised godan ending; return as-is rather than throw.
            return dict;
        }

        string b = dict[..^1];
        bool isIku = dict.EndsWith("いく", StringComparison.Ordinal) || dict == "く";
        string te = isIku ? "って" : row.Te;
        string ta = isIku ? "った" : row.Ta;

        // ある is irregular in the negative (ない, not あらない).
        bool isAru = dict == "ある";

        return (form, polarity) switch
        {
            (ConjugationForm.Past, Polarity.Affirmative) => b + ta,
            (ConjugationForm.Past, Polarity.Negative) => isAru ? "なかった" : b + row.A + "なかった",
            (ConjugationForm.TeForm, Polarity.Affirmative) => b + te,
            (ConjugationForm.TeForm, Polarity.Negative) => isAru ? "なくて" : b + row.A + "なくて",
            (ConjugationForm.Potential, Polarity.Affirmative) => b + row.E + "る",
            (ConjugationForm.Potential, Polarity.Negative) => b + row.E + "ない",
            (ConjugationForm.Passive, Polarity.Affirmative) => b + row.A + "れる",
            (ConjugationForm.Passive, Polarity.Negative) => b + row.A + "れない",
            (ConjugationForm.Causative, Polarity.Affirmative) => b + row.A + "せる",
            (ConjugationForm.Causative, Polarity.Negative) => b + row.A + "せない",
            (ConjugationForm.CausativePassive, Polarity.Affirmative) => b + row.A + "せられる",
            (ConjugationForm.CausativePassive, Polarity.Negative) => b + row.A + "せられない",
            (ConjugationForm.Imperative, Polarity.Affirmative) => b + row.E,
            (ConjugationForm.Imperative, Polarity.Negative) => dict + "な",
            _ => dict,
        };
    }

    private static string Suru(string dict, ConjugationForm form, Polarity polarity)
    {
        // prefix = everything before the trailing する (empty for bare する).
        string prefix = dict.EndsWith("する", StringComparison.Ordinal) ? dict[..^2] : dict;

        return (form, polarity) switch
        {
            (ConjugationForm.Past, Polarity.Affirmative) => prefix + "した",
            (ConjugationForm.Past, Polarity.Negative) => prefix + "しなかった",
            (ConjugationForm.TeForm, Polarity.Affirmative) => prefix + "して",
            (ConjugationForm.TeForm, Polarity.Negative) => prefix + "しなくて",
            (ConjugationForm.Potential, Polarity.Affirmative) => prefix + "できる",
            (ConjugationForm.Potential, Polarity.Negative) => prefix + "できない",
            (ConjugationForm.Passive, Polarity.Affirmative) => prefix + "される",
            (ConjugationForm.Passive, Polarity.Negative) => prefix + "されない",
            (ConjugationForm.Causative, Polarity.Affirmative) => prefix + "させる",
            (ConjugationForm.Causative, Polarity.Negative) => prefix + "させない",
            (ConjugationForm.CausativePassive, Polarity.Affirmative) => prefix + "させられる",
            (ConjugationForm.CausativePassive, Polarity.Negative) => prefix + "させられない",
            (ConjugationForm.Imperative, Polarity.Affirmative) => prefix + "しろ",
            (ConjugationForm.Imperative, Polarity.Negative) => prefix + "するな",
            _ => dict,
        };
    }

    private static string Kuru(string dict, ConjugationForm form, Polarity polarity)
    {
        // prefix = everything before the trailing くる (e.g. つれて in つれてくる).
        string prefix = dict.EndsWith("くる", StringComparison.Ordinal) ? dict[..^2] : string.Empty;

        return (form, polarity) switch
        {
            (ConjugationForm.Past, Polarity.Affirmative) => prefix + "きた",
            (ConjugationForm.Past, Polarity.Negative) => prefix + "こなかった",
            (ConjugationForm.TeForm, Polarity.Affirmative) => prefix + "きて",
            (ConjugationForm.TeForm, Polarity.Negative) => prefix + "こなくて",
            (ConjugationForm.Potential, Polarity.Affirmative) => prefix + "こられる",
            (ConjugationForm.Potential, Polarity.Negative) => prefix + "こられない",
            (ConjugationForm.Passive, Polarity.Affirmative) => prefix + "こられる",
            (ConjugationForm.Passive, Polarity.Negative) => prefix + "こられない",
            (ConjugationForm.Causative, Polarity.Affirmative) => prefix + "こさせる",
            (ConjugationForm.Causative, Polarity.Negative) => prefix + "こさせない",
            (ConjugationForm.CausativePassive, Polarity.Affirmative) => prefix + "こさせられる",
            (ConjugationForm.CausativePassive, Polarity.Negative) => prefix + "こさせられない",
            (ConjugationForm.Imperative, Polarity.Affirmative) => prefix + "こい",
            (ConjugationForm.Imperative, Polarity.Negative) => prefix + "くるな",
            _ => dict,
        };
    }

    private readonly record struct GodanRow(char A, char I, char E, string Te, string Ta);
}
