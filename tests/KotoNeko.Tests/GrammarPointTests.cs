using System.Text.Json;
using System.Text.Json.Nodes;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;

namespace KotoNeko.Tests;

// Made-up content only.
public class GrammarPointTests
{
    private static GrammarPoint Ready() => new() {
        Explanation = "Shows an outcome that goes against expectations.",
        Formation = "Verb (plain) + のに",
        Situations = { new GrammarSituation("You studied hard, but failed, and you're upset.", "Verb (past) + のに") },
        Examples = { new GrammarExample { Text = "勉強したのに、落ちた。", Status = GrammarContentStatus.Approved } },
    };

    [Fact]
    public void A_Grammar_Point_Is_A_Grammar_Study_Item()
    {
        Assert.Equal(StudyItemKind.Grammar, new GrammarPoint().Kind);
    }

    [Fact]
    public void A_Point_With_Content_An_Approved_Example_And_A_Situation_Can_Be_Approved()
    {
        Assert.Empty(GrammarContent.ApprovalProblems(Ready()));
    }

    [Fact]
    public void Approval_Lists_What_Is_Missing()
    {
        GrammarPoint point = Ready();
        point.Explanation = " ";
        point.Situations.Clear();
        point.Examples[0].Status = GrammarContentStatus.Draft;

        List<string> problems = GrammarContent.ApprovalProblems(point);

        Assert.Equal(3, problems.Count);
        Assert.Contains(problems, p => p.Contains("explanation"));
        Assert.Contains(problems, p => p.Contains("approved example"));
        Assert.Contains(problems, p => p.Contains("idea"));
    }

    [Fact]
    public void Cleaning_Drops_Blank_Rows_And_Spaces_In_Sentences()
    {
        Assert.Equal(new[] { new GrammarContrast("〜けど", "Neutral contrast.") },
            GrammarContent.Clean(new[] { new GrammarContrast(" 〜けど ", " Neutral contrast. "), new GrammarContrast("", "orphan") }));
        Assert.Equal("雨なのに、出かけた。",
            GrammarContent.Clean(new[] { new GrammarMistake("雨 なのに、 出かけた。", "", "") }).Single().Wrong);
        Assert.Empty(GrammarContent.Clean(new[] { new GrammarSituation("  ", "hint") }));
    }

    [Fact]
    public void How_It_Attaches_Is_A_List_Of_Rules()
    {
        Assert.Equal(new[] { "Verb (plain) + のに", "Noun / な-adjective + な + のに" },
            GrammarContent.FormationRules(" Verb (plain) + のに \r\n\n Noun / な-adjective + な + のに "));
        // A single line of rules joined with semicolons, as the first drafts were.
        Assert.Equal(new[] { "Verb (plain) + のに", "い-adjective + のに", "Noun + な + のに (past: Noun + だった + のに)" },
            GrammarContent.FormationRules("Verb (plain) + のに; い-adjective + のに; Noun + な + のに (past: Noun + だった + のに)"));
        Assert.Empty(GrammarContent.FormationRules(null));
    }

    [Fact]
    public void Content_Lists_Round_Trip_As_Json()
    {
        // How the GrammarPoints columns store them.
        List<GrammarMistake> mistakes = new() { new("寒いのに、着なさい。", "寒いから、着なさい。", "のに can't be followed by an order.") };
        string json = JsonSerializer.Serialize(mistakes);

        Assert.Contains("\"wrong\"", json);
        Assert.Equal(mistakes, JsonSerializer.Deserialize<List<GrammarMistake>>(json));
    }

    [Fact]
    public void The_Drafting_Request_Carries_The_Point_Known_Words_Grammar_And_Existing_Examples()
    {
        LlmRequest request = GrammarDrafting.Build(new GrammarDraftTarget("〜のに", "even though", JlptLevel.N4),
            new[] { "勉強", "試験", "勉強" }, new[] { ("〜から", "because") }, new[] { "勉強したのに、落ちた。" });

        Assert.Equal(LlmJob.Generate, request.Job);
        Assert.Equal(GrammarDrafting.PromptVersion, request.PromptVersion);
        Assert.Equal(LlmFeature.GrammarDrafting, request.Feature);
        Assert.Contains("〜のに (even though), JLPT N4", request.Prompt);
        Assert.Contains("(2, a sample)", request.Prompt);
        Assert.Contains("- 〜から: because", request.Prompt);
        Assert.Contains("- 勉強したのに、落ちた。", request.Prompt);
        Assert.Contains("don't reproduce", request.System);
        Assert.Contains("these aren't sentences to translate", request.System);
    }

    [Fact]
    public void Every_Object_In_The_Drafting_Schema_Is_Closed_And_Required()
    {
        JsonObject schema = GrammarDrafting.Schema();
        Assert.False(schema["additionalProperties"]!.GetValue<bool>());
        foreach (string list in new[] { "contrasts", "mistakes", "examples", "situations" }) {
            JsonNode items = schema["properties"]![list]!["items"]!;
            Assert.False(items["additionalProperties"]!.GetValue<bool>());
            Assert.Equal(items["properties"]!.AsObject().Count, items["required"]!.AsArray().Count);
        }
        Assert.Equal(7, schema["required"]!.AsArray().Count);
    }

    [Fact]
    public void A_Draft_Parses_And_Is_Validated()
    {
        GrammarDraft draft = JsonSerializer.Deserialize<GrammarDraft>("""
            {"explanation": "Even though.", "formation": ["Verb (plain) + のに", "Noun + な + のに"], "formality": "neutral",
             "contrasts": [{"pattern": "〜けど", "difference": "Neutral."}],
             "mistakes": [{"wrong": "寒いのに、着なさい。", "right": "寒いから、着なさい。", "why": "No orders after のに."}],
             "examples": [{"japanese": "勉強したのに、落ちた。", "english": "I studied, but failed."}],
             "situations": [{"situation": "You studied but failed, and you're upset.", "hint": "Verb (past) + のに"}]}
            """)!;

        Assert.Null(GrammarDrafting.Validate(draft));
        Assert.Equal("〜けど", draft.Contrasts![0].Pattern);
        Assert.Equal("Verb (plain) + のに\nNoun + な + のに", GrammarContent.JoinFormation(draft.Formation!));
        Assert.NotNull(GrammarDrafting.Validate(draft with { Formation = new List<string> { " " } }));
        Assert.NotNull(GrammarDrafting.Validate(draft with { Formality = "very polite" }));
        Assert.NotNull(GrammarDrafting.Validate(draft with { Examples = new List<DraftedExample>() }));
        Assert.NotNull(GrammarDrafting.Validate(draft with { Situations = null }));
        Assert.Equal(GrammarFormality.Polite, GrammarDrafting.Formality("polite"));
    }

    [Fact]
    public void Drafted_Examples_Are_Tidied_And_Only_New_Ones_Kept()
    {
        GrammarDraft draft = new("x", new List<string> { "y" }, "neutral", null, null, new List<DraftedExample> {
            new("勉強したのに、 落ちた。", " I studied, but failed. "),
            new("勉強したのに、落ちた。", "duplicate"),
            new("雨なのに、出かけた。", "It rained, but I went out."),
            new("  ", "blank"),
        }, null);

        List<DraftedExample> kept = GrammarDrafting.CleanExamples(draft, new[] { "雨なのに、出かけた。" });

        Assert.Equal(new[] { new DraftedExample("勉強したのに、落ちた。", "I studied, but failed.") }, kept);
    }
}
