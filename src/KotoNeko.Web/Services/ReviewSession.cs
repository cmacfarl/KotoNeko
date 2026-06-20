using KotoNeko.Core.Domain;
using KotoNeko.Core.Srs;
using KotoNeko.Core.Text;

namespace KotoNeko.Web.Services;

/// <summary>The outcome of submitting an answer.</summary>
public class SubmitOutcome
{
    public required AnswerJudgment Judgment { get; init; }

    /// <summary>True if this submission finished the current card.</summary>
    public bool CardFinalised { get; init; }

    /// <summary>The accepted answer, surfaced when the user was wrong.</summary>
    public string? CorrectAnswer { get; init; }
}

/// <summary>
/// A stateful, in-memory review/lesson session presenting one question at a time.
/// All SRS mutations happen on the in-memory tracked entities; nothing is written
/// to the database until <see cref="BuildPendingResults"/> is persisted by the
/// caller. This makes the native "undo" feature exact: every submission snapshots
/// the full session state, and <see cref="Undo"/> simply restores the snapshot,
/// rolling back stage changes, miss counts, and pending logs together.
/// </summary>
public class ReviewSession
{
    private readonly List<ReviewCard> _cards;
    private readonly List<QRef> _queue = new();
    private readonly List<ReviewLog> _pendingLogs = new();
    private readonly Stack<Memento> _undo = new();
    private readonly DateTime _now;
    private readonly Random _random;

    public ReviewSession(IReadOnlyList<ReviewCard> cards, SessionKind kind, DateTime now, int randomSeed)
    {
        _cards = cards.ToList();
        Kind = kind;
        _now = now;
        _random = new Random(randomSeed);

        for (int c = 0; c < _cards.Count; c++)
        {
            for (int q = 0; q < _cards[c].Questions.Count; q++)
            {
                _queue.Add(new QRef(c, q));
            }
        }

        Shuffle(_queue);
        TotalQuestions = _queue.Count;
    }

    public SessionKind Kind { get; }

    public int TotalQuestions { get; }

    public int AnsweredQuestions => _cards.Sum(c => c.Questions.Count(q => q.Answered));

    public int TotalCards => _cards.Count;

    public int FinishedCards => _cards.Count(c => c.Finalised);

    public int CorrectCount { get; private set; }

    public int IncorrectCount { get; private set; }

    public bool IsComplete => _queue.Count == 0;

    public bool CanUndo => _undo.Count > 0;

    /// <summary>The card the current question belongs to.</summary>
    public ReviewCard? CurrentCard => _queue.Count > 0 ? _cards[_queue[0].Card] : null;

    /// <summary>The question currently being asked.</summary>
    public ReviewQuestion? CurrentQuestion =>
        _queue.Count > 0 ? _cards[_queue[0].Card].Questions[_queue[0].Question] : null;

    /// <summary>
    /// Submit a typed answer for the current question. A "close, try again" result
    /// does not advance or record anything.
    /// </summary>
    public SubmitOutcome Submit(string typed)
    {
        if (_queue.Count == 0) {
            return new SubmitOutcome { Judgment = AnswerJudgment.Incorrect };
        }

        QRef current = _queue[0];
        ReviewCard card = _cards[current.Card];
        ReviewQuestion question = card.Questions[current.Question];

        AnswerJudgment judgment = question.Grade(typed);
        if (judgment == AnswerJudgment.CloseTryAgain) {
            // No state change: let the user retype.
            return new SubmitOutcome { Judgment = judgment, CorrectAnswer = question.ExpectedPrimary };
        }

        // Snapshot before mutating so undo can restore exactly.
        PushSnapshot();

        bool finalised = false;
        if (judgment == AnswerJudgment.Correct) {
            question.Answered = true;
            _queue.RemoveAt(0);

            if (card.AllAnswered && !card.Finalised) {
                FinaliseCard(card);
                finalised = true;
            }
        } else {
            question.MissedAtLeastOnce = true;
            card.IncorrectCount++;
            // Requeue the missed question to be asked again, a little later.
            _queue.RemoveAt(0);
            int insertAt = Math.Min(_queue.Count, RequeueDistance());
            _queue.Insert(insertAt, current);
        }

        return new SubmitOutcome {
            Judgment = judgment,
            CardFinalised = finalised,
            CorrectAnswer = question.ExpectedPrimary,
        };
    }

    /// <summary>Undo the most recent submission, restoring full session state.</summary>
    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        Memento m = _undo.Pop();
        m.Restore(this);
    }

    /// <summary>
    /// Build the database changes accumulated so far: the mutated SRS items and the
    /// review logs for finalised cards. Safe to call when the session ends (or is
    /// abandoned) to persist progress.
    /// </summary>
    public PendingResults BuildPendingResults()
    {
        List<SrsItem> srsItems = _cards.Where(c => c.Finalised).Select(c => c.Srs).ToList();
        return new PendingResults(srsItems, _pendingLogs.ToList());
    }

    private void FinaliseCard(ReviewCard card)
    {
        // A lesson teaches the item; it does not penalise misses made while learning.
        if (Kind == SessionKind.Lesson && card.Srs.Stage == SrsStage.Locked) {
            SrsEngine.Unlock(card.Srs, _now);
        } else {
            SrsEngine.ApplyReview(card.Srs, card.IncorrectCount, _now);
        }

        card.Finalised = true;

        if (card.IncorrectCount > 0) {
            IncorrectCount++;
        } else {
            CorrectCount++;
        }

        // One log row per question, capturing whether it was ultimately missed.
        foreach (ReviewQuestion q in card.Questions) {
            _pendingLogs.Add(new ReviewLog {
                VocabularyId = card.Vocabulary.Id,
                ReviewedAt = _now,
                QuestionType = q.Type,
                ConjugationForm = q.Form,
                ConjugationPolarity = q.Polarity,
                WasCorrect = !q.MissedAtLeastOnce,
                StageAtReview = card.Srs.Stage,
            });
        }
    }

    private int RequeueDistance() {
        // Reinsert a missed question a few positions back so it returns soon, but
        // not immediately. Clamp keeps small queues sane.
        int span = Math.Max(1, Math.Min(_queue.Count, 4));
        return _random.Next(1, span + 1);
    }

    private void Shuffle(List<QRef> list) {
        for (int i = list.Count - 1; i > 0; i--) {
            int j = _random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private void PushSnapshot()
    {
        _undo.Push(Memento.Capture(this));
    }

    private readonly record struct QRef(int Card, int Question);

    /// <summary>The DB changes a session has accumulated.</summary>
    public sealed record PendingResults(IReadOnlyList<SrsItem> SrsItems, IReadOnlyList<ReviewLog> Logs);

    /// <summary>A full snapshot of mutable session state, used to implement undo.</summary>
    private sealed class Memento
    {
        private List<QRef> _queue = new();
        private int _correct;
        private int _incorrect;
        private int _pendingLogCount;
        private CardState[] _cards = Array.Empty<CardState>();

        public static Memento Capture(ReviewSession s)
        {
            Memento m = new()
            {
                _queue = new List<QRef>(s._queue),
                _correct = s.CorrectCount,
                _incorrect = s.IncorrectCount,
                _pendingLogCount = s._pendingLogs.Count,
                _cards = new CardState[s._cards.Count],
            };

            for (int i = 0; i < s._cards.Count; i++)
            {
                ReviewCard c = s._cards[i];
                QuestionState[] questions = new QuestionState[c.Questions.Count];
                for (int q = 0; q < c.Questions.Count; q++)
                {
                    ReviewQuestion rq = c.Questions[q];
                    questions[q] = new QuestionState(rq.Answered, rq.MissedAtLeastOnce);
                }

                SrsItem srs = c.Srs;
                m._cards[i] = new CardState(
                    c.IncorrectCount,
                    c.Finalised,
                    questions,
                    srs.Stage,
                    srs.NextReviewAt,
                    srs.UnlockedAt,
                    srs.BurnedAt,
                    srs.CorrectCount,
                    srs.IncorrectCount);
            }

            return m;
        }

        public void Restore(ReviewSession s)
        {
            s._queue.Clear();
            s._queue.AddRange(_queue);
            s.CorrectCount = _correct;
            s.IncorrectCount = _incorrect;

            if (s._pendingLogs.Count > _pendingLogCount)
            {
                s._pendingLogs.RemoveRange(_pendingLogCount, s._pendingLogs.Count - _pendingLogCount);
            }

            for (int i = 0; i < s._cards.Count; i++)
            {
                ReviewCard c = s._cards[i];
                CardState cs = _cards[i];
                c.IncorrectCount = cs.IncorrectCount;
                c.Finalised = cs.Finalised;

                for (int q = 0; q < c.Questions.Count; q++)
                {
                    c.Questions[q].Answered = cs.Questions[q].Answered;
                    c.Questions[q].MissedAtLeastOnce = cs.Questions[q].Missed;
                }

                SrsItem srs = c.Srs;
                srs.Stage = cs.Stage;
                srs.NextReviewAt = cs.NextReviewAt;
                srs.UnlockedAt = cs.UnlockedAt;
                srs.BurnedAt = cs.BurnedAt;
                srs.CorrectCount = cs.SrsCorrect;
                srs.IncorrectCount = cs.SrsIncorrect;
            }
        }

        private readonly record struct QuestionState(bool Answered, bool Missed);

        private readonly record struct CardState(
            int IncorrectCount,
            bool Finalised,
            QuestionState[] Questions,
            SrsStage Stage,
            DateTime? NextReviewAt,
            DateTime? UnlockedAt,
            DateTime? BurnedAt,
            int SrsCorrect,
            int SrsIncorrect);
    }
}
