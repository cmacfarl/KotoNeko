using KotoNeko.Core.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KotoNeko.Llm;

public static class LlmServiceCollectionExtensions
{
    public const string HttpClientName = "llm";

    /// <summary>
    /// Register the LLM providers and <see cref="LlmService"/>, configured from
    /// <paramref name="configuration"/>: keys from Llm:Claude:ApiKey / Llm:Gemini:ApiKey / Llm:OpenAI:ApiKey
    /// (or ANTHROPIC_API_KEY / GEMINI_API_KEY / OPENAI_API_KEY), LM Studio from Llm:LmStudio:BaseUrl,
    /// the timeout from Llm:TimeoutSeconds, and each job's recommended route from Llm:Jobs.
    /// Shared by the web app and the eval, so they can't drift apart.
    /// </summary>
    public static IServiceCollection AddKotoNekoLlm(this IServiceCollection services, IConfiguration configuration)
    {
        // LlmService reads routes from configuration; the web host registers it, a console app may not.
        services.TryAddSingleton(configuration);

        // Local models can be slow (a minute or more per answer), so the timeout is a setting.
        services.AddHttpClient(HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("Llm:TimeoutSeconds", 300));
            client.DefaultRequestHeaders.Add("User-Agent", "KotoNeko/1.0 (personal study app)");
        });

        services.AddSingleton<ILlmProvider>(_ => new ClaudeProvider(
            FirstSet(configuration["Llm:Claude:ApiKey"], configuration["ANTHROPIC_API_KEY"]),
            configuration["Llm:Claude:Effort"]));
        services.AddSingleton<ILlmProvider>(sp => new GeminiProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            FirstSet(configuration["Llm:Gemini:ApiKey"], configuration["GEMINI_API_KEY"]),
            configuration["Llm:Gemini:ThinkingLevel"]));
        services.AddSingleton<ILlmProvider>(sp => new LmStudioProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            configuration["Llm:LmStudio:BaseUrl"]));
        services.AddSingleton<ILlmProvider>(sp => new OpenAIProvider(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            FirstSet(configuration["Llm:OpenAI:ApiKey"], configuration["OPENAI_API_KEY"]),
            configuration["Llm:OpenAI:Effort"], configuration["Llm:OpenAI:BaseUrl"]));
        services.AddSingleton<LlmService>();
        services.AddSingleton(new LlmPricing(configuration));
        services.AddSingleton<GradingService>();
        services.AddSingleton<PrelabelService>();
        return services;
    }

    private static string? FirstSet(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
