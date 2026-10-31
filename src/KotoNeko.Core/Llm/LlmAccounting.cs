namespace KotoNeko.Core.Llm;

/// <summary>One logged call as accounting sees it, at local time.</summary>
public sealed record CallCost(DateTime LocalTime, LlmFeature Feature, string Provider, string Model, int InputTokens, int OutputTokens, decimal? Cost, bool Ok);

/// <summary>Spending in one period: a day or a month.</summary>
/// <param name="UnpricedTokens">Tokens of calls on models with no price, so not in <see cref="Cost"/>.</param>
public sealed record CostPeriod(DateOnly Start, int Calls, long InputTokens, long OutputTokens, decimal Cost, long UnpricedTokens);

/// <summary>Spending on one thing: a model, a feature, or a group's total.</summary>
public sealed record SpendingLine(string Name, int Calls, int Failed, long InputTokens, long OutputTokens, decimal Cost, long UnpricedTokens)
{
    public bool HasUnpriced => UnpricedTokens > 0;
}

/// <summary>Spending in one group (a feature, or a model), split into lines (its models, or its features).</summary>
public sealed record SpendingGroup(string Name, IReadOnlyList<SpendingLine> Lines)
{
    public SpendingLine Total { get; } = new(Name, Lines.Sum(l => l.Calls), Lines.Sum(l => l.Failed),
        Lines.Sum(l => l.InputTokens), Lines.Sum(l => l.OutputTokens), Lines.Sum(l => l.Cost), Lines.Sum(l => l.UnpricedTokens));
}

/// <summary>Totals of LLM spending by day, by month and by model, from the call log.</summary>
public static class LlmAccounting
{
    /// <summary>The last <paramref name="days"/> days ending with <paramref name="today"/>, oldest first, every day present.</summary>
    public static List<CostPeriod> Daily(IEnumerable<CallCost> calls, DateOnly today, int days)
    {
        DateOnly first = today.AddDays(1 - days);
        ILookup<DateOnly, CallCost> byDay = calls.ToLookup(c => DateOnly.FromDateTime(c.LocalTime));
        return Enumerable.Range(0, days).Select(i => first.AddDays(i)).Select(d => Sum(d, byDay[d])).ToList();
    }

    /// <summary>The twelve months of <paramref name="year"/>, every month present.</summary>
    public static List<CostPeriod> Monthly(IEnumerable<CallCost> calls, int year)
    {
        ILookup<int, CallCost> byMonth = calls.Where(c => c.LocalTime.Year == year).ToLookup(c => c.LocalTime.Month);
        return Enumerable.Range(1, 12).Select(m => Sum(new DateOnly(year, m, 1), byMonth[m])).ToList();
    }

    /// <summary>Spending per feature, each split by model; most expensive first at both levels.</summary>
    public static List<SpendingGroup> ByFeature(IEnumerable<CallCost> calls) => Breakdown(calls, c => FeatureName(c.Feature), ModelName);

    /// <summary>Spending per model, each split by feature; most expensive first at both levels.</summary>
    public static List<SpendingGroup> ByModel(IEnumerable<CallCost> calls) => Breakdown(calls, ModelName, c => FeatureName(c.Feature));

    /// <summary>What a feature is called on the accounting page.</summary>
    public static string FeatureName(LlmFeature feature) => feature switch {
        LlmFeature.PracticeSentences => "Practice sentences",
        LlmFeature.GrammarDrafting => "Grammar lesson drafting",
        LlmFeature.LessonGrading => "Grammar lesson grading",
        LlmFeature.GradingEval => "Grading eval",
        LlmFeature.Prelabelling => "Test-set pre-labelling",
        LlmFeature.ReviewGrading => "Grammar review grading",
        LlmFeature.SecondOpinion => "Second opinion (unsure miss)",
        LlmFeature.Dispute => "Disputed verdict",
        LlmFeature.WhyQuestion => "\"Why?\" questions",
        LlmFeature.ListeningScript => "Listening scripts",
        LlmFeature.ListeningComprehension => "Listening comprehension",
        _ => "Other",
    };

    private static string ModelName(CallCost call) => $"{call.Provider} · {call.Model}";

    private static List<SpendingGroup> Breakdown(IEnumerable<CallCost> calls, Func<CallCost, string> group, Func<CallCost, string> line) =>
        calls.GroupBy(group)
            .Select(g => new SpendingGroup(g.Key, Lines(g, line)))
            .OrderByDescending(g => g.Total.Cost).ThenByDescending(g => g.Total.InputTokens + g.Total.OutputTokens)
            .ToList();

    private static List<SpendingLine> Lines(IEnumerable<CallCost> calls, Func<CallCost, string> line) =>
        calls.GroupBy(line)
            .Select(g => new SpendingLine(g.Key, g.Count(), g.Count(c => !c.Ok),
                g.Sum(c => (long)c.InputTokens), g.Sum(c => (long)c.OutputTokens),
                g.Sum(c => c.Cost ?? 0), g.Where(c => c.Cost is null).Sum(c => (long)c.InputTokens + c.OutputTokens)))
            .OrderByDescending(l => l.Cost).ThenByDescending(l => l.InputTokens + l.OutputTokens)
            .ToList();

    /// <summary>A round top for a chart's scale that is at least <paramref name="max"/>: 1, 2 or 5 times a power of ten.</summary>
    public static decimal NiceCeiling(decimal max)
    {
        if (max <= 0) {
            return 0.01m;
        }
        decimal step = 0.0001m;
        while (true) {
            foreach (decimal f in new[] { 1m, 2m, 5m }) {
                if (step * f >= max) {
                    return step * f;
                }
            }
            step *= 10;
        }
    }

    private static CostPeriod Sum(DateOnly start, IEnumerable<CallCost> calls)
    {
        List<CallCost> list = calls.ToList();
        return new CostPeriod(start, list.Count,
            list.Sum(c => (long)c.InputTokens), list.Sum(c => (long)c.OutputTokens),
            list.Sum(c => c.Cost ?? 0), list.Where(c => c.Cost is null).Sum(c => (long)c.InputTokens + c.OutputTokens));
    }
}
