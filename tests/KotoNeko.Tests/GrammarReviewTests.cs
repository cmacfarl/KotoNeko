using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Core.Review;
using KotoNeko.Core.Srs;

namespace KotoNeko.Tests;

public class GrammarReviewTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    private static GradingVerdict Verdict(bool correct, string confidence = "high") =>
        new(correct, true, null, "", "x", confidence);

    [Theory]
    [InlineData(SrsStage.Apprentice1, true, true)]
    [InlineData(SrsStage.Apprentice4, true, true)]
    [InlineData(SrsStage.Guru1, true, false)]
    [InlineData(SrsStage.Guru2, true, false)]
    [InlineData(SrsStage.Master, false, false)]
    [InlineData(SrsStage.Enlightened, false, false)]
    public void Cues_Thin_Out_Apprentice_Guru_Then_The_Pattern_Alone(SrsStage stage, bool meaning, bool formation)
    {
        Assert.Equal(new GrammarCues(meaning, formation), GrammarReview.CuesFor(stage));
    }

    [Theory]
    [InlineData(true, "low", GrammarReviewEffect.Advanced)]
    [InlineData(false, "high", GrammarReviewEffect.Dropped)]
    [InlineData(false, "medium", GrammarReviewEffect.Dropped)]
    [InlineData(false, "low", GrammarReviewEffect.Kept)]
    public void Only_A_Confident_Miss_Costs_A_Stage(bool correct, string confidence, GrammarReviewEffect effect)
    {
        Assert.Equal(effect, GrammarReview.Decide(Verdict(correct, confidence)));
    }

    [Fact]
    public void Applying_Effects_Moves_The_Point_As_The_Srs_Would()
    {
        SrsItem passed = new() { Stage = SrsStage.Apprentice4 };
        GrammarReview.Apply(passed, GrammarReviewEffect.Advanced, Now);
        Assert.Equal(SrsStage.Guru1, passed.Stage);

        SrsItem dropped = new() { Stage = SrsStage.Guru2 };
        GrammarReview.Apply(dropped, GrammarReviewEffect.Dropped, Now);
        Assert.Equal(SrsStage.Apprentice4, dropped.Stage);
        Assert.Equal(1, dropped.IncorrectCount);

        SrsItem kept = new() { Stage = SrsStage.Guru1, NextReviewAt = Now.AddHours(-1) };
        GrammarReview.Apply(kept, GrammarReviewEffect.Kept, Now);
        Assert.Equal(SrsStage.Guru1, kept.Stage);
        Assert.Equal(Now + SrsSchedule.Intervals[SrsStage.Guru1], kept.NextReviewAt);
        Assert.Equal((0, 0), (kept.CorrectCount, kept.IncorrectCount));

        SrsItem practice = new() { Stage = SrsStage.Guru1, NextReviewAt = Now };
        GrammarReview.Apply(practice, GrammarReviewEffect.Practice, Now.AddDays(1));
        Assert.Equal((SrsStage.Guru1, (DateTime?)Now), (practice.Stage, practice.NextReviewAt));
    }

    [Fact]
    public void Repeating_A_Burned_Or_Locked_Item_Does_Nothing()
    {
        SrsItem burned = new() { Stage = SrsStage.Burned, BurnedAt = Now };
        SrsEngine.Repeat(burned, Now.AddDays(1));
        Assert.Equal((SrsStage.Burned, (DateTime?)null), (burned.Stage, burned.NextReviewAt));
    }

    [Theory]
    [InlineData("勉強したのに、落ちた。", "勉強したのに落ちた")]
    [InlineData("勉強したのに 落ちた！", "勉強したのに、落ちた。")]
    [InlineData("ＡＢＣのに。", "ABCのに")]
    public void A_Repeat_Ignores_Spaces_Punctuation_And_Width(string sentence, string earlier)
    {
        Assert.True(GrammarReview.IsRepeat(sentence, new[] { "別の文。", earlier }));
    }

    [Fact]
    public void A_Different_Sentence_Or_An_Empty_One_Is_Not_A_Repeat()
    {
        Assert.False(GrammarReview.IsRepeat("勉強したのに、合格しなかった。", new[] { "勉強したのに、落ちた。" }));
        Assert.False(GrammarReview.IsRepeat("。", new[] { "、" }));
        Assert.Equal("カーテン", GrammarReview.Normalise("カーテン。"));
    }

    [Fact]
    public void The_First_Verdict_Decides_And_Rewrites_Are_Practice()
    {
        TutorSession session = new(new[] { 1, 2, 2, 3 });
        Assert.Equal((3, 1), (session.Total, session.Current));

        Assert.False(session.Next());
        Assert.Equal(GrammarReviewEffect.Dropped, session.Record(GrammarReviewEffect.Dropped));
        Assert.Equal(GrammarReviewEffect.Practice, session.Record(GrammarReviewEffect.Advanced));
        Assert.False(session.LeaveForLater());
        Assert.True(session.Next());

        Assert.Equal(2, session.Current);
        Assert.Equal(GrammarReviewEffect.Advanced, session.Record(GrammarReviewEffect.Advanced));
        Assert.True(session.Next());

        Assert.True(session.LeaveForLater());
        Assert.Null(session.Current);
        Assert.Equal((2, 1, 1, 0), (session.Reviewed, session.Passed, session.LeftForLater, session.Remaining));
        Assert.Throws<InvalidOperationException>(() => session.Record(GrammarReviewEffect.Advanced));
    }

    [Fact]
    public void An_Overturned_Miss_Counts_As_A_Pass_In_The_Session()
    {
        TutorSession session = new(new[] { 1 });
        session.Record(GrammarReviewEffect.Kept);
        session.Overturned();

        Assert.Equal((GrammarReviewEffect?)GrammarReviewEffect.Advanced, session.Decided);
        Assert.Equal(1, session.Passed);
        session.Overturned();
        Assert.Equal(1, session.Passed);
    }

    public static TheoryData<bool, string, bool?, string?, GrammarReviewEffect> SecondOpinions => new() {
        { true, "high", null, null, GrammarReviewEffect.Advanced },
        { false, "high", null, null, GrammarReviewEffect.Dropped },
        { false, "low", true, "medium", GrammarReviewEffect.Advanced },
        { false, "low", false, "high", GrammarReviewEffect.Dropped },
        { false, "low", false, "low", GrammarReviewEffect.Kept },
        // No second opinion to be had (the call failed, or no Recheck route).
        { false, "low", null, null, GrammarReviewEffect.Kept },
        // A second opinion only matters for an unsure miss.
        { false, "high", true, "high", GrammarReviewEffect.Dropped },
    };

    [Theory]
    [MemberData(nameof(SecondOpinions))]
    public void A_Second_Opinion_Decides_An_Unsure_Miss(
        bool first, string firstConfidence, bool? second, string? secondConfidence, GrammarReviewEffect effect)
    {
        GradingVerdict? secondVerdict = second is bool s ? Verdict(s, secondConfidence!) : null;
        Assert.Equal(effect, GrammarReview.Decide(Verdict(first, firstConfidence), secondVerdict));
        Assert.Equal(!first && firstConfidence == "low", GrammarReview.NeedsSecondOpinion(Verdict(first, firstConfidence)));
    }

    [Theory]
    [InlineData(GrammarReviewEffect.Advanced, GrammarReviewEffect.Advanced, GrammarReviewEffect.Advanced)]
    [InlineData(GrammarReviewEffect.Advanced, GrammarReviewEffect.Kept, GrammarReviewEffect.Kept)]
    [InlineData(GrammarReviewEffect.Kept, GrammarReviewEffect.Dropped, GrammarReviewEffect.Dropped)]
    [InlineData(GrammarReviewEffect.Dropped, GrammarReviewEffect.Advanced, GrammarReviewEffect.Dropped)]
    public void A_Two_Sentence_Review_Passes_Only_If_Both_Do(GrammarReviewEffect a, GrammarReviewEffect b, GrammarReviewEffect combined)
    {
        Assert.Equal(combined, GrammarReview.Combine(new[] { a, b }));
    }

    [Fact]
    public void Overturning_A_Dropped_Review_Undoes_The_Drop_And_Advances_From_The_Review()
    {
        // Guru II, missed confidently: dropped two stages to Apprentice IV.
        DateTime reviewed = Now.AddMinutes(-5);
        SrsItem srs = new() { Stage = SrsStage.Guru2, CorrectCount = 5, IncorrectCount = 1 };
        GrammarReview.Apply(srs, GrammarReviewEffect.Dropped, reviewed);
        Assert.Equal((SrsStage.Apprentice4, 2), (srs.Stage, srs.IncorrectCount));

        Assert.True(SrsEngine.OverturnMiss(srs, SrsStage.Guru2, SrsStage.Apprentice4, missCounted: true, reviewed));
        Assert.Equal(SrsStage.Master, srs.Stage);
        Assert.Equal((6, 1), (srs.CorrectCount, srs.IncorrectCount));
        Assert.Equal(reviewed + SrsSchedule.Intervals[SrsStage.Master], srs.NextReviewAt);
    }

    [Fact]
    public void Overturning_A_Kept_Review_Takes_Back_No_Miss()
    {
        SrsItem srs = new() { Stage = SrsStage.Guru1, CorrectCount = 4, IncorrectCount = 2 };
        GrammarReview.Apply(srs, GrammarReviewEffect.Kept, Now);

        Assert.True(SrsEngine.OverturnMiss(srs, SrsStage.Guru1, SrsStage.Guru1, missCounted: false, Now));
        Assert.Equal((SrsStage.Guru2, 5, 2), (srs.Stage, srs.CorrectCount, srs.IncorrectCount));
    }

    [Fact]
    public void A_Point_That_Moved_On_Is_Not_Overturned()
    {
        SrsItem srs = new() { Stage = SrsStage.Apprentice3, IncorrectCount = 1, NextReviewAt = Now };

        Assert.False(SrsEngine.OverturnMiss(srs, SrsStage.Guru2, SrsStage.Apprentice4, missCounted: true, Now.AddDays(-1)));
        Assert.Equal((SrsStage.Apprentice3, 1, (DateTime?)Now), (srs.Stage, srs.IncorrectCount, srs.NextReviewAt));
    }

    [Theory]
    [InlineData("〜けど", "けど")]
    [InlineData("～ても", "ても")]
    [InlineData("〜 ために", "ために")]
    public void Patterns_Match_Without_The_Wave_Dash(string pattern, string normalised)
    {
        Assert.Equal(normalised, GrammarReview.NormalisePattern(pattern));
    }

    [Fact]
    public void A_Contrast_Is_One_You_Can_Use()
    {
        GrammarContrast[] contrasts = {
            new("けど", "just contrasts"),
            new("くせに", "blames someone"),
            new("ものの", "formal"),
        };
        GrammarListPoint[] list = {
            new("〜けど", "but", JlptLevel.N5, GrammarStatus.NotYet),
            new("〜くせに", "even though (critical)", JlptLevel.N3, GrammarStatus.NotYet),
        };

        // けど: on the list and easier than N4. くせに: harder and not started. ものの: not on the list.
        ContrastChoice? choice = GrammarReview.ChooseContrast(JlptLevel.N4, contrasts, list, new Random(1));
        Assert.Equal(new ContrastChoice("けど", "but", "just contrasts"), choice);

        GrammarListPoint[] studying = { new("〜くせに", "even though (critical)", JlptLevel.N3, GrammarStatus.Studying) };
        Assert.Equal("くせに", GrammarReview.ChooseContrast(JlptLevel.N4, contrasts.Skip(1).ToList(), studying, new Random(1))!.Pattern);
    }

    [Fact]
    public void An_Unlisted_Contrast_Is_A_Fallback_And_None_Means_None()
    {
        GrammarContrast[] unlisted = { new("ものの", "formal") };
        ContrastChoice? choice = GrammarReview.ChooseContrast(JlptLevel.N4, unlisted, Array.Empty<GrammarListPoint>(), new Random(1));
        Assert.Equal("ものの", choice!.Pattern);
        Assert.DoesNotContain("formal", choice.Meaning);

        Assert.Null(GrammarReview.ChooseContrast(JlptLevel.N4, Array.Empty<GrammarContrast>(), Array.Empty<GrammarListPoint>(), new Random(1)));
        GrammarListPoint[] tooHard = { new("〜くせに", "x", JlptLevel.N3, GrammarStatus.NotYet) };
        Assert.Null(GrammarReview.ChooseContrast(JlptLevel.N4, new[] { new GrammarContrast("くせに", "x") }, tooHard, new Random(1)));
    }

    [Fact]
    public void A_Second_Opinion_Is_Independent_And_Not_Swayed_By_The_Comment()
    {
        GradingTask task = new("〜のに", "even though", "", "勉強したのに、落ちた。", "I studied but failed");
        LlmRequest request = Grading.BuildRecheck(task, LlmFeature.Dispute, "I meant it as a complaint");

        Assert.Equal((LlmJob.Recheck, LlmFeature.Dispute, Grading.RecheckPromptVersion), (request.Job, request.Feature, request.PromptVersion));
        Assert.Contains("never pass a sentence because the learner insists", request.System);
        Assert.Contains("target_used_correctly is true only if", request.System);
        Assert.Contains("The learner's comment on why they wrote it this way: I meant it as a complaint", request.Prompt);
        Assert.Contains("Learner's sentence: 勉強したのに、落ちた。", request.Prompt);
        Assert.DoesNotContain("learner's comment", Grading.BuildRecheck(task, LlmFeature.SecondOpinion).Prompt);
    }

    [Fact]
    public void A_Why_Question_Carries_The_Verdict_And_Earlier_Answers()
    {
        GradingTask task = new("〜のに", "even though", "", "勉強したのに、落ちた。");
        GradingVerdict verdict = new(false, true, null, "勉強したのに、落ちた。", "The tense is off.", "high");
        LlmRequest request = Explaining.Build(task, verdict, new[] { new WhyExchange("Why past?", "Because the test is over.") }, "And here?");

        Assert.Equal((LlmJob.Explain, LlmFeature.WhyQuestion), (request.Job, request.Feature));
        Assert.Contains("\"target_used_correctly\":false", request.Prompt);
        Assert.Contains("The tense is off.", request.Prompt);
        Assert.Contains("Earlier question: Why past?", request.Prompt);
        Assert.EndsWith("Question: And here?", request.Prompt);
        Assert.Contains("Don't change the verdict", request.System);
        Assert.Null(Explaining.Validate(new WhyAnswer("Because.")));
        Assert.NotNull(Explaining.Validate(new WhyAnswer(" ")));
    }

    [Fact]
    public void A_Free_Sentence_Carries_What_The_Learner_Means()
    {
        LlmRequest request = Grading.Build(new GradingTask("〜のに", "even though", "", "勉強したのに、落ちた。", "I studied but failed"),
            LlmFeature.ReviewGrading);

        Assert.Equal(LlmFeature.ReviewGrading, request.Feature);
        Assert.Contains("Situation given to the learner: (none: free sentence)", request.Prompt);
        Assert.Contains("What the learner says they mean: I studied but failed", request.Prompt);
        Assert.Contains("A free sentence (no situation) is the learner's own choice", request.System);
        Assert.Contains("When the learner says what they mean, judge the target grammar point against that meaning", request.System);
        Assert.Contains("Other differences from what they meant, such as a wrong word, don't make it", request.System);

        LlmRequest unsaid = Grading.Build(new GradingTask("〜のに", "even though", "", "勉強したのに、落ちた。"), LlmFeature.ReviewGrading);
        Assert.Contains("What the learner says they mean: (not given)", unsaid.Prompt);
    }
}
