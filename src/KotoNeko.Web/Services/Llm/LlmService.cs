using System.Diagnostics;
using System.Text.Json;
using Anthropic.Exceptions;
using KotoNeko.Core.Llm;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services.Llm;

/// <summary>Which provider and model a job uses.</summary>
public sealed record LlmRoute(LlmJob Job, string Provider, string Model, bool IsConfigured);

/// <summary>A provider and whether it can be called.</summary>
public sealed record LlmProviderStatus(string Name, bool IsConfigured, string HowToConfigure);

/// <summary>
/// Calls an LLM for a job, routed by configuration (Llm:Jobs:{Job}:Provider and
/// :Model), and returns the answer parsed into <typeparamref name="T"/>. Model IDs
/// live only in configuration. Every call is logged with its tokens, time and
/// outcome. Answers are validated: a model's promise to follow the schema isn't
/// trusted.
/// </summary>
public sealed class LlmService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IReadOnlyDictionary<string, ILlmProvider> _providers;
    private readonly IConfiguration _configuration;
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly ILogger<LlmService> _logger;

    public LlmService(
        IEnumerable<ILlmProvider> providers, IConfiguration configuration,
        IDbContextFactory<KotoNekoDbContext> factory, ILogger<LlmService> logger)
    {
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _configuration = configuration;
        _factory = factory;
        _logger = logger;
    }

    /// <summary>The configured route for a job, or null when none is set.</summary>
    public LlmRoute? Route(LlmJob job)
    {
        string? provider = _configuration[$"Llm:Jobs:{job}:Provider"];
        string? model = _configuration[$"Llm:Jobs:{job}:Model"];
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model)) {
            return null;
        }
        bool configured = _providers.TryGetValue(provider, out ILlmProvider? p) && p.IsConfigured;
        return new LlmRoute(job, p?.Name ?? provider, model, configured);
    }

    public IReadOnlyList<LlmProviderStatus> Providers() => _providers.Values
        .Select(p => new LlmProviderStatus(p.Name, p.IsConfigured, p.Name switch {
            "Claude" => "Llm:Claude:ApiKey in appsettings.Development.json, or the ANTHROPIC_API_KEY environment variable",
            "Gemini" => "Llm:Gemini:ApiKey in appsettings.Development.json, or the GEMINI_API_KEY environment variable",
            _ => "Llm:LmStudio:BaseUrl (LM Studio's server address, e.g. http://localhost:1234/v1)",
        }))
        .ToList();

    /// <summary>
    /// Run a request on its job's route and parse the answer. <paramref name="validate"/>
    /// returns a problem description, or null when the answer is usable. Throws
    /// <see cref="LlmException"/> when there's no usable answer.
    /// </summary>
    public async Task<T> CallAsync<T>(LlmRequest request, Func<T, string?> validate, CancellationToken cancellationToken = default)
    {
        LlmRoute route = Route(request.Job)
            ?? throw new LlmException(LlmOutcome.NotConfigured, $"No provider is set for the {request.Job} job (Llm:Jobs:{request.Job} in appsettings.json).");
        if (!_providers.TryGetValue(route.Provider, out ILlmProvider? provider)) {
            throw new LlmException(LlmOutcome.NotConfigured, $"The {request.Job} job names an unknown provider, {route.Provider}.");
        }
        if (!provider.IsConfigured) {
            throw new LlmException(LlmOutcome.NotConfigured, $"{provider.Name} isn't configured: set its key or address (see Settings).");
        }

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
            return parsed;
        } catch (LlmException ex) {
            await LogAsync(request, route, response, timer, ex.Outcome, ex.Message);
            throw;
        } catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested)) {
            // Anything else (an SDK error, a network failure, a timeout, a malformed
            // response body) is logged and reported, never left to crash the page.
            string message = Describe(provider.Name, ex, cancellationToken);
            await LogAsync(request, route, response, timer, LlmOutcome.Failed, message);
            throw new LlmException(LlmOutcome.Failed, message, ex);
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
                Provider = route.Provider,
                Model = route.Model,
                PromptVersion = request.PromptVersion,
                InputTokens = response?.InputTokens ?? 0,
                OutputTokens = response?.OutputTokens ?? 0,
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
