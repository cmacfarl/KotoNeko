using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Text;

namespace KotoNeko.Core.Review;

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

    /// <summary>Set only when the conjugation has audio; used to play the correct clip in reviews.</summary>
    public int? ConjugationId { get; init; }

    /// <summary>
    /// For listening questions: other words that sound the same. A meaning of any
    /// of them is accepted, since it is what the user actually heard. Empty when
    /// the question plays a <see cref="Sentence"/>: the context decides the meaning.
    /// </summary>
    public IReadOnlyList<Homophone> Homophones { get; init; } = Array.Empty<Homophone>();

    /// <summary>For listening questions: the practice sentence played after the word, if any.</summary>
    public ListeningSentence? Sentence { get; init; }

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
        QuestionType.Listening  => GradeListening(typed),
        _ => AnswerJudgment.Incorrect,
    };

    /// <summary>
    /// For a correct listening answer, the homophone whose meaning was typed, or
    /// null when the answer matched this item's own meanings.
    /// </summary>
    public Homophone? MatchedHomophone(string typed)
    {
        if (Type != QuestionType.Listening
            || AnswerMatcher.GradeMeaning(typed, ExpectedPrimary, ExpectedAlternates) == AnswerJudgment.Correct)
        {
            return null;
        }

        return Homophones.FirstOrDefault(h =>
            AnswerMatcher.GradeMeaning(typed, h.PrimaryMeaning, h.AlternateMeanings) == AnswerJudgment.Correct);
    }

    private AnswerJudgment GradeListening(string typed)
    {
        AnswerJudgment own = AnswerMatcher.GradeMeaning(typed, ExpectedPrimary, ExpectedAlternates);
        if (own == AnswerJudgment.Correct) {
            return own;
        }

        foreach (Homophone h in Homophones) {
            if (AnswerMatcher.GradeMeaning(typed, h.PrimaryMeaning, h.AlternateMeanings) == AnswerJudgment.Correct) {
                return AnswerJudgment.Correct;
            }
        }

        return own;
    }
}

/// <summary>Another word with the same reading, accepted in a listening question.</summary>
/// <summary>A practice sentence a listening question plays after the word.</summary>
/// <param name="Pieces">The sentence with furigana where the reader still needs it, shown after answering.</param>
public sealed record ListeningSentence(int PracticeSentenceId, IReadOnlyList<FuriganaPiece> Pieces, string English)
{
    public string AudioUrl => $"/api/practice-sentence/{PracticeSentenceId}/audio";
}

public sealed record Homophone(string Japanese, string Reading, string PrimaryMeaning, string? AlternateMeanings);

/// <summary>
/// One study item being reviewed, with its set of questions and the live SRS
/// entity that will be mutated when the card is finalised.
/// </summary>
public class ReviewCard
{
    public required StudyItem Item { get; init; }

    /// <summary>The tracked SRS entity for this item (mutated on finalise).</summary>
    public required SrsItem Srs { get; init; }

    public required List<ReviewQuestion> Questions { get; init; }

    /// <summary>
    /// The item's context sentence with furigana only where the reader still needs
    /// it, when readings for it are stored; null to show the sentence as plain text.
    /// </summary>
    public IReadOnlyList<FuriganaPiece>? ContextSentence { get; init; }

    /// <summary>Total incorrect answers across this card (drives the SRS penalty).</summary>
    public int IncorrectCount { get; set; }

    public bool Finalised { get; set; }

    public bool AllAnswered => Questions.All(q => q.Answered);
}
