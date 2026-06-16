namespace KotoNeko.Core.Domain;

/// <summary>
/// The SRS scheduling state for a single vocabulary item. Created when the item
/// is unlocked via a lesson.
/// </summary>
public class SrsItem
{
    public int Id { get; set; }

    public int VocabularyId { get; set; }
    public Vocabulary? Vocabulary { get; set; }

    public SrsStage Stage { get; set; } = SrsStage.Locked;

    /// <summary>
    /// When this item is next due for review. Null while locked or burned.
    /// </summary>
    public DateTime? NextReviewAt { get; set; }

    /// <summary>When the item was first unlocked (lesson completed).</summary>
    public DateTime? UnlockedAt { get; set; }

    /// <summary>When the item reached the Burned stage. Null otherwise.</summary>
    public DateTime? BurnedAt { get; set; }

    /// <summary>Total times the item was answered correctly across reviews.</summary>
    public int CorrectCount { get; set; }

    /// <summary>Total times the item was answered incorrectly across reviews.</summary>
    public int IncorrectCount { get; set; }

    /// <summary>True once the item has been unlocked (is in the SRS system).</summary>
    public bool IsUnlocked => Stage != SrsStage.Locked;

    /// <summary>True when the item is due now (unlocked, not burned, due time passed).</summary>
    public bool IsDue(DateTime now) =>
        Stage != SrsStage.Locked
        && Stage != SrsStage.Burned
        && NextReviewAt is not null
        && NextReviewAt.Value <= now;
}
