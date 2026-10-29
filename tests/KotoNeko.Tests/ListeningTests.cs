using KotoNeko.Core.Domain;
using KotoNeko.Core.Review;
using KotoNeko.Core.Text;
using Xunit;

namespace KotoNeko.Tests;

public class ListeningTests
{
    private static readonly Homophone Lecture = new("講演", "こうえん", "lecture", "talk;speech");

    private static ReviewQuestion Park() => new()
    {
        Type = QuestionType.Listening,
        Prompt = "Listening",
        ExpectedPrimary = "park",
        ExpectedAlternates = "public garden",
        Homophones = new[] { Lecture },
    };

    [Theory]
    [InlineData("park")]
    [InlineData("public garden")]
    [InlineData("lecture")]
    [InlineData("speech")]
    public void Own_And_Homophone_Meanings_Are_Accepted(string typed)
    {
        Assert.Equal(AnswerJudgment.Correct, Park().Grade(typed));
    }

    [Fact]
    public void Unrelated_Meaning_Is_Incorrect()
    {
        Assert.Equal(AnswerJudgment.Incorrect, Park().Grade("elephant"));
    }

    [Fact]
    public void Matched_Homophone_Is_Reported_Only_For_Homophone_Answers()
    {
        ReviewQuestion q = Park();

        Assert.Null(q.MatchedHomophone("park"));
        Assert.Same(Lecture, q.MatchedHomophone("talk"));
        Assert.Null(q.MatchedHomophone("elephant"));
    }

    [Fact]
    public void Meaning_Questions_Do_Not_Accept_Homophones()
    {
        ReviewQuestion q = new()
        {
            Type = QuestionType.Meaning,
            Prompt = "Meaning",
            ExpectedPrimary = "park",
            Homophones = new[] { Lecture },
        };

        Assert.Equal(AnswerJudgment.Incorrect, q.Grade("lecture"));
    }

    [Theory]
    [InlineData(SpeechSpeed.Slow, 0.8)]
    [InlineData(SpeechSpeed.Normal, 1.0)]
    [InlineData(SpeechSpeed.Fast, 1.2)]
    public void Speech_Speeds_Map_To_Speaking_Rates(SpeechSpeed speed, double rate)
    {
        Assert.Equal(rate, speed.Rate());
    }

    [Fact]
    public void New_Speech_Defaults_To_The_Speed_Existing_Clips_Were_Made_At()
    {
        Assert.Equal(SpeechSpeed.Slow, new StudySettings().SpeechSpeed);
    }
}
