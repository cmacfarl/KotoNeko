using KotoNeko.Core.Domain;
using KotoNeko.Data.Grammar;
using Xunit;

namespace KotoNeko.Tests;

public class GrammarListTests
{
    [Fact]
    public void The_Drafted_List_Is_Well_Formed()
    {
        IReadOnlyList<DraftedGrammarPoint> drafted = GrammarList.Drafted();

        Assert.Equal(drafted.Count, drafted.Select(p => p.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(drafted.Count, drafted.Select(p => (p.Level, p.Pattern)).Distinct().Count());
        // Keys are ids: a point moved to another level keeps the key it was first drafted with.
        Assert.All(drafted, p => {
            Assert.Contains(p.Level, new[] { JlptLevel.N5, JlptLevel.N4, JlptLevel.N3 });
            Assert.Matches("^n[1-5]-[a-z0-9-]+$", p.Key);
            Assert.InRange(p.Key.Length, 4, GrammarListEntry.MaxKeyLength);
            Assert.InRange(p.Pattern.Trim().Length, 1, GrammarListEntry.MaxPatternLength);
            Assert.InRange(p.Meaning.Trim().Length, 1, GrammarListEntry.MaxMeaningLength);
        });
        Assert.True(drafted.Count(p => p.Level == JlptLevel.N5) > 50);
        Assert.True(drafted.Count(p => p.Level == JlptLevel.N4) > 50);
        Assert.True(drafted.Count(p => p.Level == JlptLevel.N3) > 50);
        // In study order: easiest level first.
        Assert.Equal(drafted.OrderByDescending(p => p.Level).Select(p => p.Key), drafted.Select(p => p.Key));
    }

    private static readonly DraftedGrammarPoint[] Sample = {
        new("n5-desu", JlptLevel.N5, "〜です", "to be (polite)"),
        new("n4-noni", JlptLevel.N4, "〜のに", "even though"),
        new("n4-node", JlptLevel.N4, "〜ので", "because"),
    };

    [Fact]
    public void Seeding_Starts_N5_Known_And_Harder_Levels_Not_Yet_In_Study_Order()
    {
        List<GrammarListEntry> added = GrammarList.Plan(Sample, Array.Empty<string?>(), DateTime.UtcNow);

        Assert.Equal(new[] { "n5-desu", "n4-noni", "n4-node" }, added.Select(e => e.SeedKey));
        Assert.Equal(GrammarStatus.Known, added[0].Status);
        Assert.All(added.Skip(1), e => Assert.Equal(GrammarStatus.NotYet, e.Status));
        Assert.True(added[1].SortOrder < added[2].SortOrder);
        Assert.All(added, e => Assert.False(e.IsPruned));
    }

    [Fact]
    public void Seeding_Again_Adds_Only_Missing_Keys_So_Pruning_Sticks()
    {
        // n4-noni is on the list already (pruned or edited, it doesn't matter); a user's own point has no key.
        List<GrammarListEntry> added = GrammarList.Plan(Sample, new string?[] { "n4-noni", null }, DateTime.UtcNow);

        Assert.Equal(new[] { "n5-desu", "n4-node" }, added.Select(e => e.SeedKey));
    }
}
