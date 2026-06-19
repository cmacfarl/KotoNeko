
using System.Text;
using System.Text.Json;

namespace KotoNeko.Web.Services;

public sealed class TtsResult
{
    public byte[] Audio { get; init; } = Array.Empty<byte>();
    public string VoiceUsed { get; init; } = string.Empty;
}

/// <summary>
/// Generates Japanese TTS for a sentence and a vocabulary word contained
/// within that sentence. A single Google Cloud TTS call is made so that
/// the vocab word audio has the correct in-context pitch accent.
///
/// Requires:
///   Google Cloud TTS REST API key with texttospeech.googleapis.com enabled.
/// </summary>
public sealed class JapaneseTtsService : IDisposable
{
    // ── Japanese voices to cycle through ─────────────────────────────────
    // Journey voices are the most natural; Neural2 provide solid fallbacks.
    // Add or remove entries to suit your preference.
    private static readonly string[] DefaultVoices = new[]
    {
        "ja-JP-Chirp3-HD-Achernar", 
        "ja-JP-Chirp3-HD-Pulcherrima",
        "ja-JP-Chirp3-HD-Kore",
        "ja-JP-Chirp3-HD-Leda",
        "ja-JP-Chirp3-HD-Umbriel",
        "ja-JP-Chirp3-HD-Zephyr",
    };

    private const string TtsEndpoint =
        "https://texttospeech.googleapis.com/v1/text:synthesize";

    private readonly HttpClient  _http;
    private readonly string      _apiKey;
    private readonly string[]    _voices;
    private          int         _voiceIndex;
    private          bool        _disposed;

    /// <param name="apiKey">Google Cloud API key.</param>
    /// <param name="voices">
    /// Optional ordered list of voice names to cycle through.
    /// Defaults to <see cref="DefaultVoices"/> when null.
    /// </param>
    /// <param name="httpClient">
    /// Optional externally-managed HttpClient (e.g. from IHttpClientFactory).
    /// When null an internal instance is created and disposed with the service.
    /// </param>
    public JapaneseTtsService(string apiKey, string[]? voices = null, HttpClient? httpClient = null) {
        _apiKey  = apiKey ?? string.Empty;
        _voices  = (voices is { Length: > 0 }) ? voices : DefaultVoices;
        _http    = httpClient ?? new HttpClient();
    }

    public async Task<TtsResult> SynthesiseAsync(string text, bool advanceVoice = true) {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException("Google TTS API key is not configured. Set GoogleTtsApiKey in appsettings.json.");

        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentNullException(nameof(text));

        string voice = _voices[_voiceIndex % _voices.Length];
        if (advanceVoice) {
            _voiceIndex++;
        }

        string base64 = await CallGoogleTtsAsync(text, voice).ConfigureAwait(false);
        byte[] textAudio = Base64ToM4a(base64);

        return new TtsResult {
            Audio = textAudio,
            VoiceUsed = voice,
        };
    }

    public string PeekNextVoice() => _voices[_voiceIndex % _voices.Length];

    private async Task<string> CallGoogleTtsAsync(string sentence, string voice)
    {
        var requestBody = new {
            input = new { text = sentence },
            voice = new {
                languageCode = "ja-JP",
                name = voice,
            },
            audioConfig = new {
                audioEncoding = "MP3",
                speakingRate = "0.8",
                
            },
        };

        string json    = JsonSerializer.Serialize(requestBody);
        string url     = $"{TtsEndpoint}?key={Uri.EscapeDataString(_apiKey)}";
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _http.PostAsync(url, content).ConfigureAwait(false);

        string responseBody = await response.Content.ReadAsStringAsync()
                                                        .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) {
            throw new InvalidOperationException($"Google TTS API error {(int)response.StatusCode}: {responseBody}");
        }

        using JsonDocument doc = JsonDocument.Parse(responseBody);
        JsonElement root       = doc.RootElement;

        string audioBase64 = root.GetProperty("audioContent").GetString()
            ?? throw new InvalidOperationException("Missing audioContent in response.");

        return audioBase64;
    }

    private static byte[] Base64ToM4a(string audioBase64) {
        return Convert.FromBase64String(audioBase64);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}