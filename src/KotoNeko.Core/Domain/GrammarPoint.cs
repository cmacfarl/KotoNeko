using System.Text.Json.Serialization;

namespace KotoNeko.Core.Domain;

/// <summary>Whether a grammar point's lesson content (or one of its examples) is ready to teach.</summary>
public enum GrammarContentStatus
{
    /// <summary>Drafted or being written: not taught until the user approves it.</summary>
    Draft = 0,

    /// <summary>The user approved it: lessons may use it.</summary>
    Approved = 1,
}

/// <summary>The register a grammar point belongs to.</summary>
public enum GrammarFormality
{
    /// <summary>Fine in any register.</summary>
    Neutral = 0,

    /// <summary>Conversation with friends and family.</summary>
    Casual = 1,

    /// <summary>Polite speech (です/ます).</summary>
    Polite = 2,

    /// <summary>Writing, announcements and formal speech.</summary>
    Formal = 3,
}

/// <summary>Where a grammar example came from.</summary>
public enum GrammarExampleSource
{
    /// <summary>Written by the user.</summary>
    Yours = 0,

    /// <summary>Drafted by an LLM and approved by the user.</summary>
    Generated = 1,

    /// <summary>The user's own sentence from a lesson's guided round, passed by the grader.</summary>
    Composition = 2,
}

/// <summary>A similar grammar point and how this one differs from it.</summary>
public sealed record GrammarContrast(
    [property: JsonPropertyName("pattern")] string Pattern,
    [property: JsonPropertyName("difference")] string Difference);

/// <summary>A typical learner mistake with the point, its correction and why.</summary>
public sealed record GrammarMistake(
    [property: JsonPropertyName("wrong")] string Wrong,
    [property: JsonPropertyName("right")] string Right,
    [property: JsonPropertyName("why")] string Why);

/// <summary>
/// An idea to get a sentence of your own started: a short topic to write about, and
/// a hint at the structure. Lessons and reviews offer these on request; the sentence
/// is still free composition, graded as written. (Points drafted before
/// grammar-content-v3 have full situations here, which work as ideas too.)
/// </summary>
public sealed record GrammarSituation(
    [property: JsonPropertyName("situation")] string Situation,
    [property: JsonPropertyName("hint")] string Hint);

/// <summary>
/// A grammar point as a study item: what it means and does, how it attaches, its
/// register, how it differs from similar points, common mistakes, example
/// sentences and the guided round's situations. Its pattern, meaning and JLPT level
/// stay on the grammar list entry it teaches. Lessons only use approved content;
/// grammar reviews arrive in Phase 5.
/// </summary>
public class GrammarPoint : StudyItem
{
    public const int MaxExplanationLength = 4000;
    public const int MaxFormationLength = 500;
    public const int MaxDraftedByLength = 200;

    public GrammarPoint()
        : base(StudyItemKind.Grammar)
    {
    }

    /// <summary>The grammar list entry this point teaches (one point per entry).</summary>
    public int GrammarListEntryId { get; set; }
    public GrammarListEntry? Entry { get; set; }

    public GrammarContentStatus Status { get; set; }

    /// <summary>What the point means and does, for the lesson. Paragraphs are separated by blank lines.</summary>
    public string Explanation { get; set; } = string.Empty;

    /// <summary>How it attaches: one rule per line, e.g. "Verb (plain) + のに" then "Noun / な-adjective + な + のに".</summary>
    public string Formation { get; set; } = string.Empty;

    public GrammarFormality Formality { get; set; }

    public List<GrammarContrast> Contrasts { get; set; } = new();

    public List<GrammarMistake> Mistakes { get; set; } = new();

    public List<GrammarSituation> Situations { get; set; } = new();

    public List<GrammarExample> Examples { get; set; } = new();

    /// <summary>Which model and prompt drafted the content, or null when the user wrote it.</summary>
    public string? DraftedBy { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>An example sentence for a grammar point, with an English translation and optional audio.</summary>
public class GrammarExample
{
    public const int MaxTextLength = 300;
    public const int MaxEnglishLength = 500;

    public int Id { get; set; }

    public int GrammarPointId { get; set; }
    public GrammarPoint? GrammarPoint { get; set; }

    public string Text { get; set; } = string.Empty;

    public string English { get; set; } = string.Empty;

    public GrammarExampleSource Source { get; set; }

    /// <summary>Drafted examples wait for the user's OK; lessons show approved ones only.</summary>
    public GrammarContentStatus Status { get; set; }

    public bool HasAudio { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The audio clip. Only loaded when asked for.</summary>
    public GrammarExampleAudio? Audio { get; set; }
}

/// <summary>A grammar example's audio, kept apart so listing examples never loads it.</summary>
public class GrammarExampleAudio
{
    public int Id { get; set; }

    public int GrammarExampleId { get; set; }
    public GrammarExample? GrammarExample { get; set; }

    public byte[] Audio { get; set; } = Array.Empty<byte>();
}

/// <summary>Rules for grammar lesson content: tidying what's entered and what approval needs.</summary>
public static class GrammarContent
{
    /// <summary>Contrasts a lesson needs nothing more than; rows with no pattern are dropped.</summary>
    public static List<GrammarContrast> Clean(IEnumerable<GrammarContrast> rows) =>
        rows.Select(r => new GrammarContrast(Trim(r.Pattern, 100), Trim(r.Difference, 1000)))
            .Where(r => r.Pattern.Length > 0)
            .ToList();

    /// <summary>Mistakes; rows with no wrong sentence are dropped.</summary>
    public static List<GrammarMistake> Clean(IEnumerable<GrammarMistake> rows) =>
        rows.Select(r => new GrammarMistake(CleanSentence(r.Wrong), CleanSentence(r.Right), Trim(r.Why, 1000)))
            .Where(r => r.Wrong.Length > 0)
            .ToList();

    /// <summary>Situations; rows with no situation are dropped.</summary>
    public static List<GrammarSituation> Clean(IEnumerable<GrammarSituation> rows) =>
        rows.Select(r => new GrammarSituation(Trim(r.Situation, 500), Trim(r.Hint, 300)))
            .Where(r => r.Situation.Length > 0)
            .ToList();

    /// <summary>
    /// "How it attaches" as its rules, one per line. Content saved as a single line
    /// of rules joined with semicolons is split at the semicolons.
    /// </summary>
    public static List<string> FormationRules(string? formation)
    {
        List<string> lines = Lines(formation, '\n');
        return lines.Count == 1 ? Lines(lines[0], ';') : lines;
    }

    /// <summary>Rules as stored: trimmed, blank ones dropped, one per line.</summary>
    public static string JoinFormation(IEnumerable<string> rules) =>
        string.Join("\n", rules.SelectMany(r => Lines(r, '\n')));

    private static List<string> Lines(string? text, char separator) =>
        (text ?? string.Empty).Replace("\r", "").Split(separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>A Japanese sentence as stored: trimmed, without the spaces models sometimes put between words.</summary>
    public static string CleanSentence(string? text) =>
        (text ?? string.Empty).Trim().Replace(" ", "").Replace("　", "");

    /// <summary>What a point still needs before a lesson can teach it (empty when it's ready).</summary>
    public static List<string> ApprovalProblems(GrammarPoint point)
    {
        List<string> problems = new();
        if (string.IsNullOrWhiteSpace(point.Explanation)) {
            problems.Add("It needs an explanation.");
        }
        if (string.IsNullOrWhiteSpace(point.Formation)) {
            problems.Add("It needs \"how it attaches\".");
        }
        if (!point.Examples.Any(x => x.Status == GrammarContentStatus.Approved)) {
            problems.Add("It needs at least one approved example.");
        }
        if (point.Situations.Count == 0) {
            problems.Add("It needs at least one idea to offer when you write your own sentences.");
        }
        return problems;
    }

    private static string Trim(string? text, int max)
    {
        string t = (text ?? string.Empty).Trim();
        return t.Length > max ? t[..max] : t;
    }
}
