using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Review;

namespace KotoNeko.Core.Llm;

/// <summary>A sentence sent for a casual rewrite, numbered within the batch.</summary>
public sealed record CasualSource(int Number, string Japanese, string English);

/// <summary>One rewrite in the model's answer.</summary>
public sealed record CasualRewriteDraft(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("casual")] string Casual);

public sealed record CasualRewriteAnswer(
    [property: JsonPropertyName("rewrites")] List<CasualRewriteDraft>? Rewrites);

/// <summary>
/// Casual versions of the user's sentences (Phase 6c), for dictation: the same
/// meaning as friends would say it, in plain forms with the contractions everyday
/// speech uses. A batch goes in; each rewrite is checked here before it's kept.
/// </summary>
public static class CasualRewrite
{
    public const string PromptVersion = "casual-v1";

    /// <summary>Sentences per batch.</summary>
    public const int BatchSize = 10;

    // Polite endings, and the full forms the contractions shorten.
    private static readonly string[] Changeable = {
        "です", "ます", "でした", "ました", "ません", "ましょう",
        "ている", "でいる", "ていた", "でいた", "てい", "でい",
        "てしまう", "でしまう", "てしまっ", "でしまっ",
        "なければ", "なくては", "ておく", "でおく", "ておい", "でおい", "では",
    };

    private const string System = """
        You rewrite Japanese sentences the way friends would say them to each other in everyday conversation,
        for a learner practising listening to casual speech. Keep each sentence's meaning, content and kanji
        exactly; change only the register and the forms:

        - plain forms instead of です/ます (食べます → 食べる, 行きました → 行った, 静かです → 静か(だ));
        - the contractions everyday speech uses where they come naturally: 〜ている → 〜てる, 〜てしまう →
          〜ちゃう (〜でしまう → 〜じゃう), 〜ておく → 〜とく (〜でおく → 〜どく), 〜なければ／〜なくては →
          〜なきゃ／〜なくちゃ, では → じゃ, と言う／という → って, の (explaining) → ん;
        - particles may be dropped where a native speaker would naturally drop them in casual speech.

        Standard Tokyo casual speech only: no dialect, no slang, nothing rude. Don't add sentence-final particles
        (よ, ね) unless they make it sound natural. If a sentence is already casual and nothing would change,
        leave it out. Give each rewrite with the number of its sentence.
        """;

    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["rewrites"] = new JsonObject {
                ["type"] = "array",
                ["items"] = new JsonObject {
                    ["type"] = "object",
                    ["properties"] = new JsonObject {
                        ["number"] = new JsonObject { ["type"] = "integer" },
                        ["casual"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("number", "casual"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("rewrites"),
        ["additionalProperties"] = false,
    };

    /// <summary>Whether a sentence has something casual speech would change (a polite ending or a full form).</summary>
    public static bool WouldChange(string text) => Changeable.Any(text.Contains);

    public static LlmRequest Build(IReadOnlyList<CasualSource> sources)
    {
        StringBuilder prompt = new("Sentences:\n");
        foreach (CasualSource s in sources) {
            prompt.AppendLine($"{s.Number}. {s.Japanese}  ({s.English})");
        }
        return new LlmRequest(LlmJob.Generate, LlmFeature.CasualRewrite, PromptVersion, System, prompt.ToString().TrimEnd(),
            "casual_rewrites", Schema(), MaxTokens: 4000);
    }

    public static string? Validate(CasualRewriteAnswer answer) => answer.Rewrites is null ? "no rewrites" : null;

    /// <summary>
    /// The rewrites worth keeping, by batch number: for a sentence in the batch, not too
    /// long, and different from the original (ignoring spaces and punctuation).
    /// </summary>
    public static Dictionary<int, string> Clean(CasualRewriteAnswer answer, IReadOnlyList<CasualSource> sources)
    {
        Dictionary<int, CasualSource> byNumber = sources.ToDictionary(s => s.Number);
        Dictionary<int, string> kept = new();
        foreach (CasualRewriteDraft r in answer.Rewrites ?? new()) {
            string casual = GrammarContent.CleanSentence(r.Casual);
            if (!byNumber.TryGetValue(r.Number, out CasualSource? source) || kept.ContainsKey(r.Number)
                || casual.Length == 0 || casual.Length > CasualVersion.MaxTextLength
                || GrammarReview.Normalise(casual) == GrammarReview.Normalise(source.Japanese)) {
                continue;
            }
            kept[r.Number] = casual;
        }
        return kept;
    }
}
