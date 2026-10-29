using System.Text.Json;
using System.Text.Json.Nodes;
using KotoNeko.Core.Llm;
using Xunit;

namespace KotoNeko.Tests;

// Made-up sentences only.
public class SentenceGenerationTests
{
    private static readonly SentenceTarget Meet = new("会う", "あう", new[] { "to meet" });

    [Fact]
    public void The_Request_Carries_The_Word_Known_Words_Grammar_And_Existing_Sentences()
    {
        LlmRequest request = SentenceGeneration.Build(Meet,
            new[] { "友達", "駅", "会う" },
            new[] { ("〜てから", "after doing") },
            new[] { "友達に会う。" });

        Assert.Equal(LlmJob.Generate, request.Job);
        Assert.Equal(SentenceGeneration.PromptVersion, request.PromptVersion);
        Assert.Contains("会う (あう)", request.Prompt);
        Assert.Contains("to meet", request.Prompt);
        Assert.Contains("友達、駅", request.Prompt);
        Assert.DoesNotContain("駅、会う", request.Prompt);   // the target isn't listed as a known word
        Assert.Contains("〜てから: after doing", request.Prompt);
        Assert.Contains("- 友達に会う。", request.Prompt);
    }

    [Fact]
    public void An_Empty_Pool_Or_Grammar_List_Still_Makes_A_Sensible_Request()
    {
        LlmRequest request = SentenceGeneration.Build(Meet, Array.Empty<string>(), Array.Empty<(string, string)>(), Array.Empty<string>());

        Assert.Contains("none recorded yet", request.Prompt);
        Assert.Contains("basic N5 grammar", request.Prompt);
        Assert.Contains("Existing sentences for this word: none.", request.Prompt);
    }

    [Fact]
    public void The_Schema_Asks_For_Sentences_With_Japanese_And_English()
    {
        JsonObject schema = SentenceGeneration.Schema();
        JsonNode item = schema["properties"]!["sentences"]!["items"]!;

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.Equal(new[] { "japanese", "english" }, item["required"]!.AsArray().Select(n => n!.GetValue<string>()));
        Assert.False(item["additionalProperties"]!.GetValue<bool>());
    }

    [Fact]
    public void An_Answer_Parses_From_The_Schemas_Json()
    {
        GeneratedSentences answer = JsonSerializer.Deserialize<GeneratedSentences>(
            """{"sentences":[{"japanese":"駅で友達に会った。","english":"I met a friend at the station."}]}""")!;

        Assert.Null(SentenceGeneration.Validate(answer));
        Assert.Equal("駅で友達に会った。", Assert.Single(answer.Sentences!).Japanese);
    }

    [Fact]
    public void Answers_Without_Sentences_Are_Rejected()
    {
        Assert.NotNull(SentenceGeneration.Validate(new GeneratedSentences(null)));
        Assert.NotNull(SentenceGeneration.Validate(new GeneratedSentences(new List<GeneratedSentence>())));
        Assert.NotNull(SentenceGeneration.Validate(new GeneratedSentences(new List<GeneratedSentence> { new(" ", "x") })));
    }

    [Fact]
    public void Cleaning_Trims_Removes_Spaces_And_Drops_Repeats()
    {
        GeneratedSentences answer = new(new List<GeneratedSentence> {
            new(" 駅で 友達に会った。 ", " I met a friend. "),
            new("駅で友達に会った。", "Same again."),
            new("友達に会う。", "Already there."),
            new("明日、友達に会う。", ""),
        });

        List<GeneratedSentence> kept = SentenceGeneration.Clean(answer, new[] { "友達に会う。" });

        Assert.Equal(new[] { "駅で友達に会った。", "明日、友達に会う。" }, kept.Select(s => s.Japanese));
        Assert.Equal("I met a friend.", kept[0].English);
    }

    [Fact]
    public void Geminis_Schema_Has_Capitalised_Types_And_No_Additional_Properties()
    {
        JsonNode gemini = LlmSchemas.ForGemini(SentenceGeneration.Schema());
        JsonNode item = gemini["properties"]!["sentences"]!["items"]!;

        Assert.Equal("OBJECT", gemini["type"]!.GetValue<string>());
        Assert.Equal("ARRAY", gemini["properties"]!["sentences"]!["type"]!.GetValue<string>());
        Assert.Equal("STRING", item["properties"]!["japanese"]!["type"]!.GetValue<string>());
        Assert.Null(gemini["additionalProperties"]);
        Assert.Null(item["additionalProperties"]);
        Assert.Equal(2, item["required"]!.AsArray().Count);
    }
}
