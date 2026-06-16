using System.Globalization;
using System.Text;

namespace KotoNeko.Core.Text;

/// <summary>The outcome of grading a typed answer.</summary>
public enum AnswerJudgment
{
    Correct,
    Incorrect,

    /// <summary>
    /// The answer is close but not accepted (e.g. a near-miss typo, or incomplete
    /// romaji for a reading). The UI should shake and let the user retype rather
    /// than mark the item wrong — mirrors WaniKani's behaviour.
    /// </summary>
    CloseTryAgain,
}

/// <summary>
/// Grades typed meaning and reading answers. Meaning matching tolerates minor
/// typos; reading matching expects exact kana (after romaji conversion).
/// </summary>
public static class AnswerMatcher
{
    private static readonly string[] LeadingArticles = { "to ", "the ", "a ", "an " };

    /// <summary>
    /// Grade an English meaning answer against the primary meaning plus any
    /// alternates (a ';'-separated list).
    /// </summary>
    public static AnswerJudgment GradeMeaning(string typed, string primary, string? alternates)
    {
        string normalizedTyped = NormalizeMeaning(typed);
        if (normalizedTyped.Length == 0)
        {
            return AnswerJudgment.CloseTryAgain;
        }

        IEnumerable<string> accepted = EnumerateAnswers(primary, alternates)
            .Select(NormalizeMeaning)
            .Where(static a => a.Length > 0);

        int best = int.MaxValue;
        foreach (string candidate in accepted)
        {
            if (candidate == normalizedTyped)
            {
                return AnswerJudgment.Correct;
            }

            int distance = Levenshtein(candidate, normalizedTyped);
            if (distance < best)
            {
                best = distance;
            }

            int tolerance = TypoTolerance(candidate.Length);
            if (distance <= tolerance)
            {
                return AnswerJudgment.Correct;
            }
        }

        // Just outside tolerance: treat as a near-miss worth retyping.
        return best <= TypoTolerance(normalizedTyped.Length) + 1
            ? AnswerJudgment.CloseTryAgain
            : AnswerJudgment.Incorrect;
    }

    /// <summary>
    /// Grade a reading answer. The typed value is first converted from romaji to
    /// kana, then compared exactly against the accepted readings. Incomplete romaji
    /// (leftover latin letters) yields <see cref="AnswerJudgment.CloseTryAgain"/>.
    /// </summary>
    public static AnswerJudgment GradeReading(string typed, string primary, string? alternates)
    {
        string kana = NormalizeKana(RomajiConverter.ToHiragana(typed));
        if (kana.Length == 0)
        {
            return AnswerJudgment.CloseTryAgain;
        }

        if (ContainsLatin(kana))
        {
            // Still partly romaji: the user hasn't finished a valid kana sequence.
            return AnswerJudgment.CloseTryAgain;
        }

        foreach (string candidate in EnumerateAnswers(primary, alternates))
        {
            if (NormalizeKana(candidate) == kana)
            {
                return AnswerJudgment.Correct;
            }
        }

        return AnswerJudgment.Incorrect;
    }

    /// <summary>
    /// Grade a conjugation answer (kana, exact match after romaji conversion and
    /// normalisation).
    /// </summary>
    public static AnswerJudgment GradeConjugation(string typed, string expected)
    {
        string kana = NormalizeKana(RomajiConverter.ToHiragana(typed));
        if (kana.Length == 0)
        {
            return AnswerJudgment.CloseTryAgain;
        }

        if (ContainsLatin(kana))
        {
            return AnswerJudgment.CloseTryAgain;
        }

        return NormalizeKana(expected) == kana
            ? AnswerJudgment.Correct
            : AnswerJudgment.Incorrect;
    }

    private static IEnumerable<string> EnumerateAnswers(string primary, string? alternates)
    {
        if (!string.IsNullOrWhiteSpace(primary))
        {
            yield return primary;
        }

        if (!string.IsNullOrWhiteSpace(alternates))
        {
            foreach (string part in alternates.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return part;
            }
        }
    }

    private static string NormalizeMeaning(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string lowered = value.Trim().ToLowerInvariant();

        // Strip surrounding punctuation and collapse internal whitespace.
        StringBuilder sb = new(lowered.Length);
        bool lastWasSpace = false;
        foreach (char c in lowered)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastWasSpace = false;
            }
            else if (char.IsWhiteSpace(c) || c == '-')
            {
                if (!lastWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            // other punctuation dropped
        }

        string cleaned = sb.ToString().Trim();

        foreach (string article in LeadingArticles)
        {
            if (cleaned.StartsWith(article, StringComparison.Ordinal) && cleaned.Length > article.Length)
            {
                cleaned = cleaned.Substring(article.Length);
                break;
            }
        }

        return cleaned;
    }

    private static string NormalizeKana(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        StringBuilder sb = new(trimmed.Length);
        foreach (char c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            // Fold katakana into hiragana so either input grades equally.
            if (c is >= 'ァ' and <= 'ヶ')
            {
                sb.Append((char)(c - 0x60));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static bool ContainsLatin(string value)
    {
        foreach (char c in value)
        {
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Allowed edit distance for a meaning of the given length.</summary>
    private static int TypoTolerance(int length) => length switch
    {
        <= 3 => 0,
        <= 6 => 1,
        <= 12 => 2,
        _ => 3,
    };

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
