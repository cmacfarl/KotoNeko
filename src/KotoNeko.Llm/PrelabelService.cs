using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Data;
using KotoNeko.Data.Grammar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KotoNeko.Llm;

/// <summary>A model that votes when pre-labelling, at an optional effort or thinking level.</summary>
public sealed record Labeller(string Provider, string Model, string? Effort)
{
    public override string ToString() => $"{Provider} · {Model}{(Effort is null ? "" : $" ({Effort})")}";

    /// <summary>Parse "Provider:Model" or "Provider:Model:effort".</summary>
    public static Labeller Parse(string text)
    {
        string[] parts = text.Split(':', 3);
        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) {
            throw new FormatException($"A labeller is Provider:Model or Provider:Model:effort, not '{text}'.");
        }
        return new Labeller(parts[0], parts[1], parts.Length == 3 && parts[2].Length > 0 ? parts[2] : null);
    }

    /// <summary>The labellers in Llm:Labellers (a list of { Provider, Model, Effort }).</summary>
    public static List<Labeller> FromConfiguration(IConfiguration configuration) =>
        configuration.GetSection("Llm:Labellers").GetChildren()
            .Select(c => new Labeller(c["Provider"] ?? "", c["Model"] ?? "", string.IsNullOrWhiteSpace(c["Effort"]) ? null : c["Effort"]))
            .Where(l => l.Provider.Length > 0 && l.Model.Length > 0)
            .ToList();
}

/// <summary>One finished labelling call, reported as it happens.</summary>
/// <param name="Error">Why the call gave no vote; null when it did.</param>
public sealed record PrelabelCall(Labeller Labeller, GradingTestItem Item, int InputTokens, int OutputTokens, string? Error);

/// <summary>What a pre-labelling pass did.</summary>
public sealed record PrelabelSummary(int VotesAdded, int Failed, int Agreed, int NeedPerson, int Incomplete, long InputTokens, long OutputTokens);

/// <summary>
/// Pre-labels the grading test set: each labeller labels every item it hasn't voted
/// on yet, then each item's votes (plus how it was drafted) are combined. Where they
/// all agree the item gets an AI-agreed label; where they disagree it's marked for a
/// person. A person's label is never changed.
/// </summary>
public sealed class PrelabelService
{
    private readonly LlmService _llm;
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public PrelabelService(LlmService llm, IDbContextFactory<KotoNekoDbContext> factory)
    {
        _llm = llm;
        _factory = factory;
    }

    /// <summary>The votes still to collect: items not labelled by a person, times labellers that haven't voted.</summary>
    public async Task<List<(GradingTestItem Item, Labeller Labeller)>> PlanAsync(IReadOnlyList<Labeller> labellers, int? limit = null)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<GradingTestItem> items = await Open(db).OrderBy(i => i.SortOrder).ToListAsync();
        if (limit is int n) {
            items = items.Take(n).ToList();
        }
        HashSet<(int, string, string)> voted = (await db.GradingPrelabels.AsNoTracking()
                .Where(v => v.PromptVersion == Prelabelling.PromptVersion)
                .Select(v => new { v.ItemId, v.Provider, v.Model }).ToListAsync())
            .Select(v => (v.ItemId, v.Provider, v.Model)).ToHashSet();
        return items.SelectMany(i => labellers.Where(l => !voted.Contains((i.Id, l.Provider, l.Model))).Select(l => (i, l))).ToList();
    }

    /// <summary>
    /// Collect the planned votes (in parallel), then recombine every open item.
    /// <paramref name="onCall"/> hears about each call as it finishes, one at a time.
    /// </summary>
    public async Task<PrelabelSummary> RunAsync(
        IReadOnlyList<Labeller> labellers, IReadOnlyList<(GradingTestItem Item, Labeller Labeller)> plan,
        int parallel, Action<PrelabelCall>? onCall = null, CancellationToken cancellationToken = default)
    {
        await AddDraftedVotesAsync();

        int added = 0, failed = 0;
        long inputTokens = 0, outputTokens = 0;
        object gate = new();
        await Parallel.ForEachAsync(plan, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken },
            async (work, ct) =>
            {
                GradingTestItem item = work.Item;
                PrelabelCall call;
                try {
                    LlmResult<PrelabelAnswer> answer = await _llm.CallOnAsync<PrelabelAnswer>(
                        work.Labeller.Provider, work.Labeller.Model,
                        Prelabelling.Build(new GradingTask(item.Pattern, item.Meaning, item.Task, item.Sentence), work.Labeller.Effort),
                        Prelabelling.Validate, ct);
                    await using KotoNekoDbContext db = await _factory.CreateDbContextAsync(ct);
                    db.GradingPrelabels.Add(new GradingPrelabel {
                        ItemId = item.Id,
                        Provider = answer.Route.Provider,
                        Model = work.Labeller.Model,
                        Effort = work.Labeller.Effort,
                        PromptVersion = Prelabelling.PromptVersion,
                        Label = Prelabelling.Parse(answer.Value.Label),
                        Explanation = answer.Value.Explanation,
                        CreatedAt = DateTime.UtcNow,
                    });
                    await db.SaveChangesAsync(ct);
                    call = new PrelabelCall(work.Labeller, item, answer.InputTokens, answer.OutputTokens, null);
                } catch (LlmException ex) {
                    // A failed vote is just missing: the item stays incomplete until a rerun fills it.
                    call = new PrelabelCall(work.Labeller, item, ex.InputTokens, ex.OutputTokens, ex.Message);
                }
                lock (gate) {
                    if (call.Error is null) {
                        added++;
                    } else {
                        failed++;
                    }
                    inputTokens += call.InputTokens;
                    outputTokens += call.OutputTokens;
                    onCall?.Invoke(call);
                }
            });

        (int agreed, int needPerson, int incomplete) = await RecombineAsync(labellers);
        return new PrelabelSummary(added, failed, agreed, needPerson, incomplete, inputTokens, outputTokens);
    }

    /// <summary>
    /// Combine each open item's votes from <paramref name="labellers"/> (plus how it was
    /// drafted). Agreed items get an AI-agreed label; disagreements are marked for a
    /// person; incomplete ones are left unlabelled. Returns the counts.
    /// </summary>
    public async Task<(int Agreed, int NeedPerson, int Incomplete)> RecombineAsync(IReadOnlyList<Labeller> labellers)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<GradingTestItem> items = await Open(db).AsTracking().ToListAsync();
        List<int> ids = items.Select(i => i.Id).ToList();
        ILookup<int, GradingPrelabel> votes = (await db.GradingPrelabels.AsNoTracking()
                .Where(v => ids.Contains(v.ItemId)).ToListAsync())
            .ToLookup(v => v.ItemId);

        int agreed = 0, needPerson = 0, incomplete = 0;
        DateTime now = DateTime.UtcNow;
        foreach (GradingTestItem item in items) {
            GradingPrelabel? drafted = votes[item.Id].FirstOrDefault(v => v.PromptVersion == Prelabelling.DraftedVersion);
            List<GradingLabel> counted = labellers
                .Select(l => votes[item.Id].FirstOrDefault(v => v.PromptVersion == Prelabelling.PromptVersion
                    && v.Provider == l.Provider && v.Model == l.Model))
                .OfType<GradingPrelabel>()
                .Select(v => v.Label)
                .ToList();
            int required = labellers.Count;
            if (drafted is not null) {
                counted.Add(drafted.Label);
                required++;
            }

            PrelabelResult result = Prelabelling.Combine(counted, required, drafted?.Label);
            switch (result.State) {
                case PrelabelState.Agreed:
                    item.Label = result.Label;
                    item.LabelSource = LabelSource.AiAgreed;
                    item.NeedsPerson = false;
                    item.LabelledAt ??= now;
                    agreed++;
                    break;
                default:
                    // An earlier agreement no longer holds (a new labeller disagreed, say).
                    if (item.LabelSource == LabelSource.AiAgreed) {
                        item.Label = null;
                        item.LabelSource = LabelSource.Person;
                        item.LabelledAt = null;
                    }
                    item.NeedsPerson = result.State == PrelabelState.Disagree;
                    if (item.NeedsPerson) {
                        needPerson++;
                    } else {
                        incomplete++;
                    }
                    break;
            }
        }
        await db.SaveChangesAsync();
        return (agreed, needPerson, incomplete);
    }

    /// <summary>Record how each drafted candidate was meant, as a vote by the model that drafted it.</summary>
    private async Task AddDraftedVotesAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        IReadOnlyDictionary<string, GradingLabel> intent = GradingTestSet.DraftedIntent();
        HashSet<int> have = (await db.GradingPrelabels.Where(v => v.PromptVersion == Prelabelling.DraftedVersion)
            .Select(v => v.ItemId).ToListAsync()).ToHashSet();
        List<GradingTestItem> seeded = await db.GradingTestItems.AsNoTracking().Where(i => i.SeedKey != null).ToListAsync();
        foreach (GradingTestItem item in seeded.Where(i => !have.Contains(i.Id) && intent.ContainsKey(i.SeedKey!))) {
            db.GradingPrelabels.Add(new GradingPrelabel {
                ItemId = item.Id,
                Provider = GradingTestSet.DraftedByProvider,
                Model = GradingTestSet.DraftedByModel,
                PromptVersion = Prelabelling.DraftedVersion,
                Label = intent[item.SeedKey!],
                Explanation = "How the sentence was drafted.",
                CreatedAt = DateTime.UtcNow,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Items pre-labelling may decide: not excluded, and not labelled by a person.</summary>
    private static IQueryable<GradingTestItem> Open(KotoNekoDbContext db) => db.GradingTestItems.AsNoTracking()
        .Where(i => !i.Excluded && (i.Label == null || i.LabelSource == LabelSource.AiAgreed));
}
