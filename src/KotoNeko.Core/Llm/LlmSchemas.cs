using System.Text.Json.Nodes;

namespace KotoNeko.Core.Llm;

/// <summary>Adapting a JSON schema to what a provider accepts.</summary>
public static class LlmSchemas
{
    /// <summary>
    /// Gemini's response schema is an OpenAPI subset: type names in capitals, and no
    /// additionalProperties.
    /// </summary>
    public static JsonNode ForGemini(JsonNode schema)
    {
        switch (schema) {
            case JsonObject obj: {
                JsonObject result = new();
                foreach ((string key, JsonNode? value) in obj) {
                    if (key == "additionalProperties") {
                        continue;
                    }
                    result[key] = key == "type" && value is JsonValue v && v.TryGetValue(out string? type)
                        ? type.ToUpperInvariant()
                        : value is null ? null : ForGemini(value);
                }
                return result;
            }
            case JsonArray array:
                return new JsonArray(array.Select(item => item is null ? null : ForGemini(item)).ToArray());
            default:
                return schema.DeepClone();
        }
    }
}
