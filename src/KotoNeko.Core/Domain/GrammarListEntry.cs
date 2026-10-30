namespace KotoNeko.Core.Domain;

/// <summary>Where the user is with a grammar point.</summary>
public enum GrammarStatus
{
    NotYet = 0,
    Studying = 1,
    Known = 2,
}

/// <summary>
/// One grammar point on the user's list: its pattern, a short meaning, its JLPT
/// level and whether the user knows it. Sentence generation keeps to the Known
/// points. This is the list only; a grammar point as an SRS study item
/// (<see cref="GrammarPoint"/>) links to its entry by id.
/// </summary>
public class GrammarListEntry
{
    public const int MaxKeyLength = 64;
    public const int MaxPatternLength = 100;
    public const int MaxMeaningLength = 300;

    public int Id { get; set; }

    /// <summary>The entry's key in the drafted list (n4-noni); null for points the user added.</summary>
    public string? SeedKey { get; set; }

    /// <summary>The pattern as usually written, e.g. 〜のに or に (time).</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>A short English meaning.</summary>
    public string Meaning { get; set; } = string.Empty;

    public JlptLevel Level { get; set; }

    public GrammarStatus Status { get; set; }

    /// <summary>Pruned from the list: hidden, and never added back by seeding.</summary>
    public bool IsPruned { get; set; }

    /// <summary>Position within its level: the drafted list's study order.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}
