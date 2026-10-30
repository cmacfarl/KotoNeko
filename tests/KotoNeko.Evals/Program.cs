// The grading eval, and pre-labelling the grading test set. Both call LLMs (usually
// paid), so they only run when asked, and ask before spending anything.
//
// Pre-label (each labeller votes on every sentence a person hasn't labelled; where all
// votes agree the sentence gets an AI-agreed label, otherwise it's marked for a person):
//   dotnet run --project tests/KotoNeko.Evals -- --prelabel
//   dotnet run --project tests/KotoNeko.Evals -- --prelabel --labeller Gemini:gemini-3.8-flash:low --labeller Claude:claude-opus-5-5:high
//
// Grade (score a grader against the labels):
//   dotnet run --project tests/KotoNeko.Evals
//   dotnet run --project tests/KotoNeko.Evals -- --provider Gemini --model gemini-3.8-flash --effort low
//   dotnet run --project tests/KotoNeko.Evals -- --limit 5 --point n4-noni
//   dotnet run --project tests/KotoNeko.Evals -- --history
//
// Options: --provider, --model (override Llm:Jobs:Grade), --effort (grader effort or
// thinking level), --labeller Provider:Model[:effort] (repeatable; replaces
// Llm:Labellers entirely), --limit N, --point KEY, --parallel N (default 4), --yes.
// Configuration is the web app's: src/KotoNeko.Web/appsettings.json,
// appsettings.Development.json and the environment.
//
// Every call prints its tokens and cost (from Llm:Prices) and the running totals, so a
// run going wrong can be stopped with Ctrl+C. Pre-labelling keeps the votes already
// taken (the next run combines them); a stopped grading run isn't saved.

using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Data;
using KotoNeko.Data.Backup;
using KotoNeko.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Sentences and patterns are Japanese; the Windows console's default code page prints them as ???.
Console.OutputEncoding = System.Text.Encoding.UTF8;

Dictionary<string, List<string?>> options = ParseOptions(args);

string? backups = BackupFiles.FindRepositoryFolder(AppContext.BaseDirectory)
    ?? BackupFiles.FindRepositoryFolder(Directory.GetCurrentDirectory());
if (backups is null) {
    return Fail("Can't find the repository (KotoNeko.sln): run this from inside it.");
}
string webFolder = Path.Combine(Path.GetDirectoryName(backups)!, "src", "KotoNeko.Web");

Dictionary<string, string?> overrides = new();
if (Option("provider") is { } providerOption) {
    overrides["Llm:Jobs:Grade:Provider"] = providerOption;
}
if (Option("model") is { } modelOption) {
    overrides["Llm:Jobs:Grade:Model"] = modelOption;
}
IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile(Path.Combine(webFolder, "appsettings.json"), optional: false)
    .AddJsonFile(Path.Combine(webFolder, "appsettings.Development.json"), optional: true)
    .AddEnvironmentVariables()
    .AddInMemoryCollection(overrides)
    .Build();

string? connection = configuration["KOTONEKO_CONNECTION"] is { Length: > 0 } c ? c : configuration.GetConnectionString("KotoNeko");
if (string.IsNullOrWhiteSpace(connection)) {
    return Fail("No connection string: set KOTONEKO_CONNECTION or ConnectionStrings:KotoNeko.");
}

ServiceCollection services = new();
services.AddLogging();
services.AddKotoNekoData(connection);
services.AddKotoNekoLlm(configuration);
await using ServiceProvider serviceProvider = services.BuildServiceProvider();
IDbContextFactory<KotoNekoDbContext> factory = serviceProvider.GetRequiredService<IDbContextFactory<KotoNekoDbContext>>();

await using (KotoNekoDbContext db = await factory.CreateDbContextAsync()) {
    // Never migrate from here: the app backs up first, then migrates.
    if ((await db.Database.GetPendingMigrationsAsync()).Any()) {
        return Fail("The database needs migrating: start KotoNeko once (it backs up, then migrates), then run this again.");
    }
}

if (options.ContainsKey("history")) {
    await PrintHistoryAsync(factory);
    return 0;
}

int? limit = Option("limit") is { } limitText && int.TryParse(limitText, out int l) ? l : null;
int parallel = Option("parallel") is { } p && int.TryParse(p, out int n) ? Math.Clamp(n, 1, 16) : 4;
LlmService llm = serviceProvider.GetRequiredService<LlmService>();
LlmPricing pricing = serviceProvider.GetRequiredService<LlmPricing>();
CostMeter meter = new(pricing);

if (options.ContainsKey("prelabel")) {
    return await PrelabelAsync();
}
return await GradeAsync();

async Task<int> PrelabelAsync()
{
    // --labeller replaces the configured list entirely, so a test run can't pick up
    // a paid labeller left over from configuration.
    List<Labeller> labellers;
    try {
        labellers = options.TryGetValue("labeller", out List<string?>? given)
            ? given.OfType<string>().Select(Labeller.Parse).ToList()
            : Labeller.FromConfiguration(configuration);
    } catch (FormatException ex) {
        return Fail(ex.Message);
    }
    if (labellers.Count == 0) {
        return Fail("No labellers: set Llm:Labellers in appsettings.json or pass --labeller Provider:Model[:effort].");
    }
    if (labellers.FirstOrDefault(x => !llm.IsConfigured(x.Provider)) is { } missing) {
        // Never run with fewer labellers than asked for: it would weaken agreement unnoticed.
        return Fail($"{missing.Provider} isn't configured (no key or address), so {missing} can't vote. Nothing was sent.");
    }

    PrelabelService prelabel = serviceProvider.GetRequiredService<PrelabelService>();
    List<(GradingTestItem Item, Labeller Labeller)> plan = await prelabel.PlanAsync(labellers, limit);
    Console.WriteLine($"Labellers: {string.Join(", ", labellers)}");
    foreach (Labeller labeller in labellers) {
        Console.WriteLine($"  {labeller.Model}: {meter.PriceText(labeller.Model)}");
    }
    Console.WriteLine($"Votes to collect: {plan.Count} (sentences a person hasn't labelled, times labellers that haven't voted on them).");
    if (plan.Count > 0) {
        Console.WriteLine($"That's {plan.Count} LLM call{(plan.Count == 1 ? "" : "s")}, about 600 tokens in and a couple of hundred out each.");
        if (!Confirm()) {
            return 0;
        }
    }

    int done = 0;
    PrelabelSummary summary = await prelabel.RunAsync(labellers, plan, parallel, call =>
    {
        done++;
        string who = $"{call.Labeller.Provider} · {call.Labeller.Model}{(call.Labeller.Effort is null ? "" : $" ({call.Labeller.Effort})")}";
        Console.WriteLine(meter.Line(done, plan.Count, who, call.Item.SeedKey ?? $"#{call.Item.Id}",
            call.Labeller.Model, call.InputTokens, call.OutputTokens, call.Error));
    });
    Console.WriteLine();
    Console.WriteLine($"Votes added: {summary.VotesAdded}{(summary.Failed > 0 ? $"; {summary.Failed} failed (run again to fill them in)" : "")}.");
    Console.WriteLine($"AI-agreed: {summary.Agreed}. Need a person: {summary.NeedPerson}. Still missing votes: {summary.Incomplete}.");
    Console.WriteLine($"Total: {meter.TotalText()}");
    Console.WriteLine("The sentences that need a person are under \"Needs a person\" on the Grading test set page.");
    return 0;
}

async Task<int> GradeAsync()
{
    GradingService grading = serviceProvider.GetRequiredService<GradingService>();
    LlmRoute? route = grading.Route;
    if (route is null) {
        return Fail("No Grade route: set Llm:Jobs:Grade:Provider and :Model, or pass --provider and --model.");
    }
    if (!route.IsConfigured) {
        return Fail($"{route.Provider} isn't configured (no key or address). See Settings → AI providers.");
    }
    string? effort = Option("effort");

    List<GradingTestItem> items;
    HashSet<int> graderVoted;
    await using (KotoNekoDbContext db = await factory.CreateDbContextAsync()) {
        IQueryable<GradingTestItem> query = db.GradingTestItems.AsNoTracking().Where(i => i.Label != null && !i.Excluded);
        if (Option("point") is { } point) {
            query = query.Where(i => i.GrammarKey == point);
        }
        items = await query.OrderBy(i => i.GrammarKey).ThenBy(i => i.SortOrder).ToListAsync();
        // Items whose label this grader's model helped decide (any prompt or effort).
        graderVoted = (await db.GradingPrelabels.AsNoTracking()
                .Where(v => v.Provider == route.Provider && v.Model == route.Model)
                .Select(v => v.ItemId).ToListAsync())
            .ToHashSet();
    }
    if (limit is int max) {
        items = items.Take(max).ToList();
    }
    if (items.Count == 0) {
        return Fail("No labelled sentences to grade: label (or pre-label) some on the Grading test set page first.");
    }

    int person = items.Count(i => i.LabelSource == LabelSource.Person);
    int selfLabelled = items.Count(i => i.LabelSource == LabelSource.AiAgreed && graderVoted.Contains(i.Id));
    Console.WriteLine($"Grading {items.Count} labelled sentence{(items.Count == 1 ? "" : "s")} with {route.Provider} · {route.Model}" +
        $"{(effort is null ? "" : $" ({effort})")}, prompt {Grading.PromptVersion}.");
    Console.WriteLine($"  {person} labelled by a person (the headline score), {items.Count - person} AI-agreed" +
        $"{(selfLabelled > 0 ? $", of which {selfLabelled} this model helped label and won't be scored on" : "")}.");
    Console.WriteLine($"That's {items.Count} LLM call{(items.Count == 1 ? "" : "s")}, about 1,000 tokens in and a few hundred out each.");
    Console.WriteLine($"  {route.Model}: {meter.PriceText(route.Model)}");
    if (!Confirm()) {
        return 0;
    }

    GradingEvalRun run = new() {
        Provider = route.Provider,
        Model = route.Model,
        Effort = effort,
        PromptVersion = Grading.PromptVersion,
        StartedAt = DateTime.UtcNow,
        Items = items.Count,
    };

    List<(GradingTestItem Item, GradingEvalResult Result, GradingVerdict? Verdict)> outcomes = new();
    object gate = new();
    int done = 0;
    await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = parallel }, async (item, cancellationToken) =>
    {
        GradingEvalResult result = new() {
            ItemId = item.Id,
            Label = item.Label!.Value,
            LabelSource = item.LabelSource,
            GraderVoted = item.LabelSource == LabelSource.AiAgreed && graderVoted.Contains(item.Id),
        };
        GradingVerdict? verdict = null;
        (int Input, int Output) spent = (0, 0);
        try {
            LlmResult<GradingVerdict> graded = await grading.GradeAsync(
                new GradingTask(item.Pattern, item.Meaning, item.Task, item.Sentence), effort, cancellationToken);
            verdict = graded.Value;
            result.TargetUsedCorrectly = verdict.TargetUsedCorrectly;
            result.Natural = verdict.Natural;
            result.Confidence = verdict.Confidence;
            result.VerdictJson = graded.Json;
            spent = (graded.InputTokens, graded.OutputTokens);
            lock (gate) {
                run.InputTokens += graded.InputTokens;
                run.OutputTokens += graded.OutputTokens;
            }
        } catch (LlmException ex) {
            result.Error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            lock (gate) {
                run.InputTokens += ex.InputTokens;
                run.OutputTokens += ex.OutputTokens;
            }
            spent = (ex.InputTokens, ex.OutputTokens);
        }
        lock (gate) {
            outcomes.Add((item, result, verdict));
            done++;
            Console.WriteLine(meter.Line(done, items.Count, $"{route.Provider} · {route.Model}", item.SeedKey ?? $"#{item.Id}",
                route.Model, spent.Input, spent.Output, result.Error));
        }
    });
    Console.WriteLine();

    // The headline is person-labelled items only. AI-agreed items the grader didn't
    // vote on are reported apart, as consistency on easier items; ones it did vote on
    // aren't scored at all.
    GradingScores headline = Score(outcomes.Where(o => o.Result.LabelSource == LabelSource.Person));
    GradingScores aiAgreed = Score(outcomes.Where(o => o.Result.LabelSource == LabelSource.AiAgreed && !o.Result.GraderVoted));
    run.FinishedAt = DateTime.UtcNow;
    run.Graded = headline.Items - headline.Failed;
    run.Agreement = headline.Agreement.Value;
    run.FalseCorrectionRate = headline.FalseCorrection.Value;
    run.FalsePassRate = headline.FalsePass.Value;
    run.AiAgreedGraded = aiAgreed.Items - aiAgreed.Failed;
    run.AiAgreedAgreement = aiAgreed.Agreement.Value;
    run.Results = outcomes.Select(o => o.Result).ToList();

    GradingEvalRun? previous;
    await using (KotoNekoDbContext db = await factory.CreateDbContextAsync()) {
        previous = await db.GradingEvalRuns.AsNoTracking()
            .Where(r => r.PromptVersion == run.PromptVersion)
            .OrderByDescending(r => r.StartedAt).FirstOrDefaultAsync();
        db.GradingEvalRuns.Add(run);
        await db.SaveChangesAsync();
    }

    PrintReport(run, headline, aiAgreed, selfLabelled, outcomes, previous);
    Console.WriteLine($"Total: {meter.TotalText()}");
    return 0;
}

string? Option(string name) => options.TryGetValue(name, out List<string?>? values) ? values.LastOrDefault() : null;

bool Confirm()
{
    if (options.ContainsKey("yes")) {
        return true;
    }
    Console.Write("Go ahead? [y/N] ");
    if (Console.ReadLine()?.Trim().ToLowerInvariant() is "y" or "yes") {
        return true;
    }
    Console.WriteLine("Cancelled: nothing was sent.");
    return false;
}

static GradingScores Score(IEnumerable<(GradingTestItem Item, GradingEvalResult Result, GradingVerdict? Verdict)> outcomes) =>
    GradingEval.Score(outcomes.Select(o => new GradedItem(o.Item.GrammarKey, o.Result.Label, o.Verdict)).ToList());

static void PrintReport(
    GradingEvalRun run, GradingScores headline, GradingScores aiAgreed, int selfLabelled,
    List<(GradingTestItem Item, GradingEvalResult Result, GradingVerdict? Verdict)> outcomes, GradingEvalRun? previous)
{
    Console.WriteLine();
    Console.WriteLine($"Run {run.Id}: {run.Provider} · {run.Model}{(run.Effort is null ? "" : $" ({run.Effort})")} · {run.PromptVersion}");
    Console.WriteLine();
    Console.WriteLine($"Person-labelled sentences (the headline): {headline.Items}, of which {headline.Failed} got no verdict");
    Console.WriteLine($"  Agreement:        {headline.Agreement}   (target used correctly or not, vs. the label)");
    Console.WriteLine($"  False correction: {headline.FalseCorrection}   (labelled correct or unnatural; it failed them)");
    Console.WriteLine($"  False pass:       {headline.FalsePass}   (labelled wrong; it passed them)");
    Console.WriteLine($"  Naturalness:      {headline.NaturalnessMisses} disagreed on natural vs. unnatural (secondary)");
    Console.WriteLine();
    Console.WriteLine($"AI-agreed sentences it didn't help label: {aiAgreed.Items}, of which {aiAgreed.Failed} got no verdict");
    Console.WriteLine($"  Consistency:      {aiAgreed.Agreement}   (on the easier items: the hard ones went to a person, so this isn't accuracy)");
    if (selfLabelled > 0) {
        Console.WriteLine($"  Not scored:       {selfLabelled} AI-agreed sentences this model helped label");
    }
    Console.WriteLine();
    Console.WriteLine($"Tokens: {run.InputTokens:N0} in, {run.OutputTokens:N0} out");
    if (previous is not null) {
        Console.WriteLine($"Previous run ({previous.Provider} · {previous.Model}{(previous.Effort is null ? "" : $" ({previous.Effort})")}, {previous.StartedAt.ToLocalTime():MMM d}): " +
            $"headline agreement {previous.Agreement:P0} on {previous.Graded}, false correction {previous.FalseCorrectionRate:P0}, false pass {previous.FalsePassRate:P0}");
    }

    List<(GradingTestItem Item, GradingEvalResult Result, GradingVerdict? Verdict)> scored =
        outcomes.Where(o => !o.Result.GraderVoted).ToList();
    Console.WriteLine();
    Console.WriteLine("By grammar point (person-labelled and AI-agreed together, self-labelled left out):");
    foreach (var group in scored.GroupBy(o => o.Item.Pattern).OrderBy(g => g.Key, StringComparer.Ordinal)) {
        GradingScores s = Score(group);
        Console.WriteLine($"  {group.Key,-12} agreement {s.Agreement}, false correction {s.FalseCorrection}, false pass {s.FalsePass}{(s.Failed > 0 ? $", {s.Failed} failed" : "")}");
    }

    var disagreements = scored
        .Where(o => o.Verdict is not null && o.Verdict.TargetUsedCorrectly != o.Result.Label.TargetCorrect())
        .OrderBy(o => o.Result.LabelSource).ThenBy(o => o.Item.Pattern, StringComparer.Ordinal).ToList();
    if (disagreements.Count > 0) {
        Console.WriteLine();
        Console.WriteLine("Disagreements:");
        foreach (var d in disagreements) {
            string source = d.Result.LabelSource == LabelSource.Person ? "person" : "AI-agreed";
            Console.WriteLine($"  {d.Item.Sentence}");
            Console.WriteLine($"    label ({source}): {d.Result.Label.Display()}; grader: {GradingEval.Predicted(d.Verdict!)} ({d.Verdict!.Confidence} confidence)");
            Console.WriteLine($"    {d.Verdict.Explanation}");
        }
    }

    var failures = outcomes.Where(o => o.Verdict is null).ToList();
    if (failures.Count > 0) {
        Console.WriteLine();
        Console.WriteLine("No verdict:");
        foreach (var f in failures) {
            Console.WriteLine($"  {f.Item.Sentence}: {f.Result.Error}");
        }
    }
}

static async Task PrintHistoryAsync(IDbContextFactory<KotoNekoDbContext> factory)
{
    await using KotoNekoDbContext db = await factory.CreateDbContextAsync();
    List<GradingEvalRun> runs = await db.GradingEvalRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(30).ToListAsync();
    if (runs.Count == 0) {
        Console.WriteLine("No eval runs yet.");
        return;
    }
    foreach (GradingEvalRun r in runs) {
        Console.WriteLine($"{r.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}  {r.Provider} · {r.Model}{(r.Effort is null ? "" : $" ({r.Effort})")}  {r.PromptVersion}  " +
            $"person-labelled {r.Graded}: agreement {r.Agreement:P0}, false correction {r.FalseCorrectionRate:P0}, false pass {r.FalsePassRate:P0}  " +
            $"| AI-agreed {r.AiAgreedGraded}: consistency {r.AiAgreedAgreement:P0}");
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static Dictionary<string, List<string?>> ParseOptions(string[] args)
{
    Dictionary<string, List<string?>> result = new(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++) {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) {
            continue;
        }
        string key = args[i][2..];
        bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
        if (!result.TryGetValue(key, out List<string?>? values)) {
            result[key] = values = new List<string?>();
        }
        values.Add(hasValue ? args[++i] : null);
    }
    return result;
}

/// <summary>
/// Running token and cost totals, and the line printed after each call: that call's
/// tokens and cost, then the totals so far. Costs come from Llm:Prices; a model with
/// no price shows tokens only, and the total says it's incomplete.
/// </summary>
sealed class CostMeter(LlmPricing pricing)
{
    private long _input;
    private long _output;
    private decimal _cost;
    private readonly HashSet<string> _unpriced = new(StringComparer.OrdinalIgnoreCase);

    public string PriceText(string model) =>
        pricing.Cost(model, 1_000_000, 0) is decimal input && pricing.Cost(model, 0, 1_000_000) is decimal output
            ? $"${input:0.##} per million tokens in, ${output:0.##} out"
            : "no price set (add it to Llm:Prices in appsettings.json); tokens will still be shown";

    public string Line(int done, int total, string who, string item, string model, int input, int output, string? error)
    {
        _input += input;
        _output += output;
        decimal? cost = pricing.Cost(model, input, output);
        if (cost is decimal c) {
            _cost += c;
        } else if (input + output > 0) {
            _unpriced.Add(model);
        }
        string head = $"[{done.ToString().PadLeft(total.ToString().Length)}/{total}] {who}  {item}";
        string call = $"in {input:N0}  out {output:N0}  {(cost is decimal x ? $"${x:0.0000}" : "$?")}";
        string status = error is null ? "" : $"  FAILED: {error}";
        return $"{head}  {call}  | total in {_input:N0}  out {_output:N0}  {CostText()}{status}";
    }

    public string TotalText() => $"{_input:N0} tokens in, {_output:N0} out, {CostText()}";

    private string CostText() =>
        _unpriced.Count == 0 ? $"${_cost:0.0000}" : $"${_cost:0.0000} + unpriced ({string.Join(", ", _unpriced)})";
}
