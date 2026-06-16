namespace KotoNeko.Core.Domain;

/// <summary>
/// A record of a single answered question during a review. Powers dashboard
/// statistics and the "recently missed" list. Only committed once an answer is
/// finalised (so undone answers leave no trace).
/// </summary>
public class ReviewLog
{
    public long Id { get; set; }

    public int VocabularyId { get; set; }
    public Vocabulary? Vocabulary { get; set; }

    public DateTime ReviewedAt { get; set; }

    public QuestionType QuestionType { get; set; }

    /// <summary>For conjugation questions, which form was asked. Null otherwise.</summary>
    public ConjugationForm? ConjugationForm { get; set; }

    /// <summary>For conjugation questions, the polarity asked. Null otherwise.</summary>
    public Polarity? ConjugationPolarity { get; set; }

    /// <summary>Whether the answer was ultimately correct.</summary>
    public bool WasCorrect { get; set; }

    /// <summary>The SRS stage the item was at when this question was answered.</summary>
    public SrsStage StageAtReview { get; set; }
}
