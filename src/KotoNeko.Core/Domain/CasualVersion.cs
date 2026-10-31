namespace KotoNeko.Core.Domain;

/// <summary>Where a casual version is in review.</summary>
public enum CasualVersionStatus
{
    /// <summary>Written by the model; waits for the user's OK.</summary>
    Draft = 0,

    /// <summary>Approved and voiced: dictation may use it.</summary>
    Approved = 1,

    /// <summary>Turned down; kept so the sentence isn't sent for rewriting again.</summary>
    Rejected = 2,
}

/// <summary>
/// A casual-speech version of one of the user's sentences (Phase 6c): the same
/// meaning as friends would say it, with plain forms and contractions (〜てる,
/// 〜ちゃう, 〜なきゃ, 〜とく…), for dictation. Written by an LLM, approved by the
/// user. One per source sentence, whatever its status. If the source's text has
/// changed since (<see cref="Original"/> no longer matches), it is stale and unused.
/// </summary>
public class CasualVersion
{
    public const int MaxTextLength = 300;

    public int Id { get; set; }

    /// <summary>The source, when it's a word's practice sentence.</summary>
    public int? PracticeSentenceId { get; set; }
    public PracticeSentence? PracticeSentence { get; set; }

    /// <summary>The source, when it's a grammar point's example.</summary>
    public int? GrammarExampleId { get; set; }
    public GrammarExample? GrammarExample { get; set; }

    /// <summary>The source's text when the version was written.</summary>
    public string Original { get; set; } = string.Empty;

    /// <summary>The casual version.</summary>
    public string Text { get; set; } = string.Empty;

    public CasualVersionStatus Status { get; set; }

    public bool HasAudio { get; set; }

    public CasualVersionAudio? Audio { get; set; }

    /// <summary>Who wrote it: provider · model · prompt version.</summary>
    public string Writer { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

public class CasualVersionAudio
{
    public int Id { get; set; }

    public int CasualVersionId { get; set; }
    public CasualVersion? CasualVersion { get; set; }

    public byte[] Audio { get; set; } = Array.Empty<byte>();
}
