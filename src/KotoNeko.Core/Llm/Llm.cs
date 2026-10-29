using System.Text.Json.Nodes;

namespace KotoNeko.Core.Llm;

/// <summary>What an LLM is being asked to do. Each job is routed to its own provider and model.</summary>
public enum LlmJob
{
    /// <summary>Writing practice sentences, prompts and examples.</summary>
    Generate = 0,

    /// <summary>Judging an answer (Phase 5).</summary>
    Grade = 1,

    /// <summary>"Why?" follow-ups (Phase 5).</summary>
    Explain = 2,
}

/// <summary>How an LLM call ended, for the call log.</summary>
public enum LlmOutcome
{
    Ok = 0,

    /// <summary>The provider declined to answer (a safety refusal or a blocked prompt).</summary>
    Refused = 1,

    /// <summary>An answer came back, but not in the required shape.</summary>
    BadOutput = 2,

    /// <summary>The call failed: network, timeout, quota, a provider error.</summary>
    Failed = 3,

    /// <summary>No provider is configured for the job.</summary>
    NotConfigured = 4,
}

/// <summary>
/// One structured-output request: a system prompt, a user prompt and the JSON schema
/// the answer must follow. <see cref="PromptVersion"/> goes in the call log, so old
/// results can be traced to the prompt that produced them.
/// </summary>
public sealed record LlmRequest(
    LlmJob Job,
    string PromptVersion,
    string System,
    string Prompt,
    string SchemaName,
    JsonObject Schema,
    int MaxTokens = 8000);

/// <summary>A provider's answer: the JSON text and the tokens it took.</summary>
public sealed record LlmResponse(string Json, int InputTokens, int OutputTokens);

/// <summary>A call that didn't produce a usable answer.</summary>
public sealed class LlmException : Exception
{
    public LlmException(LlmOutcome outcome, string message, Exception? inner = null)
        : base(message, inner)
    {
        Outcome = outcome;
    }

    public LlmOutcome Outcome { get; }
}

/// <summary>One LLM provider (Claude, Gemini, LM Studio): sends a request, returns JSON.</summary>
public interface ILlmProvider
{
    /// <summary>The name used in configuration and the call log.</summary>
    string Name { get; }

    /// <summary>Whether it has what it needs to be called (a key, or a local address).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Ask <paramref name="model"/> for an answer matching the request's schema.
    /// Throws <see cref="LlmException"/> for refusals and malformed answers.
    /// </summary>
    Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken);
}

/// <summary>Every LLM call, for tracking cost and quality.</summary>
public class LlmCall
{
    public long Id { get; set; }
    public LlmJob Job { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int DurationMs { get; set; }
    public LlmOutcome Outcome { get; set; }

    /// <summary>What went wrong, when it did.</summary>
    public string? Error { get; set; }

    public DateTime CalledAt { get; set; }
}
