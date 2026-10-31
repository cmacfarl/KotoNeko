using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;

namespace KotoNeko.Tests;

public class ListeningScriptTests
{
    private static ScriptLineDraft L(string speaker, string japanese, string english = "x") => new(speaker, japanese, english);

    private static ScriptDraft Dialogue(int lines = 8, string question = "Where are they going?") => new(
        "駅で", "At the station",
        Enumerable.Range(0, lines).Select(i => L(i % 2 == 0 ? "A" : "b ", $"これは{i}番目の文です。", $"Line {i}")).ToList(),
        new List<ScriptNewWord> { new("切符", "きっぷ", "ticket") },
        question,
        new List<string> { "To Kyoto", " " });

    [Fact]
    public void The_Request_Carries_Level_Topic_Words_Grammar_And_Recent_Titles()
    {
        LlmRequest request = ListeningScripts.Build(new ScriptRequest(ListeningKind.Dialogue, JlptLevel.N4, "buying a ticket"),
            new[] { "駅", "切符", "駅" }, new[] { ("〜のに", "even though") }, new[] { "At the bakery" });

        Assert.Equal((LlmJob.Generate, LlmFeature.ListeningScript, ListeningScripts.PromptVersion), (request.Job, request.Feature, request.PromptVersion));
        Assert.Contains("Write a dialogue for a learner at JLPT N4.", request.Prompt);
        Assert.Contains("Topic: buying a ticket", request.Prompt);
        Assert.Contains("(2, a sample)", request.Prompt);
        Assert.Contains("- 〜のに: even though", request.Prompt);
        Assert.Contains("- At the bakery", request.Prompt);
        Assert.Contains("don't reproduce or adapt", request.System);
        Assert.Contains("Topic: your choice", ListeningScripts.Build(new ScriptRequest(ListeningKind.Story, JlptLevel.N5, ""),
            Array.Empty<string>(), Array.Empty<(string, string)>(), Array.Empty<string>()).Prompt);
    }

    [Fact]
    public void A_Good_Dialogue_Passes_And_Becomes_A_Script()
    {
        ScriptDraft draft = Dialogue();
        Assert.Null(ListeningScripts.Validate(draft, ListeningKind.Dialogue));

        ListeningScript script = ListeningScripts.ToScript(draft, new ScriptRequest(ListeningKind.Dialogue, JlptLevel.N4, " trains "),
            "Claude · claude-opus-5-5 · listening-script-v1", new DateTime(2026, 10, 31));
        Assert.Equal(8, script.Lines.Count);
        Assert.Equal(new[] { "A", "B" }, script.Lines.Select(l => l.Speaker).Distinct());
        Assert.Equal(Enumerable.Range(1, 8), script.Lines.Select(l => l.SortOrder));
        Assert.Equal("To Kyoto", script.AnswerPoints);
        Assert.Equal("切符 (きっぷ): ticket", script.NewWords);
        Assert.Equal("trains", script.Topic);
        Assert.Equal(JlptLevel.N4, script.Level);
    }

    [Fact]
    public void Bad_Drafts_Are_Refused()
    {
        Assert.NotNull(ListeningScripts.Validate(Dialogue(lines: 3), ListeningKind.Dialogue));
        Assert.NotNull(ListeningScripts.Validate(Dialogue(lines: 20), ListeningKind.Dialogue));
        Assert.NotNull(ListeningScripts.Validate(Dialogue(question: " "), ListeningKind.Dialogue));
        // A dialogue's speakers aren't a story's narrator.
        Assert.Equal("unexpected speaker 'A'", ListeningScripts.Validate(Dialogue(), ListeningKind.Story));

        ScriptDraft oneVoice = Dialogue() with { Lines = Enumerable.Range(0, 8).Select(_ => L("A", "こんにちは。")).ToList() };
        Assert.Equal("only one speaker", ListeningScripts.Validate(oneVoice, ListeningKind.Dialogue));
        ScriptDraft noEnglish = Dialogue() with { Lines = Enumerable.Range(0, 8).Select(i => L(i % 2 == 0 ? "A" : "B", "はい。", "")).ToList() };
        Assert.Equal("a line with no English", ListeningScripts.Validate(noEnglish, ListeningKind.Dialogue));
        ScriptDraft noPoints = Dialogue() with { AnswerPoints = new List<string> { "" } };
        Assert.Equal("no answer points", ListeningScripts.Validate(noPoints, ListeningKind.Dialogue));
    }

    [Fact]
    public void Comprehension_Grading_Sees_The_Script_Question_Points_And_Answer()
    {
        ListeningScript script = ListeningScripts.ToScript(Dialogue(), new ScriptRequest(ListeningKind.Dialogue, JlptLevel.N4, ""), "w", DateTime.UtcNow);
        LlmRequest request = Comprehension.Build(script, "They go to Kyoto");

        Assert.Equal((LlmJob.Grade, LlmFeature.ListeningComprehension), (request.Job, request.Feature));
        Assert.Contains("A: これは0番目の文です。  (Line 0)", request.Prompt);
        Assert.Contains("Question: Where are they going?", request.Prompt);
        Assert.Contains("- To Kyoto", request.Prompt);
        Assert.EndsWith("Learner's answer: They go to Kyoto", request.Prompt);
        Assert.Contains("Be lenient about wording", request.System);

        Assert.Null(Comprehension.Validate(new ComprehensionVerdict("Right.", "partly", null)));
        Assert.NotNull(Comprehension.Validate(new ComprehensionVerdict("Right.", "maybe", null)));
        Assert.Equal(ListeningUnderstanding.Partly, Comprehension.Parse("partly"));
        Assert.Equal(ListeningUnderstanding.No, Comprehension.Parse("no"));
    }

    [Fact]
    public void Script_Models_Are_The_Priced_Ones_Whose_Provider_Is_Set_Up()
    {
        List<ModelChoice> choices = ModelChoices.Available(
            new[] { "claude-opus-5-5", "gemini-3.8-flash", "local-model", "claude-haiku-5-5" },
            provider => provider == "Claude");

        Assert.Equal(new[] { "Claude|claude-opus-5-5", "Claude|claude-haiku-5-5" }, choices.Select(c => c.Key));
        Assert.Equal(new ModelChoice("Gemini", "gemini-3.8-flash"), ModelChoice.Parse("Gemini|gemini-3.8-flash"));
        Assert.Null(ModelChoice.Parse(""));
        Assert.Null(ModelChoice.Parse("Claude|"));
    }
}
