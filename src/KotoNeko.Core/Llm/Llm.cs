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

    /// <summary>Pre-labelling the grading test set (several models vote; people settle disagreements).</summary>
    Label = 3,
}

/// <summary>
/// The feature a call was made for, for the accounting page. Finer than <see cref="LlmJob"/>:
/// lesson grading and the grading eval share the Grade job and its prompt.
/// </summary>
public enum LlmFeature
{
    /// <summary>Logged before features were recorded, with a prompt version that didn't say.</summary>
    Unknown = 0,

    /// <summary>Writing practice sentences for a vocabulary word.</summary>
    PracticeSentences = 1,

    /// <summary>Drafting a grammar point's lesson content.</summary>
    GrammarDrafting = 2,

    /// <summary>Grading a sentence written in a grammar lesson.</summary>
    LessonGrading = 3,

    /// <summary>The grading eval (tests/KotoNeko.Evals).</summary>
    GradingEval = 4,

    /// <summary>Pre-labelling the grading test set.</summary>
    Prelabelling = 5,

    /// <summary>Grading a sentence written in a grammar review.</summary>
    ReviewGrading = 6,
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
/// results can be traced to the prompt that produced them, and so does <see cref="Feature"/>,
/// so spending can be split by what it was for. <see cref="Effort"/> (low, medium, high)
/// overrides the provider's configured effort or thinking level.
/// </summary>
public sealed record LlmRequest(
    LlmJob Job,
    LlmFeature Feature,
    string PromptVersion,
    string System,
    string Prompt,
    string SchemaName,
    JsonObject Schema,
    int MaxTokens = 8000,
    string? Effort = null);

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

    /// <summary>Tokens the call used before failing (an answer in the wrong shape still costs).</summary>
    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }
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
    public LlmFeature Feature { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int DurationMs { get; set; }
    public LlmOutcome Outcome { get; set; }

    /// <summary>
    /// What the call cost in US dollars, from Llm:Prices when it was made (failed calls
    /// that used tokens cost too). Null when the model had no price; the accounting
    /// page fills it in once a price is added.
    /// </summary>
    public decimal? Cost { get; set; }

    /// <summary>What went wrong, when it did.</summary>
    public string? Error { get; set; }

    public DateTime CalledAt { get; set; }
}
