using Microsoft.Extensions.Configuration;

namespace KotoNeko.Llm;

/// <summary>
/// What a model costs, in dollars per million tokens, from Llm:Prices in configuration
/// (keyed by model ID, like the routes). Output tokens include thinking. A model with
/// no price shows tokens only.
/// </summary>
public sealed class LlmPricing
{
    private readonly Dictionary<string, (decimal Input, decimal Output)> _prices;

    public LlmPricing(IConfiguration configuration)
    {
        _prices = configuration.GetSection("Llm:Prices").GetChildren()
            .Where(c => decimal.TryParse(c["Input"], System.Globalization.CultureInfo.InvariantCulture, out _)
                && decimal.TryParse(c["Output"], System.Globalization.CultureInfo.InvariantCulture, out _))
            .ToDictionary(
                c => c.Key,
                c => (decimal.Parse(c["Input"]!, System.Globalization.CultureInfo.InvariantCulture),
                      decimal.Parse(c["Output"]!, System.Globalization.CultureInfo.InvariantCulture)),
                StringComparer.OrdinalIgnoreCase);
    }

    public bool Knows(string model) => _prices.ContainsKey(model);

    /// <summary>A model's price in dollars per million tokens, or null when it has none.</summary>
    public (decimal Input, decimal Output)? Price(string model) => _prices.TryGetValue(model, out var price) ? price : null;

    /// <summary>Every priced model ID, in configuration order.</summary>
    public IReadOnlyList<string> Models => _prices.Keys.ToList();

    /// <summary>The cost of a call in dollars, or null when the model has no price.</summary>
    public decimal? Cost(string model, long inputTokens, long outputTokens) =>
        _prices.TryGetValue(model, out var price)
            ? (inputTokens * price.Input + outputTokens * price.Output) / 1_000_000m
            : null;
}
