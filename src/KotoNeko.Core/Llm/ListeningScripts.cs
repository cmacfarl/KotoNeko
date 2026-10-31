using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Llm;

/// <summary>One line of a drafted script.</summary>
public sealed record ScriptLineDraft(
    [property: JsonPropertyName("speaker")] string Speaker,
    [property: JsonPropertyName("japanese")] string Japanese,
    [property: JsonPropertyName("english")] string English);

/// <summary>A word the writer used that the learner may not know.</summary>
public sealed record ScriptNewWord(
    [property: JsonPropertyName("word")] string Word,
    [property: JsonPropertyName("reading")] string Reading,
    [property: JsonPropertyName("meaning")] string Meaning);

/// <summary>A drafted listening script.</summary>
public sealed record ScriptDraft(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("title_english")] string TitleEnglish,
    [property: JsonPropertyName("lines")] List<ScriptLineDraft>? Lines,
    [property: JsonPropertyName("new_words")] List<ScriptNewWord>? NewWords,
    [property: JsonPropertyName("question")] string Question,
    [property: JsonPropertyName("answer_points")] List<string>? AnswerPoints);

/// <summary>What a script is written for.</summary>
public sealed record ScriptRequest(ListeningKind Kind, JlptLevel Level, string Topic);

/// <summary>
/// Writing extensive-listening scripts (Phase 6): a short story or a two-person
/// dialogue in everyday Japanese, from the learner's known words and grammar with a
/// little new, plus one comprehension question in English. Written on request; the
/// model is a setting, so the request is sent on a named provider and model.
/// </summary>
public static class ListeningScripts
{
    public const string PromptVersion = "listening-script-v1";

    public const int MinLines = 6;
    public const int MaxLines = 16;

    /// <summary>Known words sent per request, as for practice sentences.</summary>
    public const int KnownWordSample = 400;

    /// <summary>Recent titles sent so topics don't repeat.</summary>
    public const int RecentTitles = 20;

    private const string System = """
        You write short listening material for an adult self-taught learner of Japanese preparing for the JLPT.
        Write it yourself, original and in your own words: don't reproduce or adapt stories, dialogues or
        examples from textbooks, websites or other published material.

        - A "story" is told by a narrator (speaker "N"): a small everyday episode with a beginning, a turn and
          an end. A "dialogue" is two people (speakers "A" and "B") talking naturally about something everyday,
          taking turns; give it a small point or outcome.
        - Write 8 to 12 lines. Each line is one or two short sentences, about 40 kana or fewer read aloud, as it
          would naturally be said. Write Japanese as it's normally written, with kanji where usual; no furigana,
          romaji or spaces. Keep the register consistent (a dialogue between friends is casual; strangers or
          colleagues are polite).
        - Use words from the learner's known-word list wherever you can, plus particles and basic function words.
          You may use up to three words that aren't on it, if the story needs them; list each in new_words with
          its reading in hiragana and an English meaning. Use only grammar the learner knows or is studying, and
          grammar easier than their level.
        - english is a natural English translation of each line.
        - question is one comprehension question in English about what happened or what someone wants or feels,
          answerable only by understanding the Japanese (not by guessing). answer_points lists, in English, the
          facts a good answer mentions (one to three).
        - title is a short Japanese title; title_english its translation.
        """;

    public static JsonObject Schema() => new() {
        ["type"] = "object",
        ["properties"] = new JsonObject {
            ["title"] = new JsonObject { ["type"] = "string" },
            ["title_english"] = new JsonObject { ["type"] = "string" },
            ["lines"] = ListOf("speaker", "japanese", "english"),
            ["new_words"] = ListOf("word", "reading", "meaning"),
            ["question"] = new JsonObject { ["type"] = "string" },
            ["answer_points"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
        },
        ["required"] = new JsonArray("title", "title_english", "lines", "new_words", "question", "answer_points"),
        ["additionalProperties"] = false,
    };

    /// <summary>Build the request. <paramref name="knownWords"/> should already be a sample.</summary>
    public static LlmRequest Build(
        ScriptRequest request, IEnumerable<string> knownWords,
        IEnumerable<(string Pattern, string Meaning)> grammar, IEnumerable<string> recentTitles)
    {
        StringBuilder prompt = new();
        prompt.AppendLine($"Write a {(request.Kind == ListeningKind.Dialogue ? "dialogue" : "story")} for a learner at JLPT {request.Level}.");
        prompt.AppendLine(request.Topic.Length > 0 ? $"Topic: {request.Topic}" : "Topic: your choice, something everyday.");
        prompt.AppendLine();

        List<string> words = knownWords.Distinct().ToList();
        prompt.AppendLine(words.Count > 0
            ? $"Words the learner knows ({words.Count}, a sample):\n{string.Join("、", words)}"
            : "Words the learner knows: none recorded yet, so use only very basic everyday words.");
        prompt.AppendLine();

        List<(string Pattern, string Meaning)> points = grammar.ToList();
        prompt.AppendLine(points.Count > 0
            ? "Grammar the learner knows or is studying:\n" + string.Join("\n", points.Select(p => $"- {p.Pattern}: {p.Meaning}"))
            : "Grammar the learner knows: basic N5 grammar.");
        prompt.AppendLine();

        List<string> titles = recentTitles.ToList();
        prompt.Append(titles.Count > 0
            ? "Recent titles (pick a different topic):\n" + string.Join("\n", titles.Select(t => $"- {t}"))
            : "Recent titles: none.");

        return new LlmRequest(LlmJob.Generate, LlmFeature.ListeningScript, PromptVersion, System, prompt.ToString(),
            "listening_script", Schema(), MaxTokens: 8000);
    }

    /// <summary>Whether a draft is usable for this kind: null when it is, else what's wrong.</summary>
    public static Func<ScriptDraft, string?> Validator(ListeningKind kind) => draft => Validate(draft, kind);

    public static string? Validate(ScriptDraft draft, ListeningKind kind)
    {
        List<ScriptLineDraft> lines = (draft.Lines ?? new()).Where(l => !string.IsNullOrWhiteSpace(l.Japanese)).ToList();
        string[] speakers = kind == ListeningKind.Dialogue ? new[] { "A", "B" } : new[] { "N" };
        return string.IsNullOrWhiteSpace(draft.Title) ? "no title"
            : lines.Count < MinLines ? $"only {lines.Count} lines"
            : lines.Count > MaxLines ? $"{lines.Count} lines, more than {MaxLines}"
            : lines.FirstOrDefault(l => !speakers.Contains(l.Speaker.Trim().ToUpperInvariant())) is { } odd ? $"unexpected speaker '{odd.Speaker}'"
            : kind == ListeningKind.Dialogue && lines.Select(l => l.Speaker.Trim().ToUpperInvariant()).Distinct().Count() < 2 ? "only one speaker"
            : lines.Any(l => string.IsNullOrWhiteSpace(l.English)) ? "a line with no English"
            : lines.Any(l => GrammarContent.CleanSentence(l.Japanese).Length > ListeningLine.MaxTextLength) ? "a line too long"
            : string.IsNullOrWhiteSpace(draft.Question) ? "no question"
            : draft.AnswerPoints is not { Count: > 0 } || draft.AnswerPoints.All(string.IsNullOrWhiteSpace) ? "no answer points"
            : null;
    }

    /// <summary>The script to save from a validated draft (no id, no audio yet).</summary>
    public static ListeningScript ToScript(ScriptDraft draft, ScriptRequest request, string writer, DateTime now)
    {
        ListeningScript script = new() {
            Kind = request.Kind,
            Title = Trim(draft.Title, ListeningScript.MaxTitleLength),
            TitleEnglish = Trim(draft.TitleEnglish ?? "", ListeningScript.MaxTitleLength),
            Topic = Trim(request.Topic, ListeningScript.MaxTopicLength),
            Level = request.Level,
            Question = Trim(draft.Question, ListeningScript.MaxQuestionLength),
            AnswerPoints = Trim(string.Join("\n", (draft.AnswerPoints ?? new()).Select(p => p.Trim()).Where(p => p.Length > 0)), 2000),
            NewWords = Trim(string.Join("\n", (draft.NewWords ?? new())
                .Where(w => !string.IsNullOrWhiteSpace(w.Word))
                .Select(w => $"{w.Word.Trim()} ({w.Reading.Trim()}): {w.Meaning.Trim()}")), 2000),
            Writer = Trim(writer, ListeningScript.MaxWriterLength),
            CreatedAt = now,
        };
        int order = 0;
        foreach (ScriptLineDraft line in (draft.Lines ?? new()).Where(l => !string.IsNullOrWhiteSpace(l.Japanese))) {
            script.Lines.Add(new ListeningLine {
                SortOrder = ++order,
                Speaker = line.Speaker.Trim().ToUpperInvariant(),
                Text = Trim(GrammarContent.CleanSentence(line.Japanese), ListeningLine.MaxTextLength),
                English = Trim(line.English.Trim(), ListeningLine.MaxEnglishLength),
            });
        }
        return script;
    }

    private static string Trim(string text, int max)
    {
        text = text.Trim();
        return text.Length > max ? text[..max] : text;
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
}
