using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Llm;

/// <summary>What the learner was asked to do, and what they wrote.</summary>
public sealed record GradingTask(string Pattern, string Meaning, string Task, string Sentence);

/// <summary>Another mistake in the sentence: feedback only, never an SRS signal.</summary>
public sealed record GradingError(
    [property: JsonPropertyName("span")] string Span,
    [property: JsonPropertyName("correction")] string Correction,
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("explanation")] string Explanation);

/// <summary>
/// The grader's structured verdict. Only <see cref="TargetUsedCorrectly"/> moves the
/// SRS; everything else is feedback.
/// </summary>
public sealed record GradingVerdict(
    [property: JsonPropertyName("target_used_correctly")] bool TargetUsedCorrectly,
    [property: JsonPropertyName("natural")] bool Natural,
    [property: JsonPropertyName("errors")] List<GradingError>? Errors,
    [property: JsonPropertyName("natural_rewrite")] string NaturalRewrite,
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("confidence")] string Confidence);

/// <summary>
/// Grading a learner's sentence for one grammar point (Phase 5's composition reviews,
/// measured first in Phase 3's eval). The verdict schema follows the roadmap: whether
/// the target was used correctly (the only SRS signal), other errors with a category
/// (kanji choice among them), "correct but unnatural", a natural rewrite and the
/// grader's confidence.
/// </summary>
public static class Grading
{
    // grade-v2: a natural reading of the situation counts; a required different form is
    // wrong, never just unnatural; the explanation is written before the verdict.
    public const string PromptVersion = "grade-v2";

    public static readonly IReadOnlyList<string> ErrorCategories = new[] {
        "target_pattern", "conjugation", "particle", "vocabulary", "kanji", "word_order", "politeness", "other",
    };

    public static readonly IReadOnlyList<string> Confidences = new[] { "low", "medium", "high" };

    private const string System = """
        You grade a Japanese learner's sentence for one grammar point. The learner was given a situation and wrote
        a sentence for it using that grammar point. Your verdict decides whether the learner's review of the
        grammar point counts as passed, so judge the target grammar point carefully and fairly:

        - explanation comes first: a short English explanation for the learner of why the target use is right or
          wrong. Decide target_used_correctly and natural from it; they must agree with what it says.
        - target_used_correctly is true only if the sentence uses the target grammar point, in a correct form
          (conjugation and how it attaches), with a meaning and usage that fit the situation. If the sentence
          doesn't use the target grammar point at all, it is false, even if the sentence is otherwise fine.
        - Judge against the situation as written. Where it leaves the speaker's circumstances or feelings open,
          and a natural reading of it makes the sentence correct, the target is used correctly: say in the
          explanation what the sentence implies and what to use if the learner meant otherwise. Where the
          situation does pin the meaning down (a neutral comparison, say, or the speaker's own choice), judge
          against that meaning.
        - Mistakes that don't involve the target grammar point (a wrong particle elsewhere, a vocabulary or kanji
          slip) don't make target_used_correctly false. List them in errors as feedback.
        - natural is false when the sentence is grammatical and uses the target correctly, but a native speaker
          wouldn't say it that way. Such a sentence still passes: target_used_correctly stays true.
        - "Unnatural" is only for a sentence that is acceptable as it stands. If the target grammar point needs
          a different form or construction to be correct here (if your explanation says another form is
          required, or that the target can't be used this way), target_used_correctly is false.
        - In errors, give the exact span of the learner's sentence, the correction, a category (use
          target_pattern for problems with the grammar point itself, kanji for a wrong kanji choice such as
          会う/合う), and a one-sentence explanation in English.
        - natural_rewrite is how a native speaker would say it for this situation, using the target grammar point.
        - confidence is how sure you are about target_used_correctly: low, medium or high.
        """;

    /// <summary>
    /// The JSON schema for <see cref="GradingVerdict"/>; every object is closed. The
    /// explanation comes first so the model reasons before it commits to a verdict
    /// (structured output follows the schema's property order).
    /// </summary>
    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["explanation"] = new JsonObject { ["type"] = "string" },
            ["target_used_correctly"] = new JsonObject { ["type"] = "boolean" },
            ["natural"] = new JsonObject { ["type"] = "boolean" },
            ["errors"] = new JsonObject {
                ["type"] = "array",
                ["items"] = new JsonObject {
                    ["type"] = "object",
                    ["properties"] = new JsonObject {
                        ["span"] = new JsonObject { ["type"] = "string" },
                        ["correction"] = new JsonObject { ["type"] = "string" },
                        ["category"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(ErrorCategories) },
                        ["explanation"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("span", "correction", "category", "explanation"),
                    ["additionalProperties"] = false,
                },
            },
            ["natural_rewrite"] = new JsonObject { ["type"] = "string" },
            ["confidence"] = new JsonObject { ["type"] = "string", ["enum"] = Strings(Confidences) },
        },
        ["required"] = new JsonArray("explanation", "target_used_correctly", "natural", "errors", "natural_rewrite", "confidence"),
        ["additionalProperties"] = false,
    };

    public static LlmRequest Build(GradingTask task, LlmFeature feature) => new(
        LlmJob.Grade, feature, PromptVersion, System,
        $"""
        Grammar point: {task.Pattern} ({task.Meaning})
        Situation given to the learner: {(task.Task.Length > 0 ? task.Task : "(none: free sentence)")}
        Learner's sentence: {task.Sentence}
        """,
        "grading_verdict", Schema(), MaxTokens: 4000);

    /// <summary>Whether a verdict is usable: null when it is, else what's wrong.</summary>
    public static string? Validate(GradingVerdict verdict) =>
        !Confidences.Contains(verdict.Confidence) ? $"unknown confidence '{verdict.Confidence}'"
        : verdict.Errors?.FirstOrDefault(e => !ErrorCategories.Contains(e.Category)) is { } bad ? $"unknown error category '{bad.Category}'"
        : null;

    private static JsonArray Strings(IEnumerable<string> values) => new(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
}

/// <summary>One item's label and the grader's verdict (null when the call failed).</summary>
public sealed record GradedItem(string GrammarKey, GradingLabel Label, GradingVerdict? Verdict);

/// <summary>A rate with the counts behind it: about 20 items per point is noisy, so counts matter.</summary>
public sealed record Rate(int Count, int Of)
{
    public double Value => Of == 0 ? 0 : (double)Count / Of;

    public override string ToString() => Of == 0 ? "n/a" : $"{Value:P0} ({Count}/{Of})";
}

/// <summary>How well a grader agreed with the labels.</summary>
/// <param name="Failed">Items with no verdict (failed or refused calls): reported, never silently dropped.</param>
/// <param name="Agreement">Graded items where the verdict on the target matched the label.</param>
/// <param name="FalseCorrection">Items labelled correct or unnatural that the grader failed.</param>
/// <param name="FalsePass">Items labelled subtly or clearly wrong that the grader passed.</param>
/// <param name="NaturalnessMisses">Items the grader passed but judged natural or unnatural differently from the label (secondary).</param>
public sealed record GradingScores(int Items, int Failed, Rate Agreement, Rate FalseCorrection, Rate FalsePass, Rate NaturalnessMisses);

public static class GradingEval
{
    /// <summary>Score graded items against their labels. Excluded items must already be left out.</summary>
    public static GradingScores Score(IReadOnlyCollection<GradedItem> items)
    {
        List<GradedItem> graded = items.Where(i => i.Verdict is not null).ToList();
        int agree = graded.Count(i => i.Verdict!.TargetUsedCorrectly == i.Label.TargetCorrect());

        List<GradedItem> shouldPass = graded.Where(i => i.Label.TargetCorrect()).ToList();
        List<GradedItem> shouldFail = graded.Where(i => !i.Label.TargetCorrect()).ToList();
        List<GradedItem> bothPassed = shouldPass.Where(i => i.Verdict!.TargetUsedCorrectly).ToList();

        return new GradingScores(
            items.Count,
            items.Count - graded.Count,
            new Rate(agree, graded.Count),
            new Rate(shouldPass.Count(i => !i.Verdict!.TargetUsedCorrectly), shouldPass.Count),
            new Rate(shouldFail.Count(i => i.Verdict!.TargetUsedCorrectly), shouldFail.Count),
            new Rate(bothPassed.Count(i => i.Verdict!.Natural != (i.Label == GradingLabel.Correct)), bothPassed.Count));
    }

    /// <summary>What label the verdict amounts to (wrong verdicts can't say subtle or clear).</summary>
    public static string Predicted(GradingVerdict verdict) =>
        !verdict.TargetUsedCorrectly ? "wrong" : verdict.Natural ? "correct" : "correct but unnatural";
}
