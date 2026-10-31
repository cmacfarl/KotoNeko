namespace KotoNeko.Core.Llm;

/// <summary>A model that can be picked for a use case on the Models page.</summary>
public sealed record ModelChoice(string Provider, string Model)
{
    /// <summary>How the choice is written in a select: "Provider|model".</summary>
    public string Key => $"{Provider}|{Model}";

    public static ModelChoice? Parse(string? key)
    {
        string[] parts = (key ?? "").Split('|', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0 ? new ModelChoice(parts[0], parts[1]) : null;
    }
}

/// <summary>Which provider serves a model, judged from its ID, for offering priced models as choices.</summary>
public static class ModelChoices
{
    /// <summary>"Claude" for claude-*, "Gemini" for gemini-*, "OpenAI" for gpt-*; null for a model no provider here serves.</summary>
    public static string? ProviderOf(string model) =>
        model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ? "Claude"
        : model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ? "Gemini"
        : model.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase) ? "OpenAI"
        : null;

    /// <summary>Every model a provider here serves, in the order given, whether or not its provider is set up.</summary>
    public static List<ModelChoice> All(IEnumerable<string> models) =>
        models.Select(m => ProviderOf(m) is { } p ? new ModelChoice(p, m) : null)
            .Where(c => c is not null)
            .Select(c => c!)
            .ToList();

    /// <summary>The models whose provider is set up, in the order given.</summary>
    public static List<ModelChoice> Available(IEnumerable<string> models, Func<string, bool> providerConfigured) =>
        All(models).Where(c => providerConfigured(c.Provider)).ToList();
}

/// <summary>
/// The model chosen for one use case on the Models page. A use case with no row uses
/// its recommended model (its job's route in Llm:Jobs), so editing appsettings still
/// changes everything that hasn't been overridden.
/// </summary>
public class ModelAssignment
{
    public LlmFeature Feature { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }

    public const int MaxProviderLength = 50;
    public const int MaxModelLength = 100;
}

/// <summary>Something the app asks a model to do, as listed on the Models page.</summary>
/// <param name="Job">The job whose route in Llm:Jobs is the recommended model.</param>
public sealed record ModelUseCase(LlmFeature Feature, LlmJob Job, string Name, string Description);

/// <summary>
/// The use cases whose model can be chosen. Pre-labelling (several models vote, by design)
/// and the grading eval (a console tool with its own arguments) aren't here: they keep
/// to configuration.
/// </summary>
public static class ModelUseCases
{
    public static readonly IReadOnlyList<ModelUseCase> All = [
        new(LlmFeature.PracticeSentences, LlmJob.Generate, "Practice sentences", "Writes the practice sentences for a vocabulary word."),
        new(LlmFeature.GrammarDrafting, LlmJob.Generate, "Grammar drafting", "Drafts a grammar point's explanation, examples and ideas."),
        new(LlmFeature.LessonGrading, LlmJob.Grade, "Lesson grading", "Grades the sentences you write in grammar lessons."),
        new(LlmFeature.ReviewGrading, LlmJob.Grade, "Review grading", "Grades the sentences you write in grammar reviews."),
        new(LlmFeature.SecondOpinion, LlmJob.Recheck, "Second opinions", "Looks again at a review the grader wasn't sure was a miss."),
        new(LlmFeature.Dispute, LlmJob.Recheck, "Disputes", "Looks again at a review verdict you dispute."),
        new(LlmFeature.WhyQuestion, LlmJob.Explain, "\"Why?\" answers", "Answers your questions about a verdict."),
        new(LlmFeature.ListeningScript, LlmJob.Generate, "Listening scripts", "Writes stories and dialogues for extensive listening."),
        new(LlmFeature.ListeningComprehension, LlmJob.Grade, "Comprehension grading", "Grades your answer to a listening script's question."),
        new(LlmFeature.CasualRewrite, LlmJob.Generate, "Casual versions", "Rewrites sentences in casual speech for dictation."),
    ];

    /// <summary>The use case for a feature, or null for one whose model can't be chosen.</summary>
    public static ModelUseCase? For(LlmFeature feature) => All.FirstOrDefault(u => u.Feature == feature);
}
