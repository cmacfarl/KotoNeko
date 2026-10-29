using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotoNeko.Core.Llm;

namespace KotoNeko.Web.Services.Llm;

/// <summary>Sending JSON to a REST provider, with one retry for a busy or flaky service.</summary>
internal static class RestCalls
{
    public static async Task<JsonNode> PostAsync(
        HttpClient http, string provider, Func<HttpRequestMessage> build, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++) {
            using HttpRequestMessage request = build();
            HttpResponseMessage response;
            try {
                response = await http.SendAsync(request, cancellationToken);
            } catch (HttpRequestException) when (attempt == 1) {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            using (response) {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                bool transient = response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
                if (transient && attempt == 1) {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode) {
                    throw new LlmException(LlmOutcome.Failed,
                        $"{provider} returned {(int)response.StatusCode} {response.ReasonPhrase}: {Shorten(body)}");
                }
                return JsonNode.Parse(body) ?? throw new LlmException(LlmOutcome.BadOutput, $"{provider} returned an empty response.");
            }
        }
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}

/// <summary>
/// Gemini, through the generateContent REST endpoint with a JSON response schema.
/// (Google now recommends its newer Interactions API, but generateContent remains
/// fully supported and is the simpler fit for one structured call.)
/// </summary>
public sealed class GeminiProvider : ILlmProvider
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    /// <param name="apiKey">From Llm:Gemini:ApiKey or GEMINI_API_KEY.</param>
    public GeminiProvider(HttpClient http, string? apiKey)
    {
        _http = http;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public string Name => "Gemini";

    public bool IsConfigured => _apiKey is not null;

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        if (_apiKey is null) {
            throw new LlmException(LlmOutcome.NotConfigured, "No Gemini API key is configured.");
        }

        JsonObject body = new() {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
            ["contents"] = new JsonArray(new JsonObject {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = request.Prompt }),
            }),
            ["generationConfig"] = new JsonObject {
                ["responseMimeType"] = "application/json",
                ["responseSchema"] = LlmSchemas.ForGemini(request.Schema),
                ["maxOutputTokens"] = request.MaxTokens,
            },
        };

        string url = string.Format(Endpoint, Uri.EscapeDataString(model));
        JsonNode response = await RestCalls.PostAsync(_http, Name, () => {
            HttpRequestMessage message = new(HttpMethod.Post, url) {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Add("x-goog-api-key", _apiKey);
            return message;
        }, cancellationToken);

        if (response["promptFeedback"]?["blockReason"]?.GetValue<string>() is { } blocked) {
            throw new LlmException(LlmOutcome.Refused, $"Gemini blocked the request ({blocked}).");
        }
        JsonNode? candidate = response["candidates"]?[0];
        string? finish = candidate?["finishReason"]?.GetValue<string>();
        if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST" or "RECITATION") {
            throw new LlmException(LlmOutcome.Refused, $"Gemini stopped the answer ({finish}).");
        }
        if (finish == "MAX_TOKENS") {
            throw new LlmException(LlmOutcome.BadOutput, "Gemini's answer was cut off at the token limit.");
        }

        // Thought summaries, if any, are parts marked "thought"; the answer is the rest.
        StringBuilder text = new();
        foreach (JsonNode? part in candidate?["content"]?["parts"]?.AsArray() ?? new JsonArray()) {
            if (part?["thought"]?.GetValue<bool>() != true && part?["text"]?.GetValue<string>() is { } t) {
                text.Append(t);
            }
        }

        JsonNode? usage = response["usageMetadata"];
        int input = usage?["promptTokenCount"]?.GetValue<int>() ?? 0;
        int output = (usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0) + (usage?["thoughtsTokenCount"]?.GetValue<int>() ?? 0);
        return new LlmResponse(text.ToString(), input, output);
    }
}

/// <summary>
/// A local model served by LM Studio, through its OpenAI-compatible chat completions
/// endpoint with a JSON schema response format. How well a model keeps to the
/// schema varies, so every answer is still validated.
/// </summary>
public sealed class LmStudioProvider : ILlmProvider
{
    private readonly HttpClient _http;
    private readonly string? _baseUrl;

    /// <param name="baseUrl">Llm:LmStudio:BaseUrl, e.g. http://localhost:1234/v1.</param>
    public LmStudioProvider(HttpClient http, string? baseUrl)
    {
        _http = http;
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl.Trim().TrimEnd('/');
    }

    public string Name => "LmStudio";

    public bool IsConfigured => _baseUrl is not null;

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        if (_baseUrl is null) {
            throw new LlmException(LlmOutcome.NotConfigured, "No LM Studio address is configured.");
        }

        JsonObject body = new() {
            ["model"] = model,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = request.System },
                new JsonObject { ["role"] = "user", ["content"] = request.Prompt }),
            ["response_format"] = new JsonObject {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject {
                    ["name"] = request.SchemaName,
                    ["strict"] = true,
                    ["schema"] = request.Schema.DeepClone(),
                },
            },
            ["max_tokens"] = request.MaxTokens,
        };

        JsonNode response;
        try {
            response = await RestCalls.PostAsync(_http, "LM Studio", () => new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions") {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            }, cancellationToken);
        } catch (HttpRequestException ex) {
            throw new LlmException(LlmOutcome.Failed, $"LM Studio isn't reachable at {_baseUrl}. Is its server running?", ex);
        }

        JsonNode? choice = response["choices"]?[0];
        if (choice?["finish_reason"]?.GetValue<string>() == "length") {
            throw new LlmException(LlmOutcome.BadOutput, "The local model's answer was cut off at the token limit.");
        }
        string text = choice?["message"]?["content"]?.GetValue<string>() ?? string.Empty;
        int input = response["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0;
        int output = response["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0;
        return new LlmResponse(text, input, output);
    }
}
