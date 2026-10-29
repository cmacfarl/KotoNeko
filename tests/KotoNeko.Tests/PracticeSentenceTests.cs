using KotoNeko.Core.Text;
using Xunit;

namespace KotoNeko.Tests;

// Made-up sentences only: real context sentences must never appear in the repository.
public class SentenceCheckTests
{
    private static readonly Func<string, bool> Known = SentenceCheck.Knows(new[] {
        ("友達", "ともだち"), ("駅", "えき"), ("分かる", "わかる"), ("行く", "いく"),
    });

    private static FuriganaPiece P(string text, string? reading = null) => new(text, reading);

    // 友達に会う。
    private static readonly FuriganaPiece[] MeetPieces = { P("友達", "ともだち"), P("に"), P("会", "あ"), P("う。") };
    private static readonly CheckWord[] MeetWords = {
        new(0, 2, "友達", false), new(2, 1, "に", true), new(3, 2, "会う", false), new(5, 1, null, false),
    };

    [Fact]
    public void A_Sentence_Of_Known_Words_Is_Usable_For_Listening()
    {
        SentenceCheckResult result = SentenceCheck.Check("友達に会う。", MeetPieces, MeetWords, "会う", Known);

        Assert.True(result.HasTarget);
        Assert.Equal(3, result.TargetStart);
        Assert.Empty(result.Unknown);
        Assert.Equal(7, result.KanaLength);   // ともだちにあう: punctuation isn't spoken
        Assert.True(result.UsableForListening);
    }

    [Fact]
    public void Other_Unknown_Words_Are_Listed_With_Their_Dictionary_Form()
    {
        // 先生に会った。
        FuriganaPiece[] pieces = { P("先生", "せんせい"), P("に"), P("会", "あ"), P("った。") };
        CheckWord[] words = { new(0, 2, "先生", false), new(2, 1, "に", true), new(3, 3, "会う", false), new(6, 1, null, false) };

        SentenceCheckResult result = SentenceCheck.Check("先生に会った。", pieces, words, "会う", Known);

        Assert.Equal(3, result.TargetStart);   // found by its dictionary form, inflected
        UnknownWord unknown = Assert.Single(result.Unknown);
        Assert.Equal("先生", unknown.Text);
        Assert.Equal("先生", unknown.Lemma);
        Assert.False(result.UsableForListening);
    }

    [Fact]
    public void A_Kana_Spelling_Of_A_Known_Word_Counts_As_Known()
    {
        // わかります: written in kana, known as 分かる.
        FuriganaPiece[] pieces = { P("駅", "えき"), P("がわかります") };
        CheckWord[] words = { new(0, 1, "駅", false), new(1, 1, "が", true), new(2, 5, "わかる", false) };

        SentenceCheckResult result = SentenceCheck.Check("駅がわかります", pieces, words, "駅", Known);

        Assert.Empty(result.Unknown);
    }

    [Fact]
    public void Basic_Function_Words_And_Punctuation_Are_Always_Fine()
    {
        // 駅です。
        FuriganaPiece[] pieces = { P("駅", "えき"), P("です。") };
        CheckWord[] words = { new(0, 1, "駅", false), new(1, 2, "です", false), new(3, 1, null, false) };

        Assert.Empty(SentenceCheck.Check("駅です。", pieces, words, "駅", _ => false).Unknown);
    }

    [Fact]
    public void An_Unrecognised_Run_Of_Characters_Is_One_Unknown_Word()
    {
        // ピザに行く: ピ and ザ unrecognised.
        FuriganaPiece[] pieces = { P("ピザに"), P("行", "い"), P("く") };
        CheckWord[] words = { new(0, 1, null, false), new(1, 1, null, false), new(2, 1, "に", true), new(3, 2, "行く", false) };

        UnknownWord unknown = Assert.Single(SentenceCheck.Check("ピザに行く", pieces, words, "行く", Known).Unknown);
        Assert.Equal("ピザ", unknown.Text);
        Assert.Null(unknown.Lemma);
    }

    [Fact]
    public void A_Sentence_Without_The_Word_Is_Not_Usable()
    {
        SentenceCheckResult result = SentenceCheck.Check("友達に会う。", MeetPieces, MeetWords, "行く", Known);

        Assert.False(result.HasTarget);
        Assert.False(result.UsableForListening);
    }

    [Fact]
    public void A_Long_Sentence_Is_Comprehensible_But_Too_Long_For_Listening()
    {
        // 友達に会う repeated: 7 kana each, 21 in all.
        string text = "友達に会う友達に会う友達に会う";
        List<FuriganaPiece> pieces = new();
        List<CheckWord> words = new();
        for (int i = 0; i < 3; i++) {
            pieces.AddRange(new[] { P("友達", "ともだち"), P("に"), P("会", "あ"), P("う") });
            words.AddRange(new[] { new CheckWord(i * 5, 2, "友達", false), new CheckWord(i * 5 + 2, 1, "に", true), new CheckWord(i * 5 + 3, 2, "会う", false) });
        }

        SentenceCheckResult result = SentenceCheck.Check(text, pieces, words, "会う", Known);

        Assert.True(result.Comprehensible);
        Assert.Equal(21, result.KanaLength);
        Assert.False(result.UsableForListening);
    }
}

public class DictationLadderTests
{
    private static IEnumerable<double> Repeat(double score, int times) => Enumerable.Repeat(score, times);

    [Fact]
    public void Starts_At_Fifteen_Kana() => Assert.Equal(15, DictationLadder.Level(Array.Empty<double>()));

    [Fact]
    public void A_Run_Of_Good_Scores_Moves_Up_A_Rung()
    {
        Assert.Equal(20, DictationLadder.Level(Repeat(0.95, 5)));
        Assert.Equal(25, DictationLadder.Level(Repeat(1.0, 10)));
    }

    [Fact]
    public void A_Run_Broken_By_An_Ordinary_Score_Starts_Again()
    {
        Assert.Equal(15, DictationLadder.Level(Repeat(0.95, 4).Append(0.8).Concat(Repeat(0.95, 4))));
    }

    [Fact]
    public void Repeated_Misses_Move_Down_But_Never_Below_The_First_Rung()
    {
        Assert.Equal(15, DictationLadder.Level(Repeat(0.95, 5).Concat(Repeat(0.3, 3))));
        Assert.Equal(15, DictationLadder.Level(Repeat(0.1, 9)));
    }

    [Fact]
    public void The_Top_Rung_Is_The_Limit()
    {
        Assert.Equal(DictationLadder.Rungs[^1], DictationLadder.Level(Repeat(1.0, 100)));
    }
}
