namespace KotoNeko.Core.Domain;

/// <summary>
/// Stored readings for one sentence, worked out once from the dictionary and
/// editable. <see cref="Furigana"/> is the bracket format (毎朝[まいあさ]…);
/// <see cref="Words"/> records where each word starts and its dictionary form,
/// which is what decides whether the reader still needs furigana over it.
/// Generated rows are cleared when a new dictionary is imported, and rebuilt;
/// edited rows are kept.
/// </summary>
public class SentenceReading
{
    public const int MaxTextLength = 1000;

    public int Id { get; set; }

    /// <summary>SHA-256 of <see cref="Text"/>, hex: the unique key.</summary>
    public string TextHash { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Furigana { get; set; } = string.Empty;

    /// <summary>JSON array of { "s": start, "l": dictionary form, "p": is particle }.</summary>
    public string Words { get; set; } = "[]";

    /// <summary>The user corrected the readings: never regenerate this row.</summary>
    public bool IsEdited { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>One dictation answer: practice only, never part of the SRS.</summary>
public class DictationAttempt
{
    public long Id { get; set; }

    /// <summary>The study item whose sentence was dictated: a vocabulary word, or a grammar point.</summary>
    public int StudyItemId { get; set; }
    public StudyItem? StudyItem { get; set; }

    /// <summary>The practice sentence dictated; null for a context sentence (or one since deleted).</summary>
    public int? PracticeSentenceId { get; set; }
    public PracticeSentence? PracticeSentence { get; set; }

    /// <summary>The sentence's length in spoken kana; 0 for attempts from before this was recorded.</summary>
    public int KanaLength { get; set; }

    public string Sentence { get; set; } = string.Empty;

    public string Typed { get; set; } = string.Empty;

    /// <summary>Only the target word was dictated, not the whole sentence.</summary>
    public bool Partial { get; set; }

    /// <summary>The grammar example dictated (Phase 6b); null for a word's sentence (or one since deleted).</summary>
    public int? GrammarExampleId { get; set; }
    public GrammarExample? GrammarExample { get; set; }

    /// <summary>Share of the kana heard correctly, 0–1.</summary>
    public double Score { get; set; }

    public DateTime PracticedAt { get; set; }
}
