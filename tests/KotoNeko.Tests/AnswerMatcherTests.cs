using KotoNeko.Core.Text;
using Xunit;

namespace KotoNeko.Tests;

public class AnswerMatcherTests
{
    [Fact]
    public void Meaning_Exact_Is_Correct()
    {
        Assert.Equal(AnswerJudgment.Correct, AnswerMatcher.GradeMeaning("newspaper", "newspaper", null));
    }

    [Fact]
    public void Meaning_Minor_Typo_Is_Accepted()
    {
        Assert.Equal(AnswerJudgment.Correct, AnswerMatcher.GradeMeaning("newspapr", "newspaper", null));
    }

    [Fact]
    public void Meaning_Ignores_Case_And_Articles()
    {
        Assert.Equal(AnswerJudgment.Correct, AnswerMatcher.GradeMeaning("To Eat", "eat", null));
    }

    [Fact]
    public void Meaning_Alternate_Is_Accepted()
    {
        Assert.Equal(AnswerJudgment.Correct, AnswerMatcher.GradeMeaning("speak", "talk", "speak; say"));
    }

    [Fact]
    public void Meaning_Wildly_Wrong_Is_Incorrect()
    {
        Assert.Equal(AnswerJudgment.Incorrect, AnswerMatcher.GradeMeaning("mountain", "newspaper", null));
    }

    [Fact]
    public void Reading_Romaji_Converted_And_Matched()
    {
        Assert.Equal(AnswerJudgment.Correct, AnswerMatcher.GradeReading("shinbun", "しんぶん", null));
    }

    [Fact]
    public void Reading_Direct_Kana_Matched()
    {
        Assert.Equal(AnswerJudgment.Correct, AnswerMatcher.GradeReading("しんぶん", "しんぶん", null));
    }

    [Fact]
    public void Reading_Wrong_Is_Incorrect()
    {
        Assert.Equal(AnswerJudgment.Incorrect, AnswerMatcher.GradeReading("ねこ", "しんぶん", null));
    }

    [Fact]
    public void Reading_Incomplete_Romaji_Asks_Retry()
    {
        // "shinbu" + leftover would still contain latin -> try again, not wrong.
        Assert.Equal(AnswerJudgment.CloseTryAgain, AnswerMatcher.GradeReading("x", "しんぶん", null));
    }

    [Fact]
    public void Conjugation_Romaji_Converted_And_Matched()
    {
        Assert.Equal(AnswerJudgment.Correct, AnswerMatcher.GradeConjugation("tabeta", "たべた"));
    }
}
