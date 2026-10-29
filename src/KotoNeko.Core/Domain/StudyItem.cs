namespace KotoNeko.Core.Domain;

/// <summary>
/// Anything the user learns and is quizzed on through the SRS. The SRS state,
/// review history, and lesson/sleep bookkeeping live here so they behave the same
/// for every kind of item; each kind derives from this and adds its own content.
/// Mapped table-per-type: a derived row shares its id with its StudyItems row.
/// </summary>
public abstract class StudyItem
{
    protected StudyItem(StudyItemKind kind)
    {
        Kind = kind;
    }

    public int Id { get; set; }

    /// <summary>Which derived type this is; stored so queries can filter without joins.</summary>
    public StudyItemKind Kind { get; private set; }

    /// <summary>
    /// When true the item is "asleep": excluded from lessons and reviews entirely.
    /// </summary>
    public bool IsAsleep { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>The SRS scheduling state for this item. One-to-one; null until its lesson.</summary>
    public SrsItem? Srs { get; set; }

    public List<ReviewLog> ReviewLogs { get; set; } = new();
}
