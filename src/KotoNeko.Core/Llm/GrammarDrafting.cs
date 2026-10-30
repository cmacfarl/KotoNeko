using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Llm;

/// <summary>The grammar point lesson content is drafted for.</summary>
public sealed record GrammarDraftTarget(string Pattern, string Meaning, JlptLevel Level);

/// <summary>A drafted example sentence.</summary>
public sealed record DraftedExample(
    [property: JsonPropertyName("japanese")] string Japanese,
    [property: JsonPropertyName("english")] string English);

/// <summary>The model's draft of a grammar point's lesson content.</summary>
public sealed record GrammarDraft(
    [property: JsonPropertyName("explanation")] string Explanation,
    [property: JsonPropertyName("formation")] List<string>? Formation,
    [property: JsonPropertyName("formality")] string Formality,
    [property: JsonPropertyName("contrasts")] List<GrammarContrast>? Contrasts,
    [property: JsonPropertyName("mistakes")] List<GrammarMistake>? Mistakes,
    [property: JsonPropertyName("examples")] List<DraftedExample>? Examples,
    [property: JsonPropertyName("situations")] List<GrammarSituation>? Situations);

/// <summary>
/// The request for a grammar point's lesson content: explanation, how it attaches,
/// register, contrasts with similar points, common mistakes, example sentences and
/// guided-round situations. Everything comes back as a draft the user edits and
/// approves; examples are checked offline against the known-words pool.
/// </summary>
public static class GrammarDrafting
{
    // v2: formation is a list of rules, not one line.
    public const string PromptVersion = "grammar-content-v2";

    public const int ExampleCount = 5;
    public const int SituationCount = 4;

    /// <summary>Known words sent per request, as for practice sentences.</summary>
    public const int KnownWordSample = 400;

    public static readonly IReadOnlyList<string> Formalities = new[] { "neutral", "casual", "polite", "formal" };

    private const string System = """
        You write lesson content for one Japanese grammar point, for an adult self-taught learner preparing for
        the JLPT. Write it yourself, in your own words and with your own example sentences: don't reproduce
        text or examples from textbooks, grammar dictionaries or websites.

        - explanation: what the point means and does, and when a native speaker reaches for it, in plain English.
          Two or three short paragraphs separated by a blank line. Mention any nuance that changes whether it fits
          (feelings it carries, who the subject can be, what can follow it).
        - formation: how it attaches, as a list of short rules, one per word type or form, e.g.
          ["Verb (plain, present or past) + のに", "い-adjective + のに", "Noun / な-adjective + な + のに",
          "Noun / な-adjective (past) + だった + のに"].
        - formality: neutral, casual, polite or formal: the register it belongs to.
        - contrasts: two to four similar points a learner confuses it with (prefer ones at or below its JLPT
          level), each with the difference in one or two sentences.
        - mistakes: two to four typical learner mistakes with this point: the wrong sentence, the corrected
          sentence, and why, in one sentence.
        - examples: natural, everyday sentences that each show the point used correctly in a different
          situation, with a natural English translation. Keep them short (about 25 kana or fewer read aloud), and
          use words from the learner's known-word list where you can, plus particles and basic function words.
          Write Japanese as it's normally written, with kanji where usual; no furigana, romaji or spaces.
        - situations: tasks for the learner to write their own sentence with the point. Each describes, in
          English, a situation and what to say, and pins down everything the point depends on: who the speaker
          is, their circumstances, and how they feel about it (for 〜のに, say whether the outcome is unwelcome
          or surprising; for a purpose clause, say what's under the speaker's control). A grader will judge the
          learner's sentence against the situation exactly as written. hint gives the shape of the answer
          (e.g. "Verb (past) + のに, then what happened") without giving the sentence away.
        """;

    /// <summary>The JSON schema the answer must follow; every object is closed.</summary>
    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["explanation"] = new JsonObject { ["type"] = "string" },
            ["formation"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
            ["formality"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(Formalities.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()) },
            ["contrasts"] = ListOf(("pattern", "difference")),
            ["mistakes"] = ListOf(("wrong", "right", "why")),
            ["examples"] = ListOf(("japanese", "english")),
            ["situations"] = ListOf(("situation", "hint")),
        },
        ["required"] = new JsonArray("explanation", "formation", "formality", "contrasts", "mistakes", "examples", "situations"),
        ["additionalProperties"] = false,
    };

    /// <summary>Build the request. <paramref name="knownWords"/> should already be a sample.</summary>
    public static LlmRequest Build(
        GrammarDraftTarget target, IEnumerable<string> knownWords,
        IEnumerable<(string Pattern, string Meaning)> knownGrammar, IEnumerable<string> existingExamples)
    {
        StringBuilder prompt = new();
        prompt.AppendLine($"Grammar point: {target.Pattern} ({target.Meaning}), JLPT {target.Level}");
        prompt.AppendLine($"Examples wanted: {ExampleCount}. Situations wanted: {SituationCount}.");
        prompt.AppendLine();

        List<string> words = knownWords.Distinct().ToList();
        prompt.AppendLine(words.Count > 0
            ? $"Words the learner knows ({words.Count}, a sample):\n{string.Join("、", words)}"
            : "Words the learner knows: none recorded yet, so use only very basic everyday words.");
        prompt.AppendLine();

        List<(string Pattern, string Meaning)> points = knownGrammar.ToList();
        prompt.AppendLine(points.Count > 0
            ? "Grammar the learner already knows:\n" + string.Join("\n", points.Select(p => $"- {p.Pattern}: {p.Meaning}"))
            : "Grammar the learner already knows: basic N5 grammar.");
        prompt.AppendLine();

        List<string> have = existingExamples.ToList();
        prompt.Append(have.Count > 0
            ? "Examples the point already has (don't repeat them):\n" + string.Join("\n", have.Select(s => $"- {s}"))
            : "Examples the point already has: none.");

        return new LlmRequest(LlmJob.Generate, PromptVersion, System, prompt.ToString(), "grammar_lesson_content", Schema(), MaxTokens: 12000);
    }

    /// <summary>Whether the draft is usable at all: null when it is, else what's wrong.</summary>
    public static string? Validate(GrammarDraft draft) =>
        string.IsNullOrWhiteSpace(draft.Explanation) ? "no explanation"
        : draft.Formation is null || draft.Formation.All(string.IsNullOrWhiteSpace) ? "no formation"
        : !Formalities.Contains(draft.Formality) ? $"unknown formality '{draft.Formality}'"
        : draft.Examples is not { Count: > 0 } || draft.Examples.All(x => string.IsNullOrWhiteSpace(x.Japanese)) ? "no examples"
        : draft.Situations is not { Count: > 0 } ? "no situations"
        : null;

    public static GrammarFormality Formality(string value) => value switch {
        "casual" => GrammarFormality.Casual,
        "polite" => GrammarFormality.Polite,
        "formal" => GrammarFormality.Formal,
        _ => GrammarFormality.Neutral,
    };

    /// <summary>The drafted examples worth keeping: tidied, not too long, and new.</summary>
    public static List<DraftedExample> CleanExamples(GrammarDraft draft, IEnumerable<string> existing)
    {
        HashSet<string> seen = new(existing.Select(GrammarContent.CleanSentence), StringComparer.Ordinal);
        List<DraftedExample> result = new();
        foreach (DraftedExample x in draft.Examples ?? new List<DraftedExample>()) {
            string japanese = GrammarContent.CleanSentence(x.Japanese);
            string english = (x.English ?? string.Empty).Trim();
            if (japanese.Length == 0 || japanese.Length > GrammarExample.MaxTextLength || !seen.Add(japanese)) {
                continue;
            }
            result.Add(new DraftedExample(japanese, english.Length > GrammarExample.MaxEnglishLength ? english[..GrammarExample.MaxEnglishLength] : english));
        }
        return result;
    }

    private static JsonObject ListOf(params string[] fields) => new() {
        ["type"] = "array",
        ["items"] = new JsonObject {
            ["type"] = "object",
            ["properties"] = new JsonObject(fields.Select(f => KeyValuePair.Create(f, (JsonNode?)new JsonObject { ["type"] = "string" }))),
            ["required"] = new JsonArray(fields.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()),
            ["additionalProperties"] = false,
        },
    };

    private static JsonObject ListOf((string, string) fields) => ListOf(fields.Item1, fields.Item2);

    private static JsonObject ListOf((string, string, string) fields) => ListOf(fields.Item1, fields.Item2, fields.Item3);
}
