using KotoNeko.Core.Domain;
using KotoNeko.Core.Srs;
using Xunit;

namespace KotoNeko.Tests;

public class SrsEngineTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unlock_Enters_Apprentice1_And_Schedules()
    {
        SrsItem item = new();
        SrsEngine.Unlock(item, Now);

        Assert.Equal(SrsStage.Apprentice1, item.Stage);
        Assert.Equal(Now + TimeSpan.FromHours(4), item.NextReviewAt);
        Assert.Equal(Now, item.UnlockedAt);
    }

    [Fact]
    public void Correct_Answer_Advances_One_Stage()
    {
        SrsItem item = new() { Stage = SrsStage.Apprentice2 };
        SrsEngine.ApplyReview(item, incorrectAnswers: 0, Now);

        Assert.Equal(SrsStage.Apprentice3, item.Stage);
        Assert.Equal(1, item.CorrectCount);
        Assert.Equal(Now + TimeSpan.FromHours(23), item.NextReviewAt);
    }

    [Fact]
    public void Reaching_Burned_Clears_Schedule()
    {
        SrsItem item = new() { Stage = SrsStage.Enlightened };
        SrsEngine.ApplyReview(item, incorrectAnswers: 0, Now);

        Assert.Equal(SrsStage.Burned, item.Stage);
        Assert.Null(item.NextReviewAt);
        Assert.Equal(Now, item.BurnedAt);
    }

    [Fact]
    public void Wrong_Answer_Below_Guru_Drops_By_Adjustment_Only()
    {
        // Apprentice4, 1 incorrect: adjustment = ceil(1/2) = 1, factor = 1 -> drop 1.
        SrsItem item = new() { Stage = SrsStage.Apprentice4 };
        SrsEngine.ApplyReview(item, incorrectAnswers: 1, Now);

        Assert.Equal(SrsStage.Apprentice3, item.Stage);
        Assert.Equal(1, item.IncorrectCount);
    }

    [Fact]
    public void Wrong_Answer_At_Guru_Or_Above_Doubles_Penalty()
    {
        // Master (7), 2 incorrect: adjustment = ceil(2/2)=1, factor = 2 -> drop 2 -> Guru1 (5).
        SrsItem item = new() { Stage = SrsStage.Master };
        SrsEngine.ApplyReview(item, incorrectAnswers: 2, Now);

        Assert.Equal(SrsStage.Guru1, item.Stage);
    }

    [Fact]
    public void Wrong_Answer_Never_Falls_Below_Apprentice1()
    {
        SrsItem item = new() { Stage = SrsStage.Apprentice1 };
        SrsEngine.ApplyReview(item, incorrectAnswers: 4, Now);

        Assert.Equal(SrsStage.Apprentice1, item.Stage);
    }
}
