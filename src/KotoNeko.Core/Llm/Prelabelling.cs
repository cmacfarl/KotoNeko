using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Llm;

/// <summary>A labeller's answer.</summary>
public sealed record PrelabelAnswer(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("explanation")] string Explanation);

/// <summary>Where a combination of votes stands.</summary>
public enum PrelabelState
{
    /// <summary>Not every labeller has voted yet (a call failed, or a labeller hasn't run).</summary>
    Incomplete,

    /// <summary>All labellers agree: the item gets an AI-agreed label.</summary>
    Agreed,

    /// <summary>The labellers disagree: a person decides.</summary>
    Disagree,
}

/// <summary>The outcome of combining one item's votes.</summary>
public sealed record PrelabelResult(PrelabelState State, GradingLabel? Label);

/// <summary>
/// Pre-labelling the grading test set: several models label each sentence
/// independently, and where they all agree the item gets an AI-agreed label; a
/// person settles the rest. The prompt adapts Kotori's sentence-checking task (a
/// teacher's verdict on the learner's Japanese) to one grammar point and situation.
/// It is deliberately not the grader's prompt, so the labels aren't the grader again.
/// </summary>
public static class Prelabelling
{
    public const string PromptVersion = "prelabel-v1";

    /// <summary>The prompt version recorded on the votes taken from how the candidates were drafted.</summary>
    public const string DraftedVersion = "drafted-intent";

    private static readonly Dictionary<string, GradingLabel> Labels = new() {
        ["correct"] = GradingLabel.Correct,
        ["subtly_wrong"] = GradingLabel.SubtlyWrong,
        ["clearly_wrong"] = GradingLabel.ClearlyWrong,
        ["correct_but_unnatural"] = GradingLabel.CorrectButUnnatural,
    };

    private const string System = """
        You are a Japanese teacher checking a sentence written by an adult English speaker studying at roughly
        JLPT N4 to N3 level. The student was given a situation and asked to answer it using one grammar point.
        Be their teacher: decide whether they used that grammar point correctly. Be accurate and honest, and
        don't invent problems in a correct sentence. If something depends on context or is debated, say so.

        Choose one label:
        - correct: the grammar point is used correctly (form, how it attaches, meaning and usage fit the
          situation) and the sentence is natural.
        - correct_but_unnatural: the grammar point is used correctly and the sentence is grammatical, but a
          native speaker wouldn't say it that way.
        - subtly_wrong: it looks plausible, but the grammar point is misused: the wrong meaning for the
          situation, or a usage restriction broken.
        - clearly_wrong: the grammar point's form is broken, or the sentence doesn't make sense for the
          situation.

        If the sentence doesn't use the target grammar point at all, it is wrong (subtly_wrong unless its form
        is also broken), even if it is otherwise good Japanese. Mistakes that don't involve the grammar point (a
        particle elsewhere, a vocabulary or kanji slip) don't make it wrong; mention them in the explanation.

        Explain briefly in English. Write any Japanese in Japanese script, never romaji. Reply only with JSON
        matching the provided schema.
        """;

    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["label"] = new JsonObject {
                ["type"] = "string",
                ["enum"] = new JsonArray(Labels.Keys.Select(k => (JsonNode?)JsonValue.Create(k)).ToArray()),
            },
            ["explanation"] = new JsonObject { ["type"] = "string" },
        },
        ["required"] = new JsonArray("label", "explanation"),
        ["additionalProperties"] = false,
    };

    public static LlmRequest Build(GradingTask task, string? effort) => new(
        LlmJob.Label, LlmFeature.Prelabelling, PromptVersion, System,
        $"""
        Grammar point: {task.Pattern} ({task.Meaning})
        Situation given to the student: {(task.Task.Length > 0 ? task.Task : "(none: free sentence)")}
        Student's sentence: {task.Sentence}
        """,
        "label", Schema(), MaxTokens: 4000, Effort: effort);

    public static string? Validate(PrelabelAnswer answer) =>
        Labels.ContainsKey(answer.Label) ? null : $"unknown label '{answer.Label}'";

    public static GradingLabel Parse(string label) => Labels[label];

    /// <summary>
    /// Combine one item's votes. They agree when they all pass or all fail the target
    /// and, when passing, all agree on correct versus unnatural. Subtly versus clearly
    /// wrong doesn't need agreement (the eval scores pass or fail): the majority
    /// decides, and <paramref name="tieBreak"/> (how the sentence was drafted) settles a tie.
    /// </summary>
    public static PrelabelResult Combine(IReadOnlyCollection<GradingLabel> votes, int required, GradingLabel? tieBreak)
    {
        if (votes.Count < required || votes.Count == 0) {
            return new PrelabelResult(PrelabelState.Incomplete, null);
        }
        if (votes.Select(v => v.TargetCorrect()).Distinct().Count() > 1) {
            return new PrelabelResult(PrelabelState.Disagree, null);
        }
        if (votes.First().TargetCorrect()) {
            return votes.Distinct().Count() == 1
                ? new PrelabelResult(PrelabelState.Agreed, votes.First())
                : new PrelabelResult(PrelabelState.Disagree, null);
        }

        int subtle = votes.Count(v => v == GradingLabel.SubtlyWrong);
        int clear = votes.Count(v => v == GradingLabel.ClearlyWrong);
        GradingLabel label = subtle > clear ? GradingLabel.SubtlyWrong
            : clear > subtle ? GradingLabel.ClearlyWrong
            : tieBreak is { } t && !t.TargetCorrect() ? t
            : GradingLabel.SubtlyWrong;
        return new PrelabelResult(PrelabelState.Agreed, label);
    }
}
