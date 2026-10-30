namespace KotoNeko.Core.Domain;

/// <summary>What one graded sentence did to a grammar point's SRS state.</summary>
public enum GrammarReviewEffect
{
    /// <summary>A rewrite after the review was decided: practice only.</summary>
    Practice = 0,

    /// <summary>Passed: the point moved up a stage.</summary>
    Advanced = 1,

    /// <summary>A miss the grader was sure of: the point dropped.</summary>
    Dropped = 2,

    /// <summary>
    /// A miss the grader wasn't sure of: until a native speaker has checked the grader,
    /// it doesn't cost a stage, so the point is reviewed again at the same stage.
    /// </summary>
    Kept = 3,
}

/// <summary>
/// One graded sentence from a grammar review, kept whatever its verdict: Dispute, the
/// mistake record and the repeat check (Phase 5) all read these.
/// </summary>
public class GrammarReviewAttempt
{
    public const int MaxSentenceLength = 300;
    public const int MaxIntentLength = 500;

    public long Id { get; set; }

    public int GrammarPointId { get; set; }
    public GrammarPoint? GrammarPoint { get; set; }

    public DateTime AttemptedAt { get; set; }

    public string Sentence { get; set; } = string.Empty;

    /// <summary>What the learner said they meant, in English; empty when they didn't say.</summary>
    public string Intent { get; set; } = string.Empty;

    public SrsStage StageBefore { get; set; }
    public SrsStage StageAfter { get; set; }

    /// <summary>What the verdict did: decided the review, or practice after it.</summary>
    public GrammarReviewEffect Effect { get; set; }

    /// <summary>The verdict's SRS signal: whether the target was used correctly.</summary>
    public bool TargetUsedCorrectly { get; set; }

    public bool Natural { get; set; }

    /// <summary>The grader's confidence: low, medium or high.</summary>
    public string Confidence { get; set; } = string.Empty;

    /// <summary>The grader's answer as it came back.</summary>
    public string VerdictJson { get; set; } = string.Empty;

    public string PromptVersion { get; set; } = string.Empty;

    /// <summary>
    /// For a Master review's second sentence: the similar point it was written with
    /// (one of the point's contrasts). Null for a sentence with the point itself.
    /// </summary>
    public string? ContrastPattern { get; set; }

    /// <summary>A second grader's verdict, asked because the first was an unsure miss; null when not asked.</summary>
    public string? SecondVerdictJson { get; set; }

    /// <summary>Who gave the second opinion (provider · model).</summary>
    public string? SecondModel { get; set; }

    /// <summary>Whether the learner disputed the verdict (a fresh second opinion, with their comment).</summary>
    public bool Disputed { get; set; }

    /// <summary>What the learner said when disputing; empty when they said nothing.</summary>
    public string DisputeComment { get; set; } = string.Empty;

    public string? DisputeVerdictJson { get; set; }
    public string? DisputeModel { get; set; }

    /// <summary>
    /// The review's miss was overturned by a dispute and counted as a pass. <see cref="Effect"/>
    /// and <see cref="StageAfter"/> keep what the first verdict did.
    /// </summary>
    public bool Overturned { get; set; }

    public const int MaxModelLength = 200;
    public const int MaxContrastLength = 100;
}
