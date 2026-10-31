using System.Text.Json.Nodes;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Llm;

namespace KotoNeko.Tests;

// Made-up sentences only.
public class ModelChoiceTests
{
    /// <summary>One request per use case, built as the app builds it.</summary>
    private static IEnumerable<LlmRequest> RequestsFor(LlmFeature feature)
    {
        GradingTask task = new("〜のに", "even though", "", "勉強したのに、落ちた。");
        switch (feature) {
            case LlmFeature.PracticeSentences:
                yield return SentenceGeneration.Build(new SentenceTarget("会う", "あう", new[] { "to meet" }),
                    Array.Empty<string>(), Array.Empty<(string, string)>(), Array.Empty<string>());
                break;
            case LlmFeature.GrammarDrafting:
                yield return GrammarDrafting.Build(new GrammarDraftTarget("〜のに", "even though", JlptLevel.N4),
                    Array.Empty<string>(), Array.Empty<(string, string)>(), Array.Empty<string>());
                break;
            case LlmFeature.LessonGrading or LlmFeature.ReviewGrading:
                yield return Grading.Build(task, feature);
                break;
            case LlmFeature.SecondOpinion or LlmFeature.Dispute:
                yield return Grading.BuildRecheck(task, feature);
                break;
            case LlmFeature.WhyQuestion:
                yield return Explaining.Build(task, new GradingVerdict(false, true, null, "勉強したのに、落ちた。", "The tense is off.", "high"),
                    Array.Empty<WhyExchange>(), "Why?");
                break;
            case LlmFeature.ListeningScript:
                yield return ListeningScripts.Build(new ScriptRequest(ListeningKind.Dialogue, JlptLevel.N4, ""),
                    Array.Empty<string>(), Array.Empty<(string, string)>(), Array.Empty<string>());
                break;
            case LlmFeature.ListeningComprehension:
                yield return Comprehension.Build(new ListeningScript { Title = "駅で", Question = "Where do they go?" }, "Kyoto");
                break;
            case LlmFeature.CasualRewrite:
                yield return CasualRewrite.Build(new[] { new CasualSource(1, "駅に行きます。", "I go to the station.") });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(feature), feature, "No request for this use case in the test");
        }
    }

    public static TheoryData<LlmFeature> UseCases()
    {
        TheoryData<LlmFeature> data = new();
        foreach (ModelUseCase useCase in ModelUseCases.All) {
            data.Add(useCase.Feature);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(UseCases))]
    public void A_Use_Cases_Recommended_Job_Is_The_Job_Its_Requests_Use(LlmFeature feature)
    {
        ModelUseCase useCase = ModelUseCases.For(feature)!;
        foreach (LlmRequest request in RequestsFor(feature)) {
            Assert.Equal(feature, request.Feature);
            Assert.Equal(useCase.Job, request.Job);
            Assert.Equal(useCase.Job, LlmService.JobOf(feature));
        }
    }

    [Theory]
    [MemberData(nameof(UseCases))]
    public void Every_Schema_Suits_Strict_Structured_Output(LlmFeature feature)
    {
        // OpenAI's strict mode (and LM Studio's) rejects an object that allows extra
        // properties or leaves one out of required: a 400 on the first real call.
        foreach (LlmRequest request in RequestsFor(feature)) {
            AssertStrict(request.Schema, request.SchemaName);
        }
        AssertStrict(Prelabelling.Build(new GradingTask("p", "m", "", "s"), null).Schema, "prelabel");
    }

    private static void AssertStrict(JsonNode? node, string path)
    {
        if (node is JsonArray array) {
            for (int i = 0; i < array.Count; i++) {
                AssertStrict(array[i], $"{path}[{i}]");
            }
            return;
        }
        if (node is not JsonObject obj) {
            return;
        }
        if (obj["properties"] is JsonObject properties) {
            Assert.True(obj["additionalProperties"]?.GetValue<bool>() == false, $"{path} allows extra properties");
            HashSet<string> required = (obj["required"] as JsonArray ?? new JsonArray()).Select(r => r!.GetValue<string>()).ToHashSet();
            foreach (string name in properties.Select(p => p.Key)) {
                Assert.True(required.Contains(name), $"{path}.{name} isn't required");
            }
        }
        foreach (KeyValuePair<string, JsonNode?> child in obj) {
            AssertStrict(child.Value, $"{path}.{child.Key}");
        }
    }

    [Fact]
    public void Every_Feature_A_Request_Can_Have_Is_A_Use_Case_Or_Left_To_Configuration()
    {
        HashSet<LlmFeature> console = new() { LlmFeature.Unknown, LlmFeature.GradingEval, LlmFeature.Prelabelling };
        foreach (LlmFeature feature in Enum.GetValues<LlmFeature>()) {
            Assert.True(console.Contains(feature) ^ ModelUseCases.For(feature) is not null, $"{feature}");
        }
        Assert.Equal(LlmJob.Label, LlmService.JobOf(LlmFeature.Prelabelling));
        Assert.Equal(LlmJob.Grade, LlmService.JobOf(LlmFeature.GradingEval));
    }

    [Fact]
    public void Models_Are_Offered_By_Provider_From_Their_Ids()
    {
        Assert.Equal("Claude", ModelChoices.ProviderOf("claude-opus-5-5"));
        Assert.Equal("Gemini", ModelChoices.ProviderOf("gemini-3.8-flash"));
        Assert.Equal("OpenAI", ModelChoices.ProviderOf("gpt-6.1-sol"));
        Assert.Null(ModelChoices.ProviderOf("google/gemma-3-12b"));

        List<ModelChoice> all = ModelChoices.All(new[] { "gpt-6-luna", "local-model", "claude-haiku-5-5" });
        Assert.Equal(new[] { "OpenAI|gpt-6-luna", "Claude|claude-haiku-5-5" }, all.Select(c => c.Key));
        Assert.Equal(new[] { "Claude|claude-haiku-5-5" },
            ModelChoices.Available(new[] { "gpt-6-luna", "claude-haiku-5-5" }, p => p == "Claude").Select(c => c.Key));
        Assert.Equal(new ModelChoice("OpenAI", "gpt-6-luna"), ModelChoice.Parse("OpenAI|gpt-6-luna"));
        Assert.Null(ModelChoice.Parse("OpenAI|"));
    }

    [Fact]
    public void The_OpenAI_Request_Asks_For_Strict_Json_Without_Storing_It()
    {
        LlmRequest request = CasualRewrite.Build(new[] { new CasualSource(1, "駅に行きます。", "I go to the station.") });
        JsonObject body = OpenAIProvider.Body(request, "gpt-6-luna", "medium");

        Assert.Equal("gpt-6-luna", body["model"]!.GetValue<string>());
        Assert.Equal(request.System, body["instructions"]!.GetValue<string>());
        Assert.Equal(request.Prompt, body["input"]!.GetValue<string>());
        JsonNode format = body["text"]!["format"]!;
        Assert.Equal("json_schema", format["type"]!.GetValue<string>());
        Assert.Equal(request.SchemaName, format["name"]!.GetValue<string>());
        Assert.True(format["strict"]!.GetValue<bool>());
        Assert.Equal(request.Schema.ToJsonString(), format["schema"]!.ToJsonString());
        Assert.Equal(request.MaxTokens, body["max_output_tokens"]!.GetValue<int>());
        Assert.False(body["store"]!.GetValue<bool>());
        Assert.Equal("medium", body["reasoning"]!["effort"]!.GetValue<string>());

        Assert.Equal("low", OpenAIProvider.Effort("none"));
        Assert.Equal("low", OpenAIProvider.Effort("minimal"));
        Assert.Equal("high", OpenAIProvider.Effort("high"));
    }

    [Fact]
    public void An_OpenAI_Answer_Is_The_Messages_Text_With_Reasoning_Counted_As_Output()
    {
        JsonNode response = JsonNode.Parse("""
            {
              "status": "completed",
              "output": [
                { "type": "reasoning", "summary": [] },
                { "type": "message", "role": "assistant", "content": [ { "type": "output_text", "text": "{\"answer\":\"Because\"}" } ] }
              ],
              "usage": { "input_tokens": 81, "output_tokens": 11, "total_tokens": 92, "output_tokens_details": { "reasoning_tokens": 4 } }
            }
            """)!;

        LlmResponse answer = OpenAIProvider.Parse(response);

        Assert.Equal("{\"answer\":\"Because\"}", answer.Json);
        Assert.Equal(81, answer.InputTokens);
        Assert.Equal(11, answer.OutputTokens);
    }

    [Fact]
    public void An_OpenAI_Refusal_Or_Cut_Off_Answer_Is_Reported()
    {
        JsonNode refused = JsonNode.Parse("""
            { "status": "completed", "output": [ { "type": "message", "content": [ { "type": "refusal", "refusal": "I can't help with that." } ] } ] }
            """)!;
        Assert.Equal(LlmOutcome.Refused, Assert.Throws<LlmException>(() => OpenAIProvider.Parse(refused)).Outcome);

        JsonNode cutOff = JsonNode.Parse("""{ "status": "incomplete", "incomplete_details": { "reason": "max_output_tokens" }, "output": [] }""")!;
        Assert.Equal(LlmOutcome.BadOutput, Assert.Throws<LlmException>(() => OpenAIProvider.Parse(cutOff)).Outcome);

        JsonNode filtered = JsonNode.Parse("""{ "status": "incomplete", "incomplete_details": { "reason": "content_filter" }, "output": [] }""")!;
        Assert.Equal(LlmOutcome.Refused, Assert.Throws<LlmException>(() => OpenAIProvider.Parse(filtered)).Outcome);
    }
}
