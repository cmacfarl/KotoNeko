using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Llm;

/// <summary>The grader's view of a comprehension answer.</summary>
public sealed record ComprehensionVerdict(
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("understood")] string Understood,
    [property: JsonPropertyName("missed")] List<string>? Missed);

/// <summary>
/// Grading an English answer to a listening script's comprehension question (the
/// Grade job): leniently, on whether the learner understood what was said.
/// </summary>
public static class Comprehension
{
    public const string PromptVersion = "comprehension-v1";

    public static readonly IReadOnlyList<string> Levels = new[] { "yes", "partly", "no" };

    private const string System = """
        A Japanese learner listened to a short story or dialogue (without seeing the text) and answered a
        comprehension question about it in English. Judge whether their answer shows they understood the
        Japanese. Be lenient about wording: a paraphrase, a short answer or imperfect English counts if it gets
        the facts right. Don't mark down extra detail unless it's wrong.

        - explanation comes first: one to three sentences for the learner, in plain English, saying what they got
          right and, if anything, what the Japanese actually said (quote the relevant Japanese line).
        - understood: "yes" if the answer covers the answer points, "partly" if it gets some of them or is vague,
          "no" if it misses them or says something the script contradicts.
        - missed lists, in English, each answer point the learner missed or got wrong (empty when none).
        """;

    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["explanation"] = new JsonObject { ["type"] = "string" },
            ["understood"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Levels.Select(l => (JsonNode?)JsonValue.Create(l)).ToArray()) },
            ["missed"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray("explanation", "understood", "missed"),
        ["additionalProperties"] = false,
    };

    public static LlmRequest Build(ListeningScript script, string answer)
    {
        StringBuilder prompt = new();
        prompt.AppendLine($"The {(script.Kind == ListeningKind.Dialogue ? "dialogue" : "story")} (Japanese, then English):");
        foreach (ListeningLine line in script.Lines.OrderBy(l => l.SortOrder)) {
            prompt.AppendLine($"{line.Speaker}: {line.Text}  ({line.English})");
        }
        prompt.AppendLine();
        prompt.AppendLine($"Question: {script.Question}");
        prompt.AppendLine("Answer points:");
        foreach (string point in script.AnswerPoints.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            prompt.AppendLine($"- {point}");
        }
        prompt.AppendLine();
        prompt.Append($"Learner's answer: {answer}");
        return new LlmRequest(LlmJob.Grade, LlmFeature.ListeningComprehension, PromptVersion, System, prompt.ToString(),
            "comprehension_verdict", Schema(), MaxTokens: 3000);
    }

    public static string? Validate(ComprehensionVerdict verdict) =>
        !Levels.Contains(verdict.Understood) ? $"unknown level '{verdict.Understood}'"
        : string.IsNullOrWhiteSpace(verdict.Explanation) ? "no explanation"
        : null;

    public static ListeningUnderstanding Parse(string understood) => understood switch {
        "yes" => ListeningUnderstanding.Yes,
        "partly" => ListeningUnderstanding.Partly,
        _ => ListeningUnderstanding.No,
    };
}
