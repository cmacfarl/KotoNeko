using System.Text.Json;
using System.Text.Json.Serialization;
using KotoNeko.Core.Ime;

namespace KotoNeko.Core.Domain;

/// <summary>
/// One way of writing one reading of a dictionary word, imported from JMdict and
/// used for offline kana → kanji conversion. A word with two spellings and one
/// reading is two rows.
/// </summary>
public class DictionaryEntry
{
    /// <summary>Readings longer than this are skipped on import: no conversion segment is that long.</summary>
    public const int MaxLength = 64;

    public int Id { get; set; }

    /// <summary>
    /// The word this spelling belongs to: its JMdict entry number (from format 3;
    /// the position in the file before that), and the key of its <see cref="DictionaryMeaning"/>.
    /// </summary>
    public int WordId { get; set; }

    /// <summary>Reading in hiragana (katakana folded), the lookup key.</summary>
    public string Reading { get; set; } = string.Empty;

    /// <summary>How the word is written: kanji, kana, or a mix.</summary>
    public string Written { get; set; } = string.Empty;

    public WordType Type { get; set; }

    /// <summary>Lower is better: common forms first, rare or outdated forms last.</summary>
    public int Rank { get; set; }

    /// <summary>Stable tie-break: the word's position in the source dictionary.</summary>
    public int Order { get; set; }

    /// <summary>The dictionary marks this word as usually written in kana.</summary>
    public bool UsuallyKana { get; set; }

    /// <summary>The word is only a particle, auxiliary, copula or conjunction.</summary>
    public bool ParticleLike { get; set; }
}

/// <summary>A dictionary word's senses (meanings), stored once for all its spellings.</summary>
public class DictionaryMeaning
{
    /// <summary>The word id: its JMdict entry number (see <see cref="DictionaryEntry.WordId"/>).</summary>
    public int Id { get; set; }

    /// <summary>The senses as JSON (see <see cref="DictionarySenses"/>).</summary>
    public string Senses { get; set; } = "[]";
}

/// <summary>
/// One sense of a dictionary word: its English glosses and parts of speech. A
/// sense can be limited to some spellings or readings (null means all of them).
/// </summary>
public sealed record DictionarySense(
    [property: JsonPropertyName("g")] IReadOnlyList<string> Glosses,
    [property: JsonPropertyName("p")] IReadOnlyList<string> PartsOfSpeech,
    [property: JsonPropertyName("k"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? OnlySpellings = null,
    [property: JsonPropertyName("r"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? OnlyReadings = null)
{
    /// <summary>Whether this sense applies to the given spelling and reading (kana as written).</summary>
    public bool AppliesTo(string written, string reading) =>
        (OnlySpellings is null || OnlySpellings.Contains(written))
        && (OnlyReadings is null || OnlyReadings.Any(r => Text.Kana.ToHiragana(r) == Text.Kana.ToHiragana(reading)));
}

/// <summary>Reading and writing the stored senses.</summary>
public static class DictionarySenses
{
    private static readonly JsonSerializerOptions Options = new() {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(IReadOnlyList<DictionarySense> senses) => JsonSerializer.Serialize(senses, Options);

    public static List<DictionarySense> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<DictionarySense>>(json, Options) ?? new List<DictionarySense>();
}

/// <summary>Which dictionary is currently imported, shown on the Settings page.</summary>
public class DictionaryImport
{
    /// <summary>
    /// What an import contains. 1: spellings and readings. 2: also each word's
    /// meanings. 3: words keyed by their JMdict entry number. An older import
    /// still works, but features needing the newer data ask for a re-import.
    /// </summary>
    public const int CurrentFormatVersion = 3;

    /// <summary>The first format with meanings (filling in words from the dictionary).</summary>
    public const int MeaningsFormatVersion = 2;

    /// <summary>The first format keyed by JMdict entry number (seeding the JLPT lists).</summary>
    public const int EntryNumbersFormatVersion = 3;

    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DictionaryDate { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public int EntryCount { get; set; }
    public int FormatVersion { get; set; } = 1;
}
