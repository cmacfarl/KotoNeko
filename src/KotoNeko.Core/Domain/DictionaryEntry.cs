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

/// <summary>Which dictionary is currently imported, shown on the Settings page.</summary>
public class DictionaryImport
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string DictionaryDate { get; set; } = string.Empty;
    public DateTime ImportedAt { get; set; }
    public int EntryCount { get; set; }
}
