using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Text;

namespace KotoNeko.Web.Services;

/// <summary>Whether a session is teaching new items or reviewing due ones.</summary>
public enum SessionKind
{
    Review,
    Lesson,
}

/// <summary>A single question within a review card.</summary>
public class ReviewQuestion
{
    public required QuestionType Type { get; init; }

    /// <summary>The label shown to the user, e.g. "Reading", "Meaning", "Past · Negative".</summary>
    public required string Prompt { get; init; }

    /// <summary>Primary accepted answer.</summary>
    public required string ExpectedPrimary { get; init; }

    /// <summary>Additional accepted answers (';'-separated), for reading/meaning.</summary>
    public string? ExpectedAlternates { get; init; }

    public ConjugationForm? Form { get; init; }
    public Polarity? Polarity { get; init; }

    /// <summary>True once answered correctly (and so cleared from the card).</summary>
    public bool Answered { get; set; }

    /// <summary>True if this question was ever answered incorrectly in this session.</summary>
    public bool MissedAtLeastOnce { get; set; }

    public AnswerJudgment Grade(string typed) => Type switch
    {
        QuestionType.Reading    => AnswerMatcher.GradeReading(typed, ExpectedPrimary, ExpectedAlternates),
        QuestionType.Meaning    => AnswerMatcher.GradeMeaning(typed, ExpectedPrimary, ExpectedAlternates),
        QuestionType.Conjugation => AnswerMatcher.GradeConjugation(typed, ExpectedPrimary),
        QuestionType.Production => AnswerMatcher.GradeReading(typed, ExpectedPrimary, ExpectedAlternates),
        _ => AnswerJudgment.Incorrect,
    };
}

/// <summary>
/// One vocabulary item being reviewed, with its set of questions and the live SRS
/// entity that will be mutated when the card is finalised.
/// </summary>
public class ReviewCard
{
    public required Vocabulary Vocabulary { get; init; }

    /// <summary>The tracked SRS entity for this item (mutated on finalise).</summary>
    public required SrsItem Srs { get; init; }

    public required List<ReviewQuestion> Questions { get; init; }

    /// <summary>Total incorrect answers across this card (drives the SRS penalty).</summary>
    public int IncorrectCount { get; set; }

    public bool Finalised { get; set; }

    public bool AllAnswered => Questions.All(q => q.Answered);
}
