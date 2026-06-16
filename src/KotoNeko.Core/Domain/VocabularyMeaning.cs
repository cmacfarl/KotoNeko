namespace KotoNeko.Core.Domain;

/// <summary>
/// A single accepted English meaning for a vocabulary item. A vocabulary item has
/// one or more of these; during a review, typing <em>any one</em> of them is
/// correct.
/// </summary>
public class VocabularyMeaning
{
    public int Id { get; set; }

    public int VocabularyId { get; set; }
    public Vocabulary? Vocabulary { get; set; }

    /// <summary>The English meaning text, e.g. "newspaper".</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Display/ordering position. The lowest-ordered meaning is treated as the
    /// primary one for list and dashboard display.
    /// </summary>
    public int SortOrder { get; set; }
}
