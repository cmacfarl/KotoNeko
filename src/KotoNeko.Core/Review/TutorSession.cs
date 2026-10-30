using System.Globalization;
using System.Text;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Core.Srs;

namespace KotoNeko.Core.Review;

/// <summary>What a review shows beside the pattern, by stage.</summary>
public sealed record GrammarCues(bool Meaning, bool Formation);

/// <summary>The rules of a grammar review (free composition): cues, verdicts and repeats.</summary>
public static class GrammarReview
{
    /// <summary>Sentences of your own a lesson asks for, after teaching the point.</summary>
    public const int LessonSentences = 3;

    /// <summary>
    /// Apprentice shows the meaning and how the point attaches; from Guru you recall how
    /// it attaches. Master's contrast and Enlightened's mixed points come later, so those
    /// stages get Guru's cues until then.
    /// </summary>
    public static GrammarCues CuesFor(SrsStage stage) =>
        stage <= SrsStage.Apprentice4 ? new GrammarCues(true, true) : new GrammarCues(true, false);

    /// <summary>What a review's deciding verdict does: only a confident miss costs a stage.</summary>
    public static GrammarReviewEffect Decide(GradingVerdict verdict) =>
        verdict.TargetUsedCorrectly ? GrammarReviewEffect.Advanced
        : string.Equals(verdict.Confidence, "low", StringComparison.OrdinalIgnoreCase) ? GrammarReviewEffect.Kept
        : GrammarReviewEffect.Dropped;

    /// <summary>Apply a decided review to the point's SRS item. Practice changes nothing.</summary>
    public static void Apply(SrsItem srs, GrammarReviewEffect effect, DateTime now)
    {
        switch (effect) {
            case GrammarReviewEffect.Advanced:
                SrsEngine.ApplyReview(srs, 0, now);
                break;
            case GrammarReviewEffect.Dropped:
                // A miss counts once, however many rewrites follow it.
                SrsEngine.ApplyReview(srs, 1, now);
                break;
            case GrammarReviewEffect.Kept:
                SrsEngine.Repeat(srs, now);
                break;
        }
    }

    /// <summary>
    /// A sentence for comparing with earlier ones: full-width and half-width forms made
    /// the same, with spaces and punctuation removed.
    /// </summary>
    public static string Normalise(string sentence)
    {
        StringBuilder kept = new();
        foreach (char c in sentence.Normalize(NormalizationForm.FormKC)) {
            UnicodeCategory category = char.GetUnicodeCategory(c);
            if (!char.IsWhiteSpace(c) && !char.IsPunctuation(c) && category != UnicodeCategory.MathSymbol) {
                kept.Append(c);
            }
        }
        return kept.ToString();
    }

    /// <summary>Whether the sentence is one already written for the point (ignoring spaces and punctuation).</summary>
    public static bool IsRepeat(string sentence, IEnumerable<string> earlier)
    {
        string normalised = Normalise(sentence);
        return normalised.Length > 0 && earlier.Any(e => Normalise(e) == normalised);
    }
}

/// <summary>
/// A grammar review session: the due points in order, one at a time. Grading is
/// asynchronous and can fail, so unlike <see cref="ReviewSession"/> the session only
/// keeps the queue and decides what each verdict counts as; the caller grades and saves.
/// A point's first verdict decides its review; rewrites after it are practice.
/// </summary>
public sealed class TutorSession
{
    private readonly List<int> _queue;

    public TutorSession(IEnumerable<int> pointIds)
    {
        _queue = pointIds.Distinct().ToList();
        Total = _queue.Count;
    }

    /// <summary>The point being reviewed, or null when the session is over.</summary>
    public int? Current => _queue.Count > 0 ? _queue[0] : null;

    public int Total { get; }
    public int Remaining => _queue.Count;
    public int Reviewed { get; private set; }
    public int Passed { get; private set; }
    public int LeftForLater { get; private set; }

    /// <summary>What the current point's review counted as, once its first verdict is in.</summary>
    public GrammarReviewEffect? Decided { get; private set; }

    /// <summary>Record a verdict on the current point and return what it does.</summary>
    public GrammarReviewEffect Record(GradingVerdict verdict)
    {
        if (Current is null) {
            throw new InvalidOperationException("The session is over.");
        }
        if (Decided is not null) {
            return GrammarReviewEffect.Practice;
        }
        GrammarReviewEffect effect = GrammarReview.Decide(verdict);
        Decided = effect;
        Reviewed++;
        if (effect == GrammarReviewEffect.Advanced) {
            Passed++;
        }
        return effect;
    }

    /// <summary>Move on from a decided point. Returns false when the current point isn't decided yet.</summary>
    public bool Next()
    {
        if (Current is null || Decided is null) {
            return false;
        }
        _queue.RemoveAt(0);
        Decided = null;
        return true;
    }

    /// <summary>Drop the current point from this session undecided: it stays due for next time.</summary>
    public bool LeaveForLater()
    {
        if (Current is null || Decided is not null) {
            return false;
        }
        _queue.RemoveAt(0);
        LeftForLater++;
        return true;
    }
}
