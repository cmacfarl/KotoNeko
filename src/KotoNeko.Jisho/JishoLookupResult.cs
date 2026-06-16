using KotoNeko.Core.Domain;

namespace KotoNeko.Jisho;

/// <summary>The outcome of a jisho.org lookup for a single word.</summary>
public class JishoLookupResult
{
    public bool Found { get; init; }

    /// <summary>The kana reading reported by jisho (empty if none / not found).</summary>
    public string Reading { get; init; } = string.Empty;

    /// <summary>The detected verb class (None if not a verb / unknown).</summary>
    public VerbClass VerbClass { get; init; } = VerbClass.None;

    /// <summary>
    /// The first sense's English definitions as separate meanings, suitable for
    /// pre-filling the meanings list. Empty if not found.
    /// </summary>
    public IReadOnlyList<string> Meanings { get; init; } = Array.Empty<string>();

    /// <summary>Set when the lookup failed (network/parse error). Null on success.</summary>
    public string? Error { get; init; }

    public static JishoLookupResult NotFound { get; } = new() { Found = false };

    public static JishoLookupResult Failure(string error) => new() { Found = false, Error = error };
}
