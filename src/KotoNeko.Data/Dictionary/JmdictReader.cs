using System.Text.Json;

namespace KotoNeko.Data.Dictionary;

/// <summary>
/// Streams a jmdict-simplified JSON file one word at a time, so the 100+ MB file
/// is never held in memory whole. Reads the header fields it needs (version,
/// dictDate), skips the rest, and hands each element of "words" to a callback.
/// </summary>
public sealed class JmdictReader
{
    private const int DefaultBufferSize = 1 << 20;

    private readonly int _initialBufferSize;
    private string? _property;   // root property whose value is next
    private bool _inWords;       // inside the "words" array
    private bool _done;          // past the end of the "words" array

    /// <param name="initialBufferSize">Starting buffer size; it grows when one word does not fit.</param>
    public JmdictReader(int initialBufferSize = DefaultBufferSize)
    {
        _initialBufferSize = Math.Max(16, initialBufferSize);
    }

    public string Version { get; private set; } = string.Empty;
    public string DictDate { get; private set; } = string.Empty;

    /// <summary>Read the whole stream, calling <paramref name="onWord"/> for each word.</summary>
    public async Task ReadAsync(Stream stream, Action<JmdictWord> onWord, CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[_initialBufferSize];
        int length = 0;
        bool final = false;
        JsonReaderState state = new(new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip });

        while (!_done)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!final)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken);
                length += read;
                final = read == 0;
            }

            int consumed;
            try
            {
                consumed = Process(buffer.AsSpan(0, length), final, ref state, onWord);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"The dictionary file is not valid JSON: {ex.Message}", ex);
            }
            if (_done)
            {
                break;
            }
            if (final && consumed == 0)
            {
                throw new InvalidDataException("The dictionary file ended before its \"words\" array was complete.");
            }

            // Keep what was not consumed; grow the buffer if one value does not fit in it.
            Buffer.BlockCopy(buffer, consumed, buffer, 0, length - consumed);
            length -= consumed;
            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
        }
    }

    /// <summary>
    /// Consume as many complete tokens or words as the data allows. Returns the
    /// bytes consumed; <paramref name="state"/> is left where reading must resume.
    /// </summary>
    private int Process(ReadOnlySpan<byte> data, bool final, ref JsonReaderState state, Action<JmdictWord> onWord)
    {
        Utf8JsonReader reader = new(data, final, state);
        int consumed = 0;

        while (true)
        {
            JsonReaderState before = reader.CurrentState;
            int beforeConsumed = (int)reader.BytesConsumed;
            if (!reader.Read())
            {
                break;
            }

            if (_inWords)
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    _inWords = false;
                    _done = true;
                    consumed = (int)reader.BytesConsumed;
                    state = reader.CurrentState;
                    return consumed;
                }

                // A word: only deserialize it once all of it is in the buffer.
                Utf8JsonReader probe = reader;
                if (!probe.TrySkip())
                {
                    state = before;
                    return beforeConsumed;
                }
                onWord(JsonSerializer.Deserialize<JmdictWord>(ref reader)!);
            }
            else if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
            {
                _property = reader.GetString();
            }
            else if (_property is not null && reader.CurrentDepth == 1)
            {
                string property = _property;
                if (property == "words" && reader.TokenType == JsonTokenType.StartArray)
                {
                    _inWords = true;
                }
                else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    // A header value we don't need (tags, languages, ...): skip it
                    // whole, or wait for more data if it isn't all in the buffer.
                    if (!reader.TrySkip())
                    {
                        state = before;
                        return beforeConsumed;
                    }
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    if (property == "version") Version = reader.GetString() ?? string.Empty;
                    if (property == "dictDate") DictDate = reader.GetString() ?? string.Empty;
                }
                _property = null;
            }

            consumed = (int)reader.BytesConsumed;
            state = reader.CurrentState;
        }

        return consumed;
    }
}
