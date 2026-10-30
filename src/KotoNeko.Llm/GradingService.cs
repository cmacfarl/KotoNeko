using KotoNeko.Core.Llm;

namespace KotoNeko.Llm;

/// <summary>
/// Grades a learner's sentence for one grammar point with the Grade job's LLM.
/// Used by the grading eval now and by composition reviews in Phase 5.
/// </summary>
public sealed class GradingService
{
    private readonly LlmService _llm;

    public GradingService(LlmService llm)
    {
        _llm = llm;
    }

    public LlmRoute? Route => _llm.Route(LlmJob.Grade);

    /// <summary>The verdict, with tokens and raw JSON. Throws <see cref="LlmException"/> when there's no usable verdict.</summary>
    /// <param name="feature">What the grading is for (a lesson or the eval), for the accounting page.</param>
    /// <param name="effort">Overrides the provider's configured effort or thinking level.</param>
    public Task<LlmResult<GradingVerdict>> GradeAsync(
        GradingTask task, LlmFeature feature, string? effort = null, CancellationToken cancellationToken = default) =>
        _llm.CallWithUsageAsync<GradingVerdict>(Grading.Build(task, feature) with { Effort = effort }, Grading.Validate, cancellationToken);

    /// <summary>Where second opinions go (the Recheck job), or null when no route is set.</summary>
    public LlmRoute? RecheckRoute => _llm.Route(LlmJob.Recheck);

    /// <summary>
    /// A second, independent verdict on a sentence. <paramref name="comment"/> is the
    /// learner's note on a disputed verdict. Throws <see cref="LlmException"/> when there's no usable verdict.
    /// </summary>
    public Task<LlmResult<GradingVerdict>> RecheckAsync(
        GradingTask task, LlmFeature feature, string comment = "", CancellationToken cancellationToken = default) =>
        _llm.CallWithUsageAsync<GradingVerdict>(Grading.BuildRecheck(task, feature, comment), Grading.Validate, cancellationToken);
}
