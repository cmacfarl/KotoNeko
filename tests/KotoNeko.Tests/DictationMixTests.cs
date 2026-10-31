using KotoNeko.Core.Domain;
using KotoNeko.Core.Text;

namespace KotoNeko.Tests;

public class DictationMixTests
{
    [Theory]
    [InlineData(10, 3)]
    [InlineData(3, 1)]
    [InlineData(2, 0)]
    [InlineData(0, 0)]
    public void Up_To_A_Third_Of_A_Session_Is_Grammar(int count, int grammar)
    {
        Assert.Equal(grammar, DictationMix.GrammarShare(count));
    }

    [Fact]
    public void Your_Own_Sentences_Come_Before_Drafted_Ones()
    {
        GrammarExampleSource[] sorted = new[] { GrammarExampleSource.Generated, GrammarExampleSource.Yours, GrammarExampleSource.Composition }
            .OrderBy(DictationMix.SourceOrder).ToArray();
        Assert.Equal(new[] { GrammarExampleSource.Composition, GrammarExampleSource.Yours, GrammarExampleSource.Generated }, sorted);
    }

    [Fact]
    public void Grammar_Sentences_Are_Spread_Through_The_Session()
    {
        List<string> mixed = DictationMix.Spread(new[] { "w1", "w2", "w3", "w4", "w5", "w6", "w7" }, new[] { "g1", "g2", "g3" });

        Assert.Equal(10, mixed.Count);
        Assert.Equal(new[] { "w1", "w2", "w3", "w4", "w5", "w6", "w7" }, mixed.Where(x => x[0] == 'w'));
        Assert.Equal(new[] { "g1", "g2", "g3" }, mixed.Where(x => x[0] == 'g'));
        Assert.NotEqual("g1", mixed[0]);
        // No two grammar sentences side by side, and none crowded at the end.
        Assert.DoesNotContain(Enumerable.Range(1, mixed.Count - 1), i => mixed[i][0] == 'g' && mixed[i - 1][0] == 'g');
        Assert.Contains(mixed.Take(5), x => x[0] == 'g');
    }

    [Fact]
    public void Either_List_Can_Be_Empty()
    {
        Assert.Equal(new[] { "g1", "g2" }, DictationMix.Spread(Array.Empty<string>(), new[] { "g1", "g2" }));
        Assert.Equal(new[] { "w1" }, DictationMix.Spread(new[] { "w1" }, Array.Empty<string>()));
    }
}
