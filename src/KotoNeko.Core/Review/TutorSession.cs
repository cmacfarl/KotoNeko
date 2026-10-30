using System.Globalization;
using System.Text;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Core.Srs;

namespace KotoNeko.Core.Review;

/// <summary>What a review shows beside the pattern, by stage.</summary>
public sealed record GrammarCues(bool Meaning, bool Formation);

/// <summary>A grammar list entry as contrast choice sees it.</summary>
public sealed record GrammarListPoint(string Pattern, string Meaning, JlptLevel Level, GrammarStatus Status);

/// <summary>
/// The similar point a Master review asks for a second sentence with, with the meaning
/// sent to the grader and how it differs (shown after grading).
/// </summary>
public sealed record ContrastChoice(string Pattern, string Meaning, string Difference);

/// <summary>The rules of a grammar review (free composition): cues, verdicts and repeats.</summary>
public static class GrammarReview
{
    /// <summary>Sentences of your own a lesson asks for, after teaching the point.</summary>
    public const int LessonSentences = 3;

    /// <summary>
    /// Apprentice shows the meaning and how the point attaches; Guru the meaning only;
    /// from Master, the pattern alone (Master adds a contrast; Enlightened's mixed points
    /// come later).
    /// </summary>
    public static GrammarCues CuesFor(SrsStage stage) =>
        stage <= SrsStage.Apprentice4 ? new GrammarCues(true, true)
        : stage <= SrsStage.Guru2 ? new GrammarCues(true, false)
        : new GrammarCues(false, false);

    /// <summary>Whether a review at this stage asks for a contrast sentence too.</summary>
    public static bool AsksForContrast(SrsStage stage) => stage >= SrsStage.Master;

    /// <summary>A pattern for matching: width-folded, without 〜 or spaces (〜けど and けど match).</summary>
    public static string NormalisePattern(string pattern) =>
        new(pattern.Normalize(NormalizationForm.FormKC)
            .Where(c => !char.IsWhiteSpace(c) && c is not ('〜' or '~' or '～' or '…'))
            .ToArray());

    /// <summary>
    /// Pick one of a point's contrasts for a Master review: one on the grammar list that
    /// the learner is studying or knows, or that is no harder than the point, if any;
    /// otherwise one not on the list (its meaning unknown). Null when the point has none.
    /// </summary>
    public static ContrastChoice? ChooseContrast(
        JlptLevel level, IReadOnlyList<GrammarContrast> contrasts, IReadOnlyList<GrammarListPoint> list, Random random)
    {
        Dictionary<string, GrammarListPoint> byPattern = new();
        foreach (GrammarListPoint entry in list) {
            byPattern.TryAdd(NormalisePattern(entry.Pattern), entry);
        }
        List<ContrastChoice> usable = new();
        List<ContrastChoice> unlisted = new();
        foreach (GrammarContrast c in contrasts.Where(c => NormalisePattern(c.Pattern).Length > 0)) {
            if (byPattern.TryGetValue(NormalisePattern(c.Pattern), out GrammarListPoint? entry)) {
                // JLPT levels count down: N5 (5) is easier than N4 (4).
                if (entry.Status != GrammarStatus.NotYet || entry.Level >= level) {
                    usable.Add(new ContrastChoice(c.Pattern, entry.Meaning, c.Difference));
                }
            } else {
                unlisted.Add(new ContrastChoice(c.Pattern, "a grammar point similar to the one being reviewed", c.Difference));
            }
        }
        List<ContrastChoice> from = usable.Count > 0 ? usable : unlisted;
        return from.Count == 0 ? null : from[random.Next(from.Count)];
    }

    /// <summary>What a review's deciding verdict does: only a confident miss costs a stage.</summary>
    public static GrammarReviewEffect Decide(GradingVerdict verdict) =>
        verdict.TargetUsedCorrectly ? GrammarReviewEffect.Advanced
        : IsUnsure(verdict) ? GrammarReviewEffect.Kept
        : GrammarReviewEffect.Dropped;

    /// <summary>A verdict the grader wasn't sure of.</summary>
    public static bool IsUnsure(GradingVerdict verdict) =>
        string.Equals(verdict.Confidence, "low", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a verdict needs a second opinion before it can count: an unsure miss.</summary>
    public static bool NeedsSecondOpinion(GradingVerdict verdict) => !verdict.TargetUsedCorrectly && IsUnsure(verdict);

    /// <summary>
    /// What a review comes to with a second opinion on an unsure miss: a pass overturns
    /// it, a confident miss confirms it, and an unsure one (or none, when the second
    /// grader couldn't be asked) leaves the point where it is.
    /// </summary>
    public static GrammarReviewEffect Decide(GradingVerdict first, GradingVerdict? second)
    {
        GrammarReviewEffect effect = Decide(first);
        if (effect != GrammarReviewEffect.Kept || second is null) {
            return effect;
        }
        return Decide(second);
    }

    /// <summary>
    /// A review of several sentences (Master: the point and a contrast) passes only if
    /// every one does; a confident miss on any drops it; otherwise it's kept.
    /// </summary>
    public static GrammarReviewEffect Combine(IEnumerable<GrammarReviewEffect> effects)
    {
        List<GrammarReviewEffect> all = effects.ToList();
        return all.Contains(GrammarReviewEffect.Dropped) ? GrammarReviewEffect.Dropped
            : all.Contains(GrammarReviewEffect.Kept) ? GrammarReviewEffect.Kept
            : GrammarReviewEffect.Advanced;
    }

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

    /// <summary>
    /// Record what a graded answer to the current point came to (see
    /// <see cref="GrammarReview.Decide(GradingVerdict, GradingVerdict?)"/>) and return
    /// what it does: the first decides the review, later ones are practice.
    /// </summary>
    public GrammarReviewEffect Record(GrammarReviewEffect effect)
    {
        if (Current is null) {
            throw new InvalidOperationException("The session is over.");
        }
        if (Decided is not null) {
            return GrammarReviewEffect.Practice;
        }
        Decided = effect;
        Reviewed++;
        if (effect == GrammarReviewEffect.Advanced) {
            Passed++;
        }
        return effect;
    }

    /// <summary>The current point's miss was overturned (a dispute): it counts as a pass.</summary>
    public void Overturned()
    {
        if (Decided is GrammarReviewEffect.Dropped or GrammarReviewEffect.Kept) {
            Decided = GrammarReviewEffect.Advanced;
            Passed++;
        }
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
