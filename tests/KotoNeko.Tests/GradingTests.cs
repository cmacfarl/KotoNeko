using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Data.Grammar;
using Xunit;

namespace KotoNeko.Tests;

public class GradingTestSetTests
{
    [Fact]
    public void The_Candidates_Are_Well_Formed_And_Carry_No_Labels()
    {
        IReadOnlyList<GradingCandidate> candidates = GradingTestSet.Candidates();
        HashSet<string> grammar = GrammarList.Drafted().Select(g => g.Key).ToHashSet();

        Assert.Equal(candidates.Count, candidates.Select(c => c.Key).Distinct().Count());
        Assert.All(candidates, c => {
            Assert.Contains(c.GrammarKey, grammar);
            Assert.InRange(c.Sentence.Length, 1, GradingTestItem.MaxSentenceLength);
            Assert.InRange(c.Task.Length, 1, GradingTestItem.MaxTaskLength);
        });
        Assert.All(candidates.GroupBy(c => c.GrammarKey), g => Assert.True(g.Count() >= 15));

        // No label may hide in the file: it would bias the person labelling.
        using Stream stream = typeof(GradingTestSet).Assembly.GetManifestResourceStream("KotoNeko.Data.Grammar.grading-candidates.json")!;
        foreach (JsonNode? node in JsonNode.Parse(stream)!.AsArray()) {
            Assert.Equal(new[] { "grammarKey", "key", "sentence", "task" }, node!.AsObject().Select(p => p.Key).Order());
        }
    }

    [Fact]
    public void Seeding_Copies_The_Grammar_Point_Shuffles_And_Can_Run_Twice()
    {
        DateTime now = DateTime.UtcNow;
        List<GradingTestItem> first = GradingTestSet.Plan(GradingTestSet.Candidates(), GrammarList.Drafted(), Array.Empty<string?>(), now);
        GradingTestItem noni = first.Single(i => i.SeedKey == "noni-01");

        Assert.Equal("〜のに", noni.Pattern);
        Assert.Null(noni.Label);
        Assert.False(noni.Excluded);

        // The queue order isn't the drafted order.
        List<string?> byOrder = first.Where(i => i.GrammarKey == "n4-noni").OrderBy(i => i.SortOrder).Select(i => i.SeedKey).ToList();
        Assert.NotEqual(first.Where(i => i.GrammarKey == "n4-noni").Select(i => i.SeedKey), byOrder);

        List<GradingTestItem> again = GradingTestSet.Plan(GradingTestSet.Candidates(), GrammarList.Drafted(), first.Select(i => i.SeedKey), now);
        Assert.Empty(again);
    }
}

public class GradingTests
{
    private static GradingVerdict Verdict(bool target, bool natural = true) =>
        new(target, natural, new List<GradingError>(), "", "", "high");

    [Fact]
    public void The_Request_Names_The_Point_Situation_And_Sentence()
    {
        LlmRequest request = Grading.Build(new GradingTask("〜のに", "even though", "Say you studied but failed.", "勉強したのに、落ちた。"));

        Assert.Equal(LlmJob.Grade, request.Job);
        Assert.Equal(Grading.PromptVersion, request.PromptVersion);
        Assert.Contains("〜のに (even though)", request.Prompt);
        Assert.Contains("Say you studied but failed.", request.Prompt);
        Assert.Contains("勉強したのに、落ちた。", request.Prompt);
        Assert.Contains("doesn't use the target grammar point at all", request.System);
        Assert.Contains("a natural reading of it makes the sentence correct", request.System);
        Assert.Contains("\"Unnatural\" is only for a sentence that is acceptable as it stands", request.System);
    }

    [Fact]
    public void The_Explanation_Comes_Before_The_Verdict()
    {
        // Structured output follows property order: the model reasons, then decides.
        JsonObject schema = Grading.Schema();
        List<string> properties = schema["properties"]!.AsObject().Select(p => p.Key).ToList();
        Assert.Equal("explanation", properties[0]);
        Assert.Equal("explanation", schema["required"]![0]!.GetValue<string>());
    }

    [Fact]
    public void Every_Object_In_The_Schema_Is_Closed()
    {
        JsonObject schema = Grading.Schema();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        Assert.False(schema["properties"]!["errors"]!["items"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Contains("kanji", schema["properties"]!["errors"]!["items"]!["properties"]!["category"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void A_Verdict_Parses_And_Is_Validated()
    {
        GradingVerdict verdict = JsonSerializer.Deserialize<GradingVerdict>("""
            {"target_used_correctly": false, "natural": true,
             "errors": [{"span": "急いでください", "correction": "急いだほうがいい", "category": "target_pattern", "explanation": "のに can't take a request."}],
             "natural_rewrite": "時間がないから、急いでください。", "explanation": "のに can't be followed by a request.", "confidence": "high"}
            """)!;

        Assert.False(verdict.TargetUsedCorrectly);
        Assert.Null(Grading.Validate(verdict));
        Assert.NotNull(Grading.Validate(verdict with { Confidence = "certain" }));
        Assert.NotNull(Grading.Validate(verdict with { Errors = new List<GradingError> { new("a", "b", "spelling", "c") } }));
        Assert.Equal("wrong", GradingEval.Predicted(verdict));
    }

    [Fact]
    public void Scoring_Counts_Agreement_False_Corrections_False_Passes_And_Failures()
    {
        List<GradedItem> items = new() {
            new("p", GradingLabel.Correct, Verdict(true)),                       // agree
            new("p", GradingLabel.Correct, Verdict(false)),                      // false correction
            new("p", GradingLabel.CorrectButUnnatural, Verdict(true, natural: false)), // agree, naturalness agrees
            new("p", GradingLabel.CorrectButUnnatural, Verdict(true, natural: true)),  // agree, naturalness miss
            new("p", GradingLabel.SubtlyWrong, Verdict(true)),                   // false pass
            new("p", GradingLabel.ClearlyWrong, Verdict(false)),                 // agree
            new("p", GradingLabel.SubtlyWrong, null),                            // failed call
        };

        GradingScores scores = GradingEval.Score(items);

        Assert.Equal(7, scores.Items);
        Assert.Equal(1, scores.Failed);
        Assert.Equal(new Rate(4, 6), scores.Agreement);
        Assert.Equal(new Rate(1, 4), scores.FalseCorrection);
        Assert.Equal(new Rate(1, 2), scores.FalsePass);
        Assert.Equal(new Rate(1, 3), scores.NaturalnessMisses);
    }

    [Fact]
    public void Nothing_Graded_Scores_As_Nothing_Rather_Than_Perfect()
    {
        GradingScores scores = GradingEval.Score(new List<GradedItem> { new("p", GradingLabel.Correct, null) });
        Assert.Equal(1, scores.Failed);
        Assert.Equal(0, scores.Agreement.Of);
        Assert.Equal("n/a", scores.Agreement.ToString());
    }
}

public class PrelabellingTests
{
    private const GradingLabel C = GradingLabel.Correct;
    private const GradingLabel U = GradingLabel.CorrectButUnnatural;
    private const GradingLabel S = GradingLabel.SubtlyWrong;
    private const GradingLabel W = GradingLabel.ClearlyWrong;

    [Fact]
    public void All_Agreeing_Gives_That_Label()
    {
        Assert.Equal(new PrelabelResult(PrelabelState.Agreed, C), Prelabelling.Combine(new[] { C, C, C }, 3, C));
        Assert.Equal(new PrelabelResult(PrelabelState.Agreed, U), Prelabelling.Combine(new[] { U, U, U }, 3, U));
    }

    [Fact]
    public void Pass_Against_Fail_Or_Correct_Against_Unnatural_Needs_A_Person()
    {
        Assert.Equal(PrelabelState.Disagree, Prelabelling.Combine(new[] { C, S, C }, 3, C).State);
        Assert.Equal(PrelabelState.Disagree, Prelabelling.Combine(new[] { C, U, C }, 3, C).State);
    }

    [Fact]
    public void Subtly_Against_Clearly_Wrong_Is_Settled_By_Majority_Then_The_Drafted_Intent()
    {
        Assert.Equal(new PrelabelResult(PrelabelState.Agreed, W), Prelabelling.Combine(new[] { W, W, S }, 3, S));
        Assert.Equal(new PrelabelResult(PrelabelState.Agreed, W), Prelabelling.Combine(new[] { W, S }, 2, W));
        Assert.Equal(new PrelabelResult(PrelabelState.Agreed, S), Prelabelling.Combine(new[] { W, S }, 2, null));
    }

    [Fact]
    public void A_Missing_Vote_Leaves_The_Item_Incomplete_Not_For_A_Person()
    {
        Assert.Equal(PrelabelState.Incomplete, Prelabelling.Combine(new[] { C, S }, 3, C).State);
        Assert.Equal(PrelabelState.Incomplete, Prelabelling.Combine(Array.Empty<GradingLabel>(), 0, null).State);
    }

    [Fact]
    public void The_Drafted_Intent_Covers_Every_Candidate()
    {
        IReadOnlyDictionary<string, GradingLabel> intent = GradingTestSet.DraftedIntent();
        HashSet<string> keys = GradingTestSet.Candidates().Select(c => c.Key).ToHashSet();

        Assert.True(keys.SetEquals(intent.Keys));
        Assert.All(GradingTestSet.Candidates().GroupBy(c => c.GrammarKey), g => {
            // Each point has right and wrong sentences to tell apart.
            Assert.Contains(g, c => intent[c.Key].TargetCorrect());
            Assert.Contains(g, c => !intent[c.Key].TargetCorrect());
        });
    }

    [Fact]
    public void The_Labelling_Prompt_Is_Not_The_Graders()
    {
        GradingTask task = new("〜のに", "even though", "Say you studied but failed.", "勉強したのに、落ちた。");
        LlmRequest label = Prelabelling.Build(task, "high");
        LlmRequest grade = Grading.Build(task);

        Assert.Equal(LlmJob.Label, label.Job);
        Assert.Equal("high", label.Effort);
        Assert.NotEqual(grade.System, label.System);
        Assert.Contains("勉強したのに、落ちた。", label.Prompt);
        Assert.Null(Prelabelling.Validate(new PrelabelAnswer("subtly_wrong", "x")));
        Assert.NotNull(Prelabelling.Validate(new PrelabelAnswer("wrong", "x")));
        Assert.Equal(GradingLabel.CorrectButUnnatural, Prelabelling.Parse("correct_but_unnatural"));
    }

    [Fact]
    public void A_Labeller_Option_Parses_With_Or_Without_Effort()
    {
        Assert.Equal(new KotoNeko.Llm.Labeller("Gemini", "gemini-3.8-flash", "low"), KotoNeko.Llm.Labeller.Parse("Gemini:gemini-3.8-flash:low"));
        Assert.Equal(new KotoNeko.Llm.Labeller("LmStudio", "google/gemma-3-12b", null), KotoNeko.Llm.Labeller.Parse("LmStudio:google/gemma-3-12b"));
        Assert.Throws<FormatException>(() => KotoNeko.Llm.Labeller.Parse("Claude"));
    }
}

public class LlmPricingTests
{
    private static KotoNeko.Llm.LlmPricing Pricing() => new(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> {
            ["Llm:Prices:_about:Input"] = "dollars per million input tokens",
            ["Llm:Prices:gemini-4-argon:Input"] = "2",
            ["Llm:Prices:gemini-4-argon:Output"] = "10",
            ["Llm:Prices:claude-haiku-5-5:Input"] = "0.10",
            ["Llm:Prices:claude-haiku-5-5:Output"] = "0.50",
        })
        .Build());

    [Fact]
    public void A_Calls_Cost_Comes_From_The_Per_Million_Prices()
    {
        KotoNeko.Llm.LlmPricing pricing = Pricing();

        Assert.Equal(0.007m, pricing.Cost("gemini-4-argon", 1_000, 500));   // 1,000 × $2/M + 500 × $10/M
        Assert.Equal(0.00035m, pricing.Cost("claude-haiku-5-5", 1_000, 500));
        Assert.Null(pricing.Cost("gemini-3.8-flash", 1_000, 500));          // no price set
        Assert.False(pricing.Knows("_about"));
    }
}
