using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using KotoNeko.Core.Llm;

namespace KotoNeko.Llm;

/// <summary>
/// Claude, through the official Anthropic SDK, with structured output. Requests use
/// the server-side refusal fallback ("default" routing), so a request a safety
/// classifier declines is answered by a fallback model where one applies; a refusal
/// that still comes back is reported as <see cref="LlmOutcome.Refused"/>.
/// </summary>
public sealed class ClaudeProvider : ILlmProvider
{
    private readonly string? _apiKey;
    private readonly string _effort;
    private AnthropicClient? _client;

    /// <param name="apiKey">From Llm:Claude:ApiKey or ANTHROPIC_API_KEY.</param>
    /// <param name="effort">low, medium, high, xhigh or max (Llm:Claude:Effort).</param>
    public ClaudeProvider(string? apiKey, string? effort)
    {
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _effort = string.IsNullOrWhiteSpace(effort) ? "medium" : effort.Trim().ToLowerInvariant();
    }

    public string Name => "Claude";

    public bool IsConfigured => _apiKey is not null;

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        if (_apiKey is null) {
            throw new LlmException(LlmOutcome.NotConfigured, "No Claude API key is configured.");
        }
        _client ??= new AnthropicClient { ApiKey = _apiKey };

        Dictionary<string, JsonElement> schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Schema.ToJsonString())!;
        BetaMessage message = await _client.Beta.Messages.Create(new MessageCreateParams {
            Model = model,
            MaxTokens = request.MaxTokens,
            Betas = ["server-side-fallback-2026-07-01"],
            Fallbacks = new Default(),
            System = request.System,
            OutputConfig = new BetaOutputConfig {
                Effort = request.Effort ?? _effort,
                Format = new BetaJsonOutputFormat { Schema = schema },
            },
            Messages = [new BetaUserMessageParam { Content = request.Prompt }],
        }, cancellationToken);

        if (message.StopReason == "refusal") {
            string category = message.StopDetails?.Category?.ToString() ?? "unspecified";
            throw new LlmException(LlmOutcome.Refused, $"Claude declined the request ({category}).");
        }
        if (message.StopReason == "max_tokens") {
            throw new LlmException(LlmOutcome.BadOutput, "Claude's answer was cut off at the token limit.");
        }

        StringBuilder text = new();
        foreach (BetaContentBlock block in message.Content) {
            if (block.TryPickText(out BetaTextBlock? part)) {
                text.Append(part.Text);
            }
        }
        return new LlmResponse(text.ToString(), (int)message.Usage.InputTokens, (int)message.Usage.OutputTokens);
    }
}
