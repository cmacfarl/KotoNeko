using System.Text.Json;
using KotoNeko.Core.Domain;

namespace KotoNeko.Jisho;

/// <summary>
/// Thin client over jisho.org's public search API. We use it only to detect a
/// word's reading and verb class (and optionally its meaning); the conjugation
/// table itself is computed locally by <c>ConjugationEngine</c>, since jisho does
/// not serve a conjugation table as data.
/// </summary>
public class JishoClient
{
    private const string SearchUrl = "https://jisho.org/api/v1/search/words?keyword=";

    private readonly HttpClient _http;

    public JishoClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<JishoLookupResult> LookupAsync(string word, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return JishoLookupResult.NotFound;
        }

        try
        {
            string url = SearchUrl + Uri.EscapeDataString(word.Trim());
            using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return JishoLookupResult.Failure($"jisho returned HTTP {(int)response.StatusCode}.");
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            return Parse(doc, word.Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return JishoLookupResult.Failure(ex.Message);
        }
    }

    private static JishoLookupResult Parse(JsonDocument doc, string word)
    {
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) || data.GetArrayLength() == 0)
        {
            return JishoLookupResult.NotFound;
        }

        // Prefer the entry whose word/reading matches the query exactly; fall back
        // to the first result.
        JsonElement chosen = data[0];
        foreach (JsonElement entry in data.EnumerateArray())
        {
            if (MatchesWord(entry, word))
            {
                chosen = entry;
                break;
            }
        }

        string reading = ExtractReading(chosen, word);
        VerbClass verbClass = ExtractVerbClass(chosen);
        IReadOnlyList<string> meanings = ExtractMeanings(chosen);

        return new JishoLookupResult
        {
            Found = true,
            Reading = reading,
            VerbClass = verbClass,
            Meanings = meanings,
        };
    }

    private static bool MatchesWord(JsonElement entry, string word)
    {
        if (!entry.TryGetProperty("japanese", out JsonElement japanese))
        {
            return false;
        }

        foreach (JsonElement j in japanese.EnumerateArray())
        {
            if (j.TryGetProperty("word", out JsonElement w) && w.ValueKind == JsonValueKind.String && w.GetString() == word)
            {
                return true;
            }

            if (j.TryGetProperty("reading", out JsonElement r) && r.ValueKind == JsonValueKind.String && r.GetString() == word)
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractReading(JsonElement entry, string word)
    {
        if (!entry.TryGetProperty("japanese", out JsonElement japanese))
        {
            return string.Empty;
        }

        string firstReading = string.Empty;
        foreach (JsonElement j in japanese.EnumerateArray())
        {
            if (!j.TryGetProperty("reading", out JsonElement r) || r.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            string reading = r.GetString() ?? string.Empty;
            if (reading.Length == 0)
            {
                continue;
            }

            if (firstReading.Length == 0)
            {
                firstReading = reading;
            }

            // Prefer the reading from the block whose word matches the query.
            bool wordMatches = j.TryGetProperty("word", out JsonElement w)
                && w.ValueKind == JsonValueKind.String && w.GetString() == word;
            if (wordMatches)
            {
                return reading;
            }
        }

        return firstReading;
    }

    private static IReadOnlyList<string> ExtractMeanings(JsonElement entry)
    {
        if (!entry.TryGetProperty("senses", out JsonElement senses) || senses.GetArrayLength() == 0)
        {
            return Array.Empty<string>();
        }

        JsonElement firstSense = senses[0];
        if (!firstSense.TryGetProperty("english_definitions", out JsonElement defs))
        {
            return Array.Empty<string>();
        }

        List<string> parts = new();
        foreach (JsonElement d in defs.EnumerateArray())
        {
            if (d.ValueKind == JsonValueKind.String)
            {
                string? value = d.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    parts.Add(value.Trim());
                }
            }
        }

        return parts;
    }

    private static VerbClass ExtractVerbClass(JsonElement entry)
    {
        if (!entry.TryGetProperty("senses", out JsonElement senses))
        {
            return VerbClass.None;
        }

        foreach (JsonElement sense in senses.EnumerateArray())
        {
            if (!sense.TryGetProperty("parts_of_speech", out JsonElement pos))
            {
                continue;
            }

            foreach (JsonElement p in pos.EnumerateArray())
            {
                if (p.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                VerbClass detected = ClassifyPartOfSpeech(p.GetString() ?? string.Empty);
                if (detected != VerbClass.None)
                {
                    return detected;
                }
            }
        }

        return VerbClass.None;
    }

    /// <summary>Map a jisho part-of-speech string to a <see cref="VerbClass"/>.</summary>
    private static VerbClass ClassifyPartOfSpeech(string pos)
    {
        // Order matters: check Kuru/Suru special classes before the generic checks.
        if (pos.Contains("Kuru verb", StringComparison.OrdinalIgnoreCase))
        {
            return VerbClass.Kuru;
        }

        if (pos.Contains("Suru verb", StringComparison.OrdinalIgnoreCase)
            || pos.Contains("aux. verb suru", StringComparison.OrdinalIgnoreCase))
        {
            return VerbClass.Suru;
        }

        if (pos.Contains("Ichidan verb", StringComparison.OrdinalIgnoreCase))
        {
            return VerbClass.Ichidan;
        }

        if (pos.Contains("Godan verb", StringComparison.OrdinalIgnoreCase))
        {
            return VerbClass.Godan;
        }

        return VerbClass.None;
    }
}
