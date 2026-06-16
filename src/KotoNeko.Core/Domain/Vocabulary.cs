namespace KotoNeko.Core.Domain;

/// <summary>
/// A single vocabulary item. The unit a user learns and is quizzed on.
/// </summary>
public class Vocabulary
{
    public int Id { get; set; }

    /// <summary>The Japanese writing of the word (may contain kanji), e.g. 新聞.</summary>
    public string Japanese { get; set; } = string.Empty;

    /// <summary>
    /// The kana reading of the word, e.g. しんぶん. Redundant for kana-only words
    /// (where it equals <see cref="Japanese"/>), but always populated so reading
    /// reviews have an answer.
    /// </summary>
    public string Reading { get; set; } = string.Empty;

    /// <summary>
    /// All accepted English meanings. Typing any one of them during a review counts
    /// as correct. The lowest <see cref="VocabularyMeaning.SortOrder"/> is the
    /// primary meaning used for display.
    /// </summary>
    public List<VocabularyMeaning> Meanings { get; set; } = new();

    /// <summary>
    /// Additional accepted readings (kana), separated by ';'. Used so alternate
    /// readings grade as correct. May be empty.
    /// </summary>
    public string AlternateReadings { get; set; } = string.Empty;

    /// <summary>Optional example sentence in Japanese.</summary>
    public string? ContextSentence { get; set; }

    /// <summary>Optional personal mnemonic / memo.</summary>
    public string? Memo { get; set; }

    /// <summary>The verb class. <see cref="VerbClass.None"/> means it is not a verb.</summary>
    public VerbClass VerbClass { get; set; } = VerbClass.None;

    /// <summary>Convenience flag: true when this item conjugates.</summary>
    public bool IsVerb => VerbClass != VerbClass.None;

    /// <summary>
    /// When true the item is "asleep": excluded from lessons and reviews entirely.
    /// </summary>
    public bool IsAsleep { get; set; }

    public int? SourceMaterialId { get; set; }
    public SourceMaterial? SourceMaterial { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The SRS scheduling state for this item. One-to-one.</summary>
    public SrsItem? Srs { get; set; }

    /// <summary>The 14 stored conjugations (only populated for verbs).</summary>
    public List<Conjugation> Conjugations { get; set; } = new();

    public List<ReviewLog> ReviewLogs { get; set; } = new();

    /// <summary>The primary meaning (lowest sort order), or empty if none set.</summary>
    public string PrimaryMeaning =>
        Meanings.OrderBy(m => m.SortOrder).FirstOrDefault()?.Text ?? string.Empty;

    /// <summary>All meanings joined for display, e.g. "to see, to look, to watch".</summary>
    public string MeaningsDisplay =>
        string.Join(", ", Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text));
}
