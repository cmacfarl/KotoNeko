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
    [InlineData(SrsStage.Master, true, false)]
    [InlineData(SrsStage.Enlightened, true, false)]
    public void Apprentice_Shows_How_It_Attaches_And_Guru_Leaves_It_To_You(SrsStage stage, bool meaning, bool formation)
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
        Assert.Equal(GrammarReviewEffect.Dropped, session.Record(Verdict(false)));
        Assert.Equal(GrammarReviewEffect.Practice, session.Record(Verdict(true)));
        Assert.False(session.LeaveForLater());
        Assert.True(session.Next());

        Assert.Equal(2, session.Current);
        Assert.Equal(GrammarReviewEffect.Advanced, session.Record(Verdict(true)));
        Assert.True(session.Next());

        Assert.True(session.LeaveForLater());
        Assert.Null(session.Current);
        Assert.Equal((2, 1, 1, 0), (session.Reviewed, session.Passed, session.LeftForLater, session.Remaining));
        Assert.Throws<InvalidOperationException>(() => session.Record(Verdict(true)));
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
