namespace KotoNeko.Core.Llm;

/// <summary>A model that can be picked for a job on the Settings page.</summary>
public sealed record ModelChoice(string Provider, string Model)
{
    /// <summary>How the choice is stored: "Provider|model".</summary>
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
    /// <summary>"Claude" for claude-*, "Gemini" for gemini-*; null for a model no provider here serves.</summary>
    public static string? ProviderOf(string model) =>
        model.StartsWith("claude-", StringComparison.OrdinalIgnoreCase) ? "Claude"
        : model.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ? "Gemini"
        : null;

    /// <summary>The models whose provider is set up, in the order given.</summary>
    public static List<ModelChoice> Available(IEnumerable<string> models, Func<string, bool> providerConfigured) =>
        models.Select(m => ProviderOf(m) is { } p ? new ModelChoice(p, m) : null)
            .Where(c => c is not null && providerConfigured(c.Provider))
            .Select(c => c!)
            .ToList();
}
