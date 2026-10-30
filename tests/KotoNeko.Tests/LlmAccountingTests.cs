using KotoNeko.Core.Llm;

namespace KotoNeko.Tests;

public class LlmAccountingTests
{
    private static CallCost Call(string when, LlmFeature feature, string model, int input, int output, decimal? cost, bool ok = true) =>
        new(DateTime.Parse(when), feature, model.StartsWith("gemini") ? "Gemini" : "Claude", model, input, output, cost, ok);

    private static readonly List<CallCost> Calls = new() {
        Call("2026-10-30 09:00", LlmFeature.LessonGrading, "gemini-3.8-flash", 400, 100, 0.0006m),
        Call("2026-10-30 21:00", LlmFeature.GrammarDrafting, "claude-opus-5-5", 2000, 3000, 0.068m),
        Call("2026-10-28 12:00", LlmFeature.GradingEval, "gemini-3.8-flash", 400, 0, 0.0003m, ok: false),
        Call("2026-10-01 08:00", LlmFeature.GradingEval, "local-model", 500, 500, null),
        Call("2026-09-15 10:00", LlmFeature.GradingEval, "claude-opus-5-5", 1000, 1000, 0.024m),
        Call("2025-12-31 23:00", LlmFeature.PracticeSentences, "claude-opus-5-5", 1000, 1000, 0.024m),
    };

    [Fact]
    public void Daily_Has_Every_Day_Oldest_First_Ending_Today()
    {
        List<CostPeriod> days = LlmAccounting.Daily(Calls, new DateOnly(2026, 10, 30), 30);

        Assert.Equal(30, days.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), days[0].Start);
        Assert.Equal(new DateOnly(2026, 10, 30), days[^1].Start);
        Assert.Equal(0.0686m, days[^1].Cost);
        Assert.Equal(2, days[^1].Calls);
        Assert.Equal(0, days[^2].Calls);
        Assert.Equal(1000, days[0].UnpricedTokens);
        Assert.Equal(0m, days[0].Cost);
    }

    [Fact]
    public void Monthly_Has_All_Twelve_Months_Of_The_Year_Only()
    {
        List<CostPeriod> months = LlmAccounting.Monthly(Calls, 2026);

        Assert.Equal(12, months.Count);
        Assert.Equal(new DateOnly(2026, 1, 1), months[0].Start);
        Assert.Equal(0m, months[0].Cost);
        Assert.Equal(0.024m, months[8].Cost);
        Assert.Equal(0.0689m, months[9].Cost);
        Assert.Equal(4, months[9].Calls);
    }

    [Fact]
    public void By_Feature_Splits_Each_Feature_By_Model_Most_Expensive_First()
    {
        List<SpendingGroup> features = LlmAccounting.ByFeature(Calls.Where(c => c.LocalTime.Year == 2026));

        Assert.Equal(new[] { "Grammar lesson drafting", "Grading eval", "Grammar lesson grading" }, features.Select(f => f.Name));
        SpendingGroup eval = features[1];
        Assert.Equal(new[] { "Claude · claude-opus-5-5", "Gemini · gemini-3.8-flash", "Claude · local-model" }, eval.Lines.Select(l => l.Name));
        Assert.Equal((3, 1, 1900L, 1500L, 0.0243m), (eval.Total.Calls, eval.Total.Failed, eval.Total.InputTokens, eval.Total.OutputTokens, eval.Total.Cost));
        Assert.True(eval.Total.HasUnpriced);
        Assert.True(eval.Lines[2].HasUnpriced);
        Assert.False(features[0].Total.HasUnpriced);
    }

    [Fact]
    public void By_Model_Splits_Each_Model_By_Feature()
    {
        List<SpendingGroup> models = LlmAccounting.ByModel(Calls.Where(c => c.LocalTime.Year == 2026));

        Assert.Equal(new[] { "Claude · claude-opus-5-5", "Gemini · gemini-3.8-flash", "Claude · local-model" }, models.Select(m => m.Name));
        SpendingGroup flash = models[1];
        Assert.Equal((2, 1, 800L, 100L, 0.0009m), (flash.Total.Calls, flash.Total.Failed, flash.Total.InputTokens, flash.Total.OutputTokens, flash.Total.Cost));
        Assert.Equal(new[] { "Grammar lesson grading", "Grading eval" }, flash.Lines.Select(l => l.Name));
    }

    [Fact]
    public void Every_Feature_Has_A_Name()
    {
        Assert.All(Enum.GetValues<LlmFeature>().Where(f => f != LlmFeature.Unknown),
            f => Assert.NotEqual("Other", LlmAccounting.FeatureName(f)));
    }

    [Theory]
    [InlineData("0", "0.01")]
    [InlineData("0.0007", "0.001")]
    [InlineData("0.0686", "0.1")]
    [InlineData("0.13", "0.2")]
    [InlineData("3.2", "5")]
    [InlineData("10", "10")]
    public void The_Chart_Scale_Tops_Out_At_A_Round_Number(string max, string top)
    {
        Assert.Equal(decimal.Parse(top, System.Globalization.CultureInfo.InvariantCulture),
            LlmAccounting.NiceCeiling(decimal.Parse(max, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
