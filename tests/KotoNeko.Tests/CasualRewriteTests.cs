using KotoNeko.Core.Llm;
using KotoNeko.Core.Text;

namespace KotoNeko.Tests;

public class CasualRewriteTests
{
    private static readonly List<CasualSource> Batch = new() {
        new(1, "宿題を忘れてしまいました。", "I forgot my homework."),
        new(2, "駅で待っています。", "I'm waiting at the station."),
        new(3, "行かなければなりません。", "I have to go."),
    };

    [Theory]
    [InlineData("毎朝コーヒーを飲みます。", true)]
    [InlineData("本を読んでいる。", true)]
    [InlineData("財布を忘れてしまった。", true)]
    [InlineData("早く寝なければ。", true)]
    [InlineData("それではまた。", true)]
    [InlineData("ちょっと待って。", false)]
    [InlineData("雨なのに出かけた。", false)]
    public void Sentences_Casual_Speech_Would_Change_Go_First(string text, bool changes)
    {
        Assert.Equal(changes, CasualRewrite.WouldChange(text));
    }

    [Fact]
    public void The_Request_Numbers_Each_Sentence_With_Its_English()
    {
        LlmRequest request = CasualRewrite.Build(Batch);

        Assert.Equal((LlmJob.Generate, LlmFeature.CasualRewrite, CasualRewrite.PromptVersion), (request.Job, request.Feature, request.PromptVersion));
        Assert.Contains("1. 宿題を忘れてしまいました。  (I forgot my homework.)", request.Prompt);
        Assert.Contains("3. 行かなければなりません。", request.Prompt);
        Assert.Contains("〜てしまう →", request.System);
        Assert.Contains("no dialect", request.System);
    }

    [Fact]
    public void Only_Real_Changes_To_Sentences_In_The_Batch_Are_Kept()
    {
        CasualRewriteAnswer answer = new(new List<CasualRewriteDraft> {
            new(1, "宿題を忘れちゃった。"),
            new(2, "駅で待っています"),      // the same, bar punctuation
            new(3, "行かなきゃ。"),
            new(3, "行かなくちゃ。"),        // a second answer for 3
            new(9, "知らない文。"),          // not in the batch
        });

        Dictionary<int, string> kept = CasualRewrite.Clean(answer, Batch);

        Assert.Equal(new Dictionary<int, string> { [1] = "宿題を忘れちゃった。", [3] = "行かなきゃ。" }, kept);
        Assert.NotNull(CasualRewrite.Validate(new CasualRewriteAnswer(null)));
    }

    [Theory]
    [InlineData(10, 2)]
    [InlineData(5, 1)]
    [InlineData(4, 0)]
    public void Up_To_A_Fifth_Of_A_Session_Is_Casual(int count, int casual)
    {
        Assert.Equal(casual, DictationMix.CasualShare(count));
    }
}
