using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KotoNeko.Core.Llm;

namespace KotoNeko.Llm;

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
/// Gemini, through the generateContent REST endpoint with the JSON schema passed as
/// is (responseJsonSchema) and a thinking level (Llm:Gemini:ThinkingLevel, or the
/// request's effort). Google now recommends its newer Interactions API, but
/// generateContent remains fully supported and is the simpler fit for one
/// structured call.
/// </summary>
public sealed class GeminiProvider : ILlmProvider
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _thinkingLevel;

    /// <param name="apiKey">From Llm:Gemini:ApiKey or GEMINI_API_KEY.</param>
    /// <param name="thinkingLevel">low, medium or high (Llm:Gemini:ThinkingLevel); default low.</param>
    public GeminiProvider(HttpClient http, string? apiKey, string? thinkingLevel = null)
    {
        _http = http;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _thinkingLevel = string.IsNullOrWhiteSpace(thinkingLevel) ? "low" : thinkingLevel.Trim().ToLowerInvariant();
    }

    public string Name => "Gemini";

    /// <summary>The generateContent request body.</summary>
    public static JsonObject Body(LlmRequest request, string model, string thinkingLevel) => new() {
        ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
        ["contents"] = new JsonArray(new JsonObject {
            ["role"] = "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = request.Prompt }),
        }),
        ["generationConfig"] = new JsonObject {
            ["responseMimeType"] = "application/json",
            ["responseJsonSchema"] = request.Schema.DeepClone(),
            ["maxOutputTokens"] = request.MaxTokens,
            ["thinkingConfig"] = ThinkingConfig(model, thinkingLevel),
        },
    };

    /// <summary>
    /// Gemini 3 and later take a thinking level; 2.x models predate levels and take a
    /// token budget, so the level is mapped to one.
    /// </summary>
    public static JsonObject ThinkingConfig(string model, string level)
    {
        level = level switch { "xhigh" or "max" => "high", "minimal" => "low", _ => level };
        if (model.StartsWith("gemini-2.", StringComparison.Ordinal)) {
            return new JsonObject { ["thinkingBudget"] = level switch { "low" => 1024, "high" => 24576, _ => 8192 } };
        }
        return new JsonObject { ["thinkingLevel"] = level };
    }

    public bool IsConfigured => _apiKey is not null;

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        if (_apiKey is null) {
            throw new LlmException(LlmOutcome.NotConfigured, "No Gemini API key is configured.");
        }

        JsonObject body = Body(request, model, request.Effort ?? _thinkingLevel);

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

/// <summary>
/// OpenAI's GPT models, through the Responses REST endpoint with a strict JSON schema
/// (text.format) and a reasoning effort (Llm:OpenAI:Effort, or the request's effort).
/// Responses aren't stored on OpenAI's side (store: false). The address is a setting
/// (Llm:OpenAI:BaseUrl) only so a local stand-in can answer in tests.
/// </summary>
public sealed class OpenAIProvider : ILlmProvider
{
    public const string DefaultBaseUrl = "https://api.openai.com/v1";

    private readonly HttpClient _http;
    private readonly string? _apiKey;
    private readonly string _effort;
    private readonly string _baseUrl;

    /// <param name="apiKey">From Llm:OpenAI:ApiKey or OPENAI_API_KEY.</param>
    /// <param name="effort">low, medium, high, xhigh or max (Llm:OpenAI:Effort); default medium.</param>
    /// <param name="baseUrl">Llm:OpenAI:BaseUrl; default https://api.openai.com/v1.</param>
    public OpenAIProvider(HttpClient http, string? apiKey, string? effort = null, string? baseUrl = null)
    {
        _http = http;
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _effort = string.IsNullOrWhiteSpace(effort) ? "medium" : effort.Trim().ToLowerInvariant();
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim().TrimEnd('/');
    }

    public string Name => "OpenAI";

    public bool IsConfigured => _apiKey is not null;

    /// <summary>
    /// The effort sent. Not every GPT model takes none or minimal (GPT-6 Astra rejects
    /// none, GPT-6.1 Sol both), so those become low; the rest pass through.
    /// </summary>
    public static string Effort(string level) => level is "none" or "minimal" ? "low" : level;

    /// <summary>The Responses request body.</summary>
    public static JsonObject Body(LlmRequest request, string model, string effort) => new() {
        ["model"] = model,
        ["instructions"] = request.System,
        ["input"] = request.Prompt,
        ["text"] = new JsonObject {
            ["format"] = new JsonObject {
                ["type"] = "json_schema",
                ["name"] = request.SchemaName,
                ["strict"] = true,
                ["schema"] = request.Schema.DeepClone(),
            },
        },
        ["reasoning"] = new JsonObject { ["effort"] = Effort(effort) },
        ["max_output_tokens"] = request.MaxTokens,
        ["store"] = false,
    };

    /// <summary>The answer in a Responses reply. Throws for a refusal or an answer cut short.</summary>
    public static LlmResponse Parse(JsonNode response)
    {
        if (response["status"]?.GetValue<string>() == "incomplete") {
            string reason = response["incomplete_details"]?["reason"]?.GetValue<string>() ?? "unspecified";
            throw reason == "max_output_tokens"
                ? new LlmException(LlmOutcome.BadOutput, "OpenAI's answer was cut off at the token limit.")
                : new LlmException(LlmOutcome.Refused, $"OpenAI stopped the answer ({reason}).");
        }

        // The output holds reasoning items and then the message; only the message's text is the answer.
        StringBuilder text = new();
        foreach (JsonNode? item in response["output"]?.AsArray() ?? new JsonArray()) {
            if (item?["type"]?.GetValue<string>() != "message") {
                continue;
            }
            foreach (JsonNode? part in item["content"]?.AsArray() ?? new JsonArray()) {
                switch (part?["type"]?.GetValue<string>()) {
                    case "refusal":
                        throw new LlmException(LlmOutcome.Refused, $"OpenAI declined the request: {part["refusal"]?.GetValue<string>()}");
                    case "output_text":
                        text.Append(part["text"]?.GetValue<string>());
                        break;
                }
            }
        }

        // Output tokens include reasoning tokens.
        JsonNode? usage = response["usage"];
        return new LlmResponse(text.ToString(),
            usage?["input_tokens"]?.GetValue<int>() ?? 0, usage?["output_tokens"]?.GetValue<int>() ?? 0);
    }

    public async Task<LlmResponse> CompleteJsonAsync(LlmRequest request, string model, CancellationToken cancellationToken)
    {
        if (_apiKey is null) {
            throw new LlmException(LlmOutcome.NotConfigured, "No OpenAI API key is configured.");
        }

        JsonObject body = Body(request, model, request.Effort ?? _effort);
        JsonNode response = await RestCalls.PostAsync(_http, Name, () => {
            HttpRequestMessage message = new(HttpMethod.Post, _baseUrl + "/responses") {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            return message;
        }, cancellationToken);
        return Parse(response);
    }
}
