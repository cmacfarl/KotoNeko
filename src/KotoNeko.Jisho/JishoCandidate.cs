using KotoNeko.Core.Domain;

namespace KotoNeko.Jisho;

/// <summary>
/// One match returned by <see cref="JishoClient.SearchAsync"/>. Used to let the
/// user pick the written form (kanji) for a word they typed in kana.
/// </summary>
public class JishoCandidate
{
    /// <summary>The written form (may include kanji). Empty for kana-only words.</summary>
    public string Word { get; init; } = string.Empty;

    /// <summary>The kana reading.</summary>
    public string Reading { get; init; } = string.Empty;

    /// <summary>The first sense's English definitions.</summary>
    public IReadOnlyList<string> Meanings { get; init; } = Array.Empty<string>();

    /// <summary>The detected verb class (None if not a verb / unknown).</summary>
    public VerbClass VerbClass { get; init; } = VerbClass.None;

    /// <summary>What to put in the "Japanese writing" field when this is chosen.</summary>
    public string DisplayWriting => string.IsNullOrEmpty(Word) ? Reading : Word;
}
