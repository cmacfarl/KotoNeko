using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace KotoNeko.Core.Llm;

/// <summary>The word sentences are generated for.</summary>
public sealed record SentenceTarget(string Japanese, string Reading, IReadOnlyList<string> Meanings);

/// <summary>One generated candidate.</summary>
public sealed record GeneratedSentence(
    [property: JsonPropertyName("japanese")] string Japanese,
    [property: JsonPropertyName("english")] string English);

/// <summary>The model's answer.</summary>
public sealed record GeneratedSentences(
    [property: JsonPropertyName("sentences")] List<GeneratedSentence>? Sentences);

/// <summary>
/// The request for practice sentences: the target word, a sample of the words the
/// learner knows, and the grammar they know. Candidates are only suggestions: each
/// is checked offline against the known-words pool before the user sees it.
/// </summary>
public static class SentenceGeneration
{
    public const string PromptVersion = "practice-sentences-v1";

    /// <summary>Candidates asked for per request.</summary>
    public const int Count = 3;

    /// <summary>Known words sent per request: enough to write with, small enough to keep the prompt cheap.</summary>
    public const int KnownWordSample = 400;

    private const int MaxSentenceLength = 300;

    private const string System = """
        You write short Japanese sentences for a learner's listening practice. The learner hears a target word
        and then one of your sentences, and has to understand the sentence by ear. Only the target word is being
        tested, so everything else in the sentence should already be familiar to them: a plain sentence they can
        follow is far more useful than a rich one with a word they don't know.

        Write the requested number of different sentences, each using the target word (inflected as needed) in
        the meaning given:
        - Use only words from the learner's known-word list, plus particles and the most basic function words
          (です, ます, だ, する, ある, いる, こと, もの). Every other word you use counts against the sentence.
        - Keep to the grammar patterns the learner knows (listed), plus basic plain and polite forms.
        - Keep each sentence short: about 20 kana or fewer when read aloud.
        - Make them natural, everyday Japanese that a native speaker would say, each in a different situation
          that shows what the word means.
        - Write them as Japanese is normally written, with kanji where it's usual. No furigana, romaji or spaces.
        - Give a natural English translation of each.
        Don't repeat any of the learner's existing sentences for the word.
        """;

    /// <summary>The JSON schema the answer must follow.</summary>
    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["sentences"] = new JsonObject {
                ["type"] = "array",
                ["items"] = new JsonObject {
                    ["type"] = "object",
                    ["properties"] = new JsonObject {
                        ["japanese"] = new JsonObject { ["type"] = "string" },
                        ["english"] = new JsonObject { ["type"] = "string" },
                    },
                    ["required"] = new JsonArray("japanese", "english"),
                    ["additionalProperties"] = false,
                },
            },
        },
        ["required"] = new JsonArray("sentences"),
        ["additionalProperties"] = false,
    };

    /// <summary>Build the request. <paramref name="knownWords"/> should already be a sample.</summary>
    public static LlmRequest Build(
        SentenceTarget target, IEnumerable<string> knownWords,
        IEnumerable<(string Pattern, string Meaning)> grammar, IEnumerable<string> existing)
    {
        StringBuilder prompt = new();
        prompt.AppendLine($"Target word: {target.Japanese} ({target.Reading})");
        prompt.AppendLine($"Meaning: {(target.Meanings.Count > 0 ? string.Join(", ", target.Meanings) : "(not given)")}");
        prompt.AppendLine($"Number of sentences: {Count}");
        prompt.AppendLine();

        List<string> words = knownWords.Where(w => w != target.Japanese).Distinct().ToList();
        prompt.AppendLine(words.Count > 0
            ? $"Words the learner knows ({words.Count}, a sample):\n{string.Join("、", words)}"
            : "Words the learner knows: none recorded yet, so use only very basic everyday words.");
        prompt.AppendLine();

        List<(string Pattern, string Meaning)> points = grammar.ToList();
        prompt.AppendLine(points.Count > 0
            ? "Grammar the learner knows:\n" + string.Join("\n", points.Select(p => $"- {p.Pattern}: {p.Meaning}"))
            : "Grammar the learner knows: basic N5 grammar.");
        prompt.AppendLine();

        List<string> have = existing.ToList();
        prompt.Append(have.Count > 0
            ? "Existing sentences for this word (don't repeat them):\n" + string.Join("\n", have.Select(s => $"- {s}"))
            : "Existing sentences for this word: none.");

        return new LlmRequest(LlmJob.Generate, PromptVersion, System, prompt.ToString(), "practice_sentences", Schema());
    }

    /// <summary>Whether the answer is usable at all: null when it is, else what's wrong.</summary>
    public static string? Validate(GeneratedSentences answer) =>
        answer.Sentences is not { Count: > 0 } ? "no sentences"
        : answer.Sentences.Any(s => string.IsNullOrWhiteSpace(s.Japanese)) ? "a sentence with no Japanese"
        : null;

    /// <summary>The candidates worth keeping: trimmed, not too long, and new.</summary>
    public static List<GeneratedSentence> Clean(GeneratedSentences answer, IEnumerable<string> existing)
    {
        HashSet<string> seen = new(existing.Select(s => s.Trim()), StringComparer.Ordinal);
        List<GeneratedSentence> result = new();
        foreach (GeneratedSentence s in answer.Sentences ?? new List<GeneratedSentence>()) {
            string japanese = s.Japanese.Trim().Replace(" ", "").Replace("　", "");
            string english = (s.English ?? string.Empty).Trim();
            if (japanese.Length == 0 || japanese.Length > MaxSentenceLength || !seen.Add(japanese)) {
                continue;
            }
            result.Add(new GeneratedSentence(japanese, english.Length > 500 ? english[..500] : english));
        }
        return result;
    }
}
