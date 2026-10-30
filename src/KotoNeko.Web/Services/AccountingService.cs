using KotoNeko.Core.Llm;
using KotoNeko.Data;
using KotoNeko.Llm;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>What the accounting page shows for one year, plus the last 30 days.</summary>
public sealed record AccountingReport(
    int Year,
    IReadOnlyList<int> Years,
    IReadOnlyList<CostPeriod> Last30Days,
    IReadOnlyList<CostPeriod> Months,
    IReadOnlyList<SpendingGroup> Last30ByFeature,
    IReadOnlyList<SpendingGroup> Last30ByModel,
    IReadOnlyList<SpendingGroup> YearByFeature,
    IReadOnlyList<SpendingGroup> YearByModel,
    decimal ThisMonth,
    decimal AllTime,
    IReadOnlyList<string> UnpricedModels)
{
    public decimal Last30Total => Last30Days.Sum(d => d.Cost);
    public decimal YearTotal => Months.Sum(m => m.Cost);
}

/// <summary>
/// LLM spending from the call log: every call stores its cost (Llm:Prices at the
/// time); calls logged before that, or on a model with no price yet, are filled in
/// from today's prices when one is known.
/// </summary>
public sealed class AccountingService
{
    public const int RecentDays = 30;

    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly LlmPricing _pricing;

    public AccountingService(IDbContextFactory<KotoNekoDbContext> factory, LlmPricing pricing)
    {
        _factory = factory;
        _pricing = pricing;
    }

    public async Task<AccountingReport> ReportAsync(int? year, DateTime utcNow)
    {
        await FillMissingCostsAsync();

        DateTime localNow = utcNow.ToLocalTime();
        int shownYear = year ?? localNow.Year;
        DateOnly today = DateOnly.FromDateTime(localNow);

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<int> years = (await db.LlmCalls.AsNoTracking().Select(c => c.CalledAt.Year).Distinct().ToListAsync())
            .Append(localNow.Year).Append(shownYear).Distinct().OrderDescending().ToList();

        // A day's margin either side of each range covers the difference between UTC and local time.
        DateTime recentFrom = utcNow.AddDays(-RecentDays - 1);
        DateTime yearFrom = new DateTime(shownYear, 1, 1).AddDays(-1);
        DateTime yearTo = new DateTime(shownYear + 1, 1, 1).AddDays(1);
        List<CallCost> calls = (await db.LlmCalls.AsNoTracking()
                .Where(c => c.CalledAt >= recentFrom || (c.CalledAt >= yearFrom && c.CalledAt < yearTo))
                .Select(c => new { c.CalledAt, c.Feature, c.Provider, c.Model, c.InputTokens, c.OutputTokens, c.Cost, c.Outcome })
                .ToListAsync())
            .Select(c => new CallCost(DateTime.SpecifyKind(c.CalledAt, DateTimeKind.Utc).ToLocalTime(), c.Feature,
                c.Provider, c.Model, c.InputTokens, c.OutputTokens, c.Cost, c.Outcome == LlmOutcome.Ok))
            .ToList();
        decimal allTime = await db.LlmCalls.SumAsync(c => c.Cost ?? 0);

        DateOnly firstRecent = today.AddDays(1 - RecentDays);
        List<CallCost> recent = calls.Where(c => DateOnly.FromDateTime(c.LocalTime) >= firstRecent && DateOnly.FromDateTime(c.LocalTime) <= today).ToList();
        List<CallCost> inYear = calls.Where(c => c.LocalTime.Year == shownYear).ToList();
        List<CostPeriod> months = LlmAccounting.Monthly(inYear, shownYear);
        decimal thisMonth = shownYear == localNow.Year
            ? months[localNow.Month - 1].Cost
            : LlmAccounting.Monthly(calls, localNow.Year)[localNow.Month - 1].Cost;

        return new AccountingReport(
            shownYear, years,
            LlmAccounting.Daily(recent, today, RecentDays),
            months,
            LlmAccounting.ByFeature(recent),
            LlmAccounting.ByModel(recent),
            LlmAccounting.ByFeature(inYear),
            LlmAccounting.ByModel(inYear),
            thisMonth,
            allTime,
            recent.Concat(inYear).Where(c => c.Cost is null && c.InputTokens + c.OutputTokens > 0)
                .Select(c => c.Model).Distinct().Order().ToList());
    }

    /// <summary>Price the logged calls that have no cost yet, where the model now has a price. Returns how many.</summary>
    public async Task<int> FillMissingCostsAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<string> models = await db.LlmCalls.Where(c => c.Cost == null && (c.InputTokens > 0 || c.OutputTokens > 0))
            .Select(c => c.Model).Distinct().ToListAsync();
        List<string> priced = models.Where(_pricing.Knows).ToList();
        if (priced.Count == 0) {
            return 0;
        }
        List<LlmCall> unpriced = await db.LlmCalls
            .Where(c => c.Cost == null && (c.InputTokens > 0 || c.OutputTokens > 0) && priced.Contains(c.Model))
            .ToListAsync();
        foreach (LlmCall call in unpriced) {
            call.Cost = _pricing.Cost(call.Model, call.InputTokens, call.OutputTokens);
        }
        await db.SaveChangesAsync();
        return unpriced.Count;
    }
}
