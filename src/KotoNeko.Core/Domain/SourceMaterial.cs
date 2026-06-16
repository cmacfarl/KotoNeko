namespace KotoNeko.Core.Domain;

/// <summary>
/// A source that vocabulary was mined from, e.g. "Tobira 2" or "Yotsuba-to".
/// Populates the source dropdown on the vocabulary admin page.
/// </summary>
public class SourceMaterial
{
    public int Id { get; set; }

    /// <summary>Display name, e.g. "Tobira 2".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Loose category, used only for grouping/display.</summary>
    public SourceKind Kind { get; set; } = SourceKind.Other;

    /// <summary>Optional free-text note about the source.</summary>
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Vocabulary items mined from this source.</summary>
    public List<Vocabulary> Vocabulary { get; set; } = new();
}
