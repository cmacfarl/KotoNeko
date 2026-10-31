using System.Diagnostics;
using System.Text.Json;
using Anthropic.Exceptions;
using KotoNeko.Core.Llm;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KotoNeko.Llm;

/// <summary>Which provider and model a job uses.</summary>
public sealed record LlmRoute(LlmJob Job, string Provider, string Model, bool IsConfigured);

/// <summary>A parsed answer, with the route that produced it, its tokens and the raw JSON.</summary>
public sealed record LlmResult<T>(T Value, LlmRoute Route, int InputTokens, int OutputTokens, string Json);

/// <summary>A provider and whether it can be called.</summary>
public sealed record LlmProviderStatus(string Name, bool IsConfigured, string HowToConfigure);

/// <summary>A model that can be chosen on the Models page, and whether its provider is set up.</summary>
public sealed record ModelOption(ModelChoice Choice, bool IsConfigured);

/// <summary>
/// Calls an LLM and returns the answer parsed into <typeparamref name="T"/>. A request
/// goes to the model chosen for its use case on the Models page (stored in the
/// ModelAssignments table and held here), or, with no choice or one whose provider
/// isn't set up, to its job's recommended route in configuration (Llm:Jobs:{Job}).
/// Every call is logged with its tokens, time and outcome. Answers are validated: a
/// model's promise to follow the schema isn't trusted.
/// </summary>
public sealed class LlmService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly LlmPricing _pricing;
    private readonly ILogger<LlmService> _logger;

    // The Models page's choices, replaced whole on each change so readers never see half of one.
    private IReadOnlyDictionary<LlmFeature, ModelChoice> _assignments = new Dictionary<LlmFeature, ModelChoice>();
    private bool _loaded;

    public LlmService(
        IEnumerable<ILlmProvider> providers, IConfiguration configuration,
        IDbContextFactory<KotoNekoDbContext> factory, LlmPricing pricing, ILogger<LlmService> logger)
    {
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _configuration = configuration;
        _factory = factory;
        _pricing = pricing;
        _logger = logger;
    }

    /// <summary>A job's route in configuration (the recommended model for its use cases), or null when none is set.</summary>
    public LlmRoute? RecommendedRoute(LlmJob job)
    {
        string? provider = _configuration[$"Llm:Jobs:{job}:Provider"];
        string? model = _configuration[$"Llm:Jobs:{job}:Model"];
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model)) {
            return null;
        }
        bool configured = _providers.TryGetValue(provider, out ILlmProvider? p) && p.IsConfigured;
        return new LlmRoute(job, p?.Name ?? provider, model, configured);
    }

    /// <summary>
    /// Where a use case's calls go: the model chosen on the Models page when its provider
    /// is set up, otherwise the recommended route. Null when neither is set.
    /// </summary>
    public LlmRoute? Route(LlmFeature feature) => Route(feature, JobOf(feature));

    private LlmRoute? Route(LlmFeature feature, LlmJob job) =>
        Assigned(feature) is { } chosen && IsConfigured(chosen.Provider)
            ? new LlmRoute(job, _providers[chosen.Provider].Name, chosen.Model, true)
            : RecommendedRoute(job);

    /// <summary>The job whose route is a feature's recommended model.</summary>
    public static LlmJob JobOf(LlmFeature feature) => ModelUseCases.For(feature)?.Job ?? feature switch {
        LlmFeature.Prelabelling => LlmJob.Label,
        LlmFeature.GradingEval => LlmJob.Grade,
        _ => LlmJob.Generate,
    };

    /// <summary>The model chosen for a use case, whether or not its provider is set up; null for the recommended one.</summary>
    public ModelChoice? Assigned(LlmFeature feature) => _assignments.GetValueOrDefault(feature);

    /// <summary>Read the Models page's choices from the database. Called at startup, and before a call if it wasn't.</summary>
    public async Task LoadAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken);
        _assignments = (await db.ModelAssignments.AsNoTracking().ToListAsync(cancellationToken))
            .ToDictionary(a => a.Feature, a => new ModelChoice(a.Provider, a.Model));
        _loaded = true;
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded) {
            return;
        }
        try {
            await LoadAssignmentsAsync(cancellationToken);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // Without the database the recommended models still work.
            _logger.LogWarning(ex, "Couldn't read the model choices; using the recommended models.");
        }
    }

    /// <summary>
    /// The models that can be chosen: every priced model a provider here serves (by ID,
    /// as configuration sorts them), plus any recommended route that isn't priced (a local model).
    /// </summary>
    public IReadOnlyList<ModelOption> ModelOptions()
    {
        List<ModelChoice> choices = ModelChoices.All(_pricing.Models);
        foreach (LlmJob job in Enum.GetValues<LlmJob>()) {
            if (RecommendedRoute(job) is { } route && !choices.Any(c => Same(c, route.Provider, route.Model))) {
                choices.Add(new ModelChoice(route.Provider, route.Model));
            }
        }
        return choices.Select(c => new ModelOption(c, IsConfigured(c.Provider))).ToList();
    }

    /// <summary>
    /// Choose a use case's model, or with null go back to the recommended one. Only a
    /// listed model whose provider is set up can be chosen.
    /// </summary>
    public async Task SetAssignmentAsync(LlmFeature feature, ModelChoice? choice)
    {
        if (ModelUseCases.For(feature) is null) {
            throw new InvalidOperationException($"The model for {feature} can't be chosen.");
        }
        if (choice is not null && !ModelOptions().Any(o => o.IsConfigured && Same(o.Choice, choice.Provider, choice.Model))) {
            throw new InvalidOperationException($"{choice.Provider} · {choice.Model} can't be chosen: it isn't listed, or its provider has no key.");
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        ModelAssignment? row = await db.ModelAssignments.FindAsync(feature);
        if (choice is null) {
            if (row is not null) {
                db.ModelAssignments.Remove(row);
            }
        } else {
            if (row is null) {
                row = new ModelAssignment { Feature = feature };
                db.ModelAssignments.Add(row);
            }
            row.Provider = choice.Provider;
            row.Model = choice.Model;
            row.ChangedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        await LoadAssignmentsAsync();
    }

    private static bool Same(ModelChoice choice, string provider, string model) =>
        string.Equals(choice.Provider, provider, StringComparison.OrdinalIgnoreCase) && choice.Model == model;

    public IReadOnlyList<LlmProviderStatus> Providers() => _providers.Values
        .Select(p => new LlmProviderStatus(p.Name, p.IsConfigured, p.Name switch {
            "Claude" => "Llm:Claude:ApiKey in appsettings.Development.json, or the ANTHROPIC_API_KEY environment variable",
            "Gemini" => "Llm:Gemini:ApiKey in appsettings.Development.json, or the GEMINI_API_KEY environment variable",
            "OpenAI" => "Llm:OpenAI:ApiKey in appsettings.Development.json, or the OPENAI_API_KEY environment variable",
            _ => "Llm:LmStudio:BaseUrl (LM Studio's server address, e.g. http://localhost:1234/v1)",
        }))
        .ToList();

    /// <summary>
    /// Run a request on its use case's route and parse the answer. <paramref name="validate"/>
    /// returns a problem description, or null when the answer is usable. Throws
    /// <see cref="LlmException"/> when there's no usable answer.
    /// </summary>
    public async Task<T> CallAsync<T>(LlmRequest request, Func<T, string?> validate, CancellationToken cancellationToken = default) =>
        (await CallWithUsageAsync(request, validate, cancellationToken)).Value;

    /// <summary>As <see cref="CallAsync{T}"/>, also returning the route used and the tokens spent.</summary>
    public async Task<LlmResult<T>> CallWithUsageAsync<T>(LlmRequest request, Func<T, string?> validate, CancellationToken cancellationToken = default)
    {
        await EnsureLoadedAsync(cancellationToken);
        LlmRoute route = Route(request.Feature, request.Job)
            ?? throw new LlmException(LlmOutcome.NotConfigured,
                $"No model is set for this: choose one on the Models page, or set Llm:Jobs:{request.Job} in appsettings.json.");
        return await CallOnAsync(route.Provider, route.Model, request, validate, cancellationToken);
    }

    /// <summary>Whether a provider exists and has its key or address.</summary>
    public bool IsConfigured(string provider) => _providers.TryGetValue(provider, out ILlmProvider? p) && p.IsConfigured;

    /// <summary>
    /// As <see cref="CallWithUsageAsync{T}"/>, on a named provider and model rather than
    /// the use case's route (pre-labelling asks several models the same question).
    /// </summary>
    public async Task<LlmResult<T>> CallOnAsync<T>(
        string providerName, string model, LlmRequest request, Func<T, string?> validate, CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(providerName, out ILlmProvider? provider)) {
            throw new LlmException(LlmOutcome.NotConfigured, $"Unknown provider {providerName} (Claude, Gemini, OpenAI or LmStudio).");
        }
        if (!provider.IsConfigured) {
            throw new LlmException(LlmOutcome.NotConfigured, $"{provider.Name} isn't configured: set its key or address (see the Models page).");
        }
        LlmRoute route = new(request.Job, provider.Name, model, true);

        Stopwatch timer = Stopwatch.StartNew();
        LlmResponse? response = null;
        try {
            response = await provider.CompleteJsonAsync(request, route.Model, cancellationToken);
            T? parsed;
            try {
                parsed = JsonSerializer.Deserialize<T>(response.Json, JsonOptions);
            } catch (JsonException ex) {
                throw new LlmException(LlmOutcome.BadOutput, $"{provider.Name}'s answer wasn't valid JSON.", ex);
            }
            if (parsed is null || validate(parsed) is string problem && problem.Length > 0) {
                throw new LlmException(LlmOutcome.BadOutput,
                    $"{provider.Name}'s answer didn't have the expected shape{(parsed is null ? "" : ": " + validate(parsed))}.");
            }
            await LogAsync(request, route, response, timer, LlmOutcome.Ok, null);
            return new LlmResult<T>(parsed, route, response.InputTokens, response.OutputTokens, response.Json);
        } catch (LlmException ex) {
            await LogAsync(request, route, response, timer, ex.Outcome, ex.Message);
            ex.InputTokens = response?.InputTokens ?? 0;
            ex.OutputTokens = response?.OutputTokens ?? 0;
            throw;
        } catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested)) {
            // Anything else (an SDK error, a network failure, a timeout, a malformed
            // response body) is logged and reported, never left to crash the page.
            string message = Describe(provider.Name, ex, cancellationToken);
            await LogAsync(request, route, response, timer, LlmOutcome.Failed, message);
            throw new LlmException(LlmOutcome.Failed, message, ex) {
                InputTokens = response?.InputTokens ?? 0,
                OutputTokens = response?.OutputTokens ?? 0,
            };
        }
    }

    /// <summary>A failure in words, most specific first: retrying helps some of these, not others.</summary>
    private static string Describe(string provider, Exception ex, CancellationToken cancellationToken) => ex switch {
        AnthropicUnauthorizedException => $"{provider} rejected the API key.",
        AnthropicForbiddenException => $"{provider} refused access with this key.",
        AnthropicRateLimitException => $"{provider} is rate-limiting requests; try again in a minute.",
        Anthropic5xxException => $"{provider} is having trouble right now; try again later.",
        AnthropicBadRequestException => $"{provider} rejected the request: {ex.Message}",
        AnthropicIOException => $"Couldn't reach {provider}: {ex.Message}",
        TaskCanceledException when !cancellationToken.IsCancellationRequested => $"{provider} took too long to answer (Llm:TimeoutSeconds).",
        _ => $"{provider} call failed: {ex.Message}",
    };

    private async Task LogAsync(LlmRequest request, LlmRoute route, LlmResponse? response, Stopwatch timer, LlmOutcome outcome, string? error)
    {
        try {
            await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
            db.LlmCalls.Add(new LlmCall {
                Job = request.Job,
                Feature = request.Feature,
                Provider = route.Provider,
                Model = route.Model,
                PromptVersion = request.PromptVersion,
                InputTokens = response?.InputTokens ?? 0,
                OutputTokens = response?.OutputTokens ?? 0,
                Cost = _pricing.Cost(route.Model, response?.InputTokens ?? 0, response?.OutputTokens ?? 0),
                DurationMs = (int)Math.Min(int.MaxValue, timer.ElapsedMilliseconds),
                Outcome = outcome,
                Error = error is { Length: > 500 } ? error[..500] : error,
                CalledAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        } catch (Exception ex) {
            // The call log must never turn a good answer into a failure.
            _logger.LogWarning(ex, "Couldn't log an LLM call.");
        }
        if (outcome != LlmOutcome.Ok) {
            _logger.LogWarning("LLM {Job} call on {Provider} {Model}: {Outcome} {Error}", request.Job, route.Provider, route.Model, outcome, error);
        }
    }

    /// <summary>Calls and tokens over the last <paramref name="days"/> days, by outcome.</summary>
    public async Task<(int Calls, int Failed, long InputTokens, long OutputTokens)> UsageAsync(int days)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        DateTime since = DateTime.UtcNow.AddDays(-days);
        var calls = await db.LlmCalls.AsNoTracking().Where(c => c.CalledAt >= since)
            .Select(c => new { c.Outcome, c.InputTokens, c.OutputTokens }).ToListAsync();
        return (calls.Count, calls.Count(c => c.Outcome != LlmOutcome.Ok),
            calls.Sum(c => (long)c.InputTokens), calls.Sum(c => (long)c.OutputTokens));
    }
}
