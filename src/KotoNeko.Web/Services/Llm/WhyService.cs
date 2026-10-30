using KotoNeko.Core.Llm;
using KotoNeko.Llm;

namespace KotoNeko.Web.Services.Llm;

/// <summary>
/// Answers "Why?" questions about a verdict with the Explain job's LLM. Answers aren't
/// stored; the call log keeps their cost.
/// </summary>
public sealed class WhyService
{
    private readonly LlmService _llm;

    public WhyService(LlmService llm)
    {
        _llm = llm;
    }

    /// <summary>Where questions go, or null when no route is configured.</summary>
    public LlmRoute? Route => _llm.Route(LlmJob.Explain);

    /// <summary>The answer. Throws <see cref="LlmException"/> when there's no usable answer.</summary>
    public async Task<string> AskAsync(
        GradingTask task, GradingVerdict verdict, IReadOnlyList<WhyExchange> earlier, string question,
        CancellationToken cancellationToken = default)
    {
        WhyAnswer answer = await _llm.CallAsync<WhyAnswer>(
            Explaining.Build(task, verdict, earlier, question), Explaining.Validate, cancellationToken);
        return answer.Answer.Trim();
    }
}
