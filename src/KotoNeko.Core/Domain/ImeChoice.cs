namespace KotoNeko.Core.Domain;

/// <summary>
/// A spelling the user picked for a reading in the Japanese input, remembered so
/// it comes first next time. Stored against the dictionary form (読む, not 読んだ),
/// so every inflection benefits.
/// </summary>
public class ImeChoice
{
    public int Id { get; set; }

    /// <summary>Reading in hiragana.</summary>
    public string Reading { get; set; } = string.Empty;

    public string Written { get; set; } = string.Empty;

    /// <summary>How many times this spelling was picked.</summary>
    public int Count { get; set; }

    public DateTime LastChosenAt { get; set; }
}
