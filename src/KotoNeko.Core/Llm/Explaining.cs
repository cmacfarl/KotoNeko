using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KotoNeko.Core.Llm;

/// <summary>The answer to a "Why?" question.</summary>
public sealed record WhyAnswer([property: JsonPropertyName("answer")] string Answer);

/// <summary>An earlier question about the same verdict, and its answer.</summary>
public sealed record WhyExchange(string Question, string Answer);

/// <summary>
/// "Why?" follow-ups (the Explain job): a few questions about one graded sentence,
/// answered in the context of its grammar point and verdict. An answer never changes
/// the verdict; if it thinks the verdict was wrong, it says so and points to Dispute.
/// </summary>
public static class Explaining
{
    public const string PromptVersion = "why-v1";

    /// <summary>Questions allowed per verdict.</summary>
    public const int MaxQuestions = 3;

    public const int MaxQuestionLength = 500;

    private static readonly JsonSerializerOptions VerdictJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const string System = """
        A Japanese learner wrote a sentence using a grammar point, and a grader judged it. Answer the learner's
        follow-up question about it, in plain English, briefly: a short paragraph, about 120 words at most.

        - Stay on this sentence, this grammar point and the verdict (including the grammar and words in the
          sentence). If the question is about something else, say in one sentence that you can only help with
          this sentence and grammar point.
        - Don't change the verdict. If you think it was wrong, say so plainly and suggest the learner use the
          Dispute button to get a second opinion; don't say their review now counts differently.
        - Write Japanese normally, with kanji, and give the reading in brackets for any word likely to be above
          the grammar point's JLPT level.
        """;

    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject { ["answer"] = new JsonObject { ["type"] = "string" } },
        ["required"] = new JsonArray("answer"),
        ["additionalProperties"] = false,
    };

    public static LlmRequest Build(GradingTask task, GradingVerdict verdict, IReadOnlyList<WhyExchange> earlier, string question)
    {
        StringBuilder prompt = new();
        prompt.AppendLine($"Grammar point: {task.Pattern} ({task.Meaning})");
        if (task.Task.Length > 0) {
            prompt.AppendLine($"Situation given to the learner: {task.Task}");
        }
        prompt.AppendLine($"What the learner says they mean: {(task.Intent.Length > 0 ? task.Intent : "(not given)")}");
        prompt.AppendLine($"Learner's sentence: {task.Sentence}");
        prompt.AppendLine($"The grader's verdict: {JsonSerializer.Serialize(verdict, VerdictJson)}");
        foreach (WhyExchange e in earlier) {
            prompt.AppendLine();
            prompt.AppendLine($"Earlier question: {e.Question}");
            prompt.AppendLine($"Your answer: {e.Answer}");
        }
        prompt.AppendLine();
        prompt.Append($"Question: {question}");
        return new LlmRequest(LlmJob.Explain, LlmFeature.WhyQuestion, PromptVersion, System, prompt.ToString(), "why_answer", Schema(), MaxTokens: 3000);
    }

    public static string? Validate(WhyAnswer answer) =>
        string.IsNullOrWhiteSpace(answer.Answer) ? "no answer" : null;
}
