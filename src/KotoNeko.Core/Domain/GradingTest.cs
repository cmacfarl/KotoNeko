namespace KotoNeko.Core.Domain;

/// <summary>A person's verdict on a test sentence: the answer key the grader is measured against.</summary>
public enum GradingLabel
{
    /// <summary>The grammar point is used correctly.</summary>
    Correct = 0,

    /// <summary>Looks plausible, but misuses the grammar point.</summary>
    SubtlyWrong = 1,

    /// <summary>Broken form or meaning.</summary>
    ClearlyWrong = 2,

    /// <summary>Grammatical, but not how a native speaker would say it. Passes, with a note.</summary>
    CorrectButUnnatural = 3,
}

/// <summary>Who decided an item's label.</summary>
public enum LabelSource
{
    /// <summary>A person (you, or a tutor) chose it.</summary>
    Person = 0,

    /// <summary>Every pre-labelling model (and how it was drafted) agreed; no person has checked it.</summary>
    AiAgreed = 1,
}

public static class GradingLabels
{
    /// <summary>Whether the label means the grammar point was used correctly: the SRS signal.</summary>
    public static bool TargetCorrect(this GradingLabel label) =>
        label is GradingLabel.Correct or GradingLabel.CorrectButUnnatural;

    public static string Display(this GradingLabel label) => label switch {
        GradingLabel.Correct => "Correct",
        GradingLabel.SubtlyWrong => "Subtly wrong",
        GradingLabel.ClearlyWrong => "Clearly wrong",
        GradingLabel.CorrectButUnnatural => "Correct but unnatural",
        _ => label.ToString(),
    };
}

/// <summary>
/// One sentence in the grading test set: a learner's attempt at a grammar point,
/// with the situation it answers, and the label a person gave it. The grader is
/// scored against these labels before it's trusted with the SRS. Labels must come
/// from a person: labels an LLM wrote would make the test measure nothing.
/// </summary>
public class GradingTestItem
{
    public const int MaxSentenceLength = 300;
    public const int MaxTaskLength = 300;
    public const int MaxNoteLength = 500;

    public int Id { get; set; }

    /// <summary>The drafted candidate's key (noni-03); null for sentences the user added.</summary>
    public string? SeedKey { get; set; }

    /// <summary>The grammar point's key in the grammar list (n4-noni).</summary>
    public string GrammarKey { get; set; } = string.Empty;

    /// <summary>The grammar point as shown to the grader, copied when the item was made.</summary>
    public string Pattern { get; set; } = string.Empty;

    public string Meaning { get; set; } = string.Empty;

    /// <summary>The situation the sentence answers, in English ("Say you took medicine but…").</summary>
    public string Task { get; set; } = string.Empty;

    public string Sentence { get; set; } = string.Empty;

    /// <summary>Null until labelled.</summary>
    public GradingLabel? Label { get; set; }

    /// <summary>Who decided <see cref="Label"/>. A person's label is never overwritten by pre-labelling.</summary>
    public LabelSource LabelSource { get; set; }

    /// <summary>The pre-labelling votes disagree: a person should decide (unset once a person labels it).</summary>
    public bool NeedsPerson { get; set; }

    /// <summary>The labeller's note: why, or what a tutor said.</summary>
    public string? Note { get; set; }

    /// <summary>Not a useful test item (ambiguous, or the task doesn't fit): left out of scoring.</summary>
    public bool Excluded { get; set; }

    /// <summary>Position in the labelling queue (shuffled, so intended labels aren't grouped).</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LabelledAt { get; set; }
}

/// <summary>
/// One model's vote on a test item when pre-labelling. Votes taken from how the
/// candidates were drafted are recorded too, attributed to the model that drafted them.
/// </summary>
public class GradingPrelabel
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>The effort or thinking level used, when one was set.</summary>
    public string? Effort { get; set; }

    public string PromptVersion { get; set; } = string.Empty;
    public GradingLabel Label { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>One run of the grading eval: a provider and model graded every labelled item.</summary>
public class GradingEvalRun
{
    public int Id { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;

    /// <summary>The effort or thinking level the grader ran at; null for the provider's default.</summary>
    public string? Effort { get; set; }

    public string PromptVersion { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    /// <summary>Labelled, non-excluded items in the run.</summary>
    public int Items { get; set; }

    /// <summary>Person-labelled items with a verdict: what the headline rates are based on.</summary>
    public int Graded { get; set; }

    /// <summary>AI-agreed items the grader didn't vote on, with a verdict.</summary>
    public int AiAgreedGraded { get; set; }

    /// <summary>
    /// Agreement on those AI-agreed items: consistency on the easier items (the hard
    /// ones went to a person), not accuracy.
    /// </summary>
    public double AiAgreedAgreement { get; set; }

    /// <summary>
    /// Share of graded items where the verdict on the target matched the label: on
    /// person-labelled items only (the headline), since the grader may have voted on
    /// the AI-agreed ones.
    /// </summary>
    public double Agreement { get; set; }

    /// <summary>Share of items labelled correct (or unnatural) that the grader failed.</summary>
    public double FalseCorrectionRate { get; set; }

    /// <summary>Share of items labelled wrong that the grader passed.</summary>
    public double FalsePassRate { get; set; }

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    public List<GradingEvalResult> Results { get; set; } = new();
}

/// <summary>The grader's verdict on one item in a run.</summary>
public class GradingEvalResult
{
    public long Id { get; set; }

    public int RunId { get; set; }
    public GradingEvalRun? Run { get; set; }

    public int ItemId { get; set; }

    /// <summary>The item's label when the run was made (labels can change later).</summary>
    public GradingLabel Label { get; set; }

    /// <summary>Who decided that label.</summary>
    public LabelSource LabelSource { get; set; }

    /// <summary>The grader's own model voted on this item when pre-labelling: it's left out of its scores.</summary>
    public bool GraderVoted { get; set; }

    /// <summary>The grader's call on the target; null when the call failed.</summary>
    public bool? TargetUsedCorrectly { get; set; }

    public bool? Natural { get; set; }

    /// <summary>low, medium or high.</summary>
    public string? Confidence { get; set; }

    /// <summary>The raw verdict, as the grader returned it.</summary>
    public string? VerdictJson { get; set; }

    public string? Error { get; set; }
}
