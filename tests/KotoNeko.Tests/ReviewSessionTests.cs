using KotoNeko.Core.Domain;
using KotoNeko.Core.Review;
using KotoNeko.Core.Text;
using Xunit;

namespace KotoNeko.Tests;

public class ReviewSessionTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int Seed = 42;

    [Fact]
    public void All_Correct_Advances_Stage_And_Logs_Each_Question()
    {
        ReviewCard card = Card(1, SrsStage.Apprentice2, Meaning("newspaper"), Reading("しんぶん"));
        ReviewSession session = Review(card);

        AnswerAllCorrectly(session);

        Assert.True(session.IsComplete);
        Assert.Equal(SrsStage.Apprentice3, card.Srs.Stage);
        Assert.Equal(1, session.CorrectCount);
        Assert.Equal(0, session.IncorrectCount);

        ReviewSession.PendingResults results = session.CommitPendingResults();
        Assert.Same(card.Srs, Assert.Single(results.SrsItems));
        Assert.Equal(2, results.Logs.Count);
        Assert.All(results.Logs, l => Assert.True(l.WasCorrect));
    }

    [Fact]
    public void Miss_Requeues_The_Question_And_Drops_The_Stage()
    {
        ReviewCard card = Card(1, SrsStage.Guru1, Meaning("newspaper"), Reading("しんぶん"));
        ReviewSession session = Review(card);
        ReviewQuestion missed = session.CurrentQuestion!;

        SubmitOutcome outcome = session.Submit(Wrong(missed));

        Assert.Equal(AnswerJudgment.Incorrect, outcome.Judgment);
        Assert.Equal(missed.ExpectedPrimary, outcome.CorrectAnswer);
        Assert.False(session.IsComplete);
        Assert.Equal(0, session.AnsweredQuestions);
        Assert.Equal(1, card.IncorrectCount);

        AnswerAllCorrectly(session);

        // One miss at Guru: ceil(1 / 2) = 1, doubled at Guru+ = 2 stages down.
        Assert.Equal(SrsStage.Apprentice3, card.Srs.Stage);
        Assert.Equal(1, session.IncorrectCount);

        ReviewSession.PendingResults results = session.CommitPendingResults();
        Assert.False(results.Logs.Single(l => l.QuestionType == missed.Type).WasCorrect);
        Assert.True(results.Logs.Single(l => l.QuestionType != missed.Type).WasCorrect);
    }

    [Fact]
    public void Conjugation_Miss_Is_Practice_Only()
    {
        ReviewCard card = Card(1, SrsStage.Apprentice2, Meaning("to eat"), Conjugation("たべた"));
        ReviewSession session = Review(card);

        bool missedConjugation = false;
        while (!session.IsComplete)
        {
            ReviewQuestion q = session.CurrentQuestion!;
            if (q.Type == QuestionType.Conjugation && !missedConjugation)
            {
                session.Submit(Wrong(q));
                missedConjugation = true;
            }
            else
            {
                session.Submit(q.ExpectedPrimary);
            }
        }

        Assert.Equal(SrsStage.Apprentice3, card.Srs.Stage);
        Assert.Equal(1, session.CorrectCount);
        Assert.Equal(0, session.IncorrectCount);

        ReviewLog conjugationLog = session.CommitPendingResults().Logs
            .Single(l => l.QuestionType == QuestionType.Conjugation);
        Assert.False(conjugationLog.WasCorrect);
    }

    [Fact]
    public void Lesson_Unlocks_The_Item_Regardless_Of_Misses()
    {
        ReviewCard card = Card(1, SrsStage.Locked, Meaning("newspaper"), Reading("しんぶん"));
        ReviewSession session = new(new[] { card }, SessionKind.Lesson, Now, Seed);

        session.Submit(Wrong(session.CurrentQuestion!));
        AnswerAllCorrectly(session);

        Assert.Equal(SrsStage.Apprentice1, card.Srs.Stage);
        Assert.Equal(Now + TimeSpan.FromHours(4), card.Srs.NextReviewAt);
        Assert.Equal(Now, card.Srs.UnlockedAt);
    }

    [Fact]
    public void Close_Answer_Changes_Nothing()
    {
        ReviewCard card = Card(1, SrsStage.Apprentice2, Meaning("newspaper"), Reading("しんぶん"));
        ReviewSession session = Review(card);
        ReviewQuestion before = session.CurrentQuestion!;

        SubmitOutcome outcome = session.Submit(Close(before));

        Assert.Equal(AnswerJudgment.CloseTryAgain, outcome.Judgment);
        Assert.Same(before, session.CurrentQuestion);
        Assert.False(before.MissedAtLeastOnce);
        Assert.Equal(0, card.IncorrectCount);
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Undo_After_A_Miss_Restores_The_Question_Without_Penalty()
    {
        ReviewCard card = Card(1, SrsStage.Guru1, Meaning("newspaper"), Reading("しんぶん"));
        ReviewSession session = Review(card);
        ReviewQuestion missed = session.CurrentQuestion!;

        session.Submit(Wrong(missed));
        Assert.True(session.CanUndo);
        session.Undo();

        Assert.False(session.CanUndo);
        Assert.Same(missed, session.CurrentQuestion);
        Assert.False(missed.MissedAtLeastOnce);
        Assert.Equal(0, card.IncorrectCount);

        AnswerAllCorrectly(session);
        Assert.Equal(SrsStage.Guru2, card.Srs.Stage);
    }

    [Fact]
    public void Undo_Of_A_Finalising_Answer_Unfinalises_The_Card()
    {
        ReviewCard card = Card(1, SrsStage.Apprentice2, Meaning("newspaper"));
        ReviewSession session = Review(card);

        SubmitOutcome outcome = session.Submit("newspaper");
        Assert.True(outcome.CardFinalised);
        Assert.True(session.IsComplete);

        session.Undo();

        Assert.False(session.IsComplete);
        Assert.False(card.Finalised);
        Assert.Equal(SrsStage.Apprentice2, card.Srs.Stage);
        Assert.Equal(0, session.CorrectCount);

        ReviewSession.PendingResults results = session.CommitPendingResults();
        Assert.Empty(results.SrsItems);
        Assert.Empty(results.Logs);
    }

    [Fact]
    public void Commit_Ends_The_Undo_Window()
    {
        ReviewCard card = Card(1, SrsStage.Apprentice2, Meaning("newspaper"), Reading("しんぶん"));
        ReviewSession session = Review(card);
        ReviewQuestion missed = session.CurrentQuestion!;

        session.Submit(Wrong(missed));
        session.CommitPendingResults();

        Assert.False(session.CanUndo);
        session.Undo();
        Assert.True(missed.MissedAtLeastOnce);
        Assert.Equal(1, card.IncorrectCount);
    }

    [Fact]
    public void Only_The_Latest_Answer_Is_Undoable()
    {
        ReviewCard first = Card(1, SrsStage.Apprentice2, Meaning("newspaper"));
        ReviewCard second = Card(2, SrsStage.Apprentice2, Meaning("dog"));
        ReviewSession session = Review(first, second);
        ReviewQuestion missed = session.CurrentQuestion!;

        session.Submit(Wrong(missed));
        session.Submit(Wrong(session.CurrentQuestion!));
        session.Undo();
        session.Undo();

        // The first miss stands: undo reached back one answer only.
        Assert.True(missed.MissedAtLeastOnce);
    }

    /// <summary>
    /// Regression: a card persisted the moment it finalised could then be undone,
    /// leaving the database with a stage the session no longer agreed with, and
    /// the re-answered card was never written again. Drive the session the way the
    /// UI does (commit whenever the user moves on) against a fake store and check
    /// the store ends up matching memory.
    /// </summary>
    [Fact]
    public void Committed_Results_Always_Match_The_Final_Session_State()
    {
        ReviewCard[] cards =
        {
            Card(1, SrsStage.Guru1, Meaning("newspaper"), Reading("しんぶん")),
            Card(2, SrsStage.Guru1, Meaning("dog"), Reading("いぬ")),
            Card(3, SrsStage.Apprentice4, Meaning("cat")),
        };
        ReviewSession session = Review(cards);

        Dictionary<int, SrsStage> storedStages = new();
        Dictionary<int, int> timesStored = new();
        List<ReviewLog> storedLogs = new();

        void CommitToStore()
        {
            ReviewSession.PendingResults results = session.CommitPendingResults();
            foreach (SrsItem srs in results.SrsItems)
            {
                storedStages[srs.StudyItemId] = srs.Stage;
                timesStored[srs.StudyItemId] = timesStored.GetValueOrDefault(srs.StudyItemId) + 1;
            }
            storedLogs.AddRange(results.Logs);
        }

        // Every finalising answer is undone once and replaced by a miss, so each
        // card's true outcome includes a penalty the undone answer did not have.
        HashSet<int> undoneOnce = new();
        while (!session.IsComplete)
        {
            ReviewCard card = session.CurrentCard!;
            ReviewQuestion q = session.CurrentQuestion!;

            SubmitOutcome outcome = session.Submit(q.ExpectedPrimary);
            if (outcome.CardFinalised && undoneOnce.Add(card.Item.Id))
            {
                session.Undo();
                session.Submit(Wrong(q));
            }

            CommitToStore();
        }
        CommitToStore();

        foreach (ReviewCard card in cards)
        {
            int id = card.Item.Id;
            Assert.Equal(1, timesStored[id]);
            Assert.Equal(card.Srs.Stage, storedStages[id]);

            List<ReviewLog> logs = storedLogs.Where(l => l.StudyItemId == id).ToList();
            Assert.Equal(card.Questions.Count, logs.Count);
            Assert.Single(logs, l => !l.WasCorrect);
        }

        Assert.Equal(SrsStage.Apprentice3, storedStages[1]);
        Assert.Equal(SrsStage.Apprentice3, storedStages[2]);
        Assert.Equal(SrsStage.Apprentice3, storedStages[3]);
    }

    private static ReviewSession Review(params ReviewCard[] cards) =>
        new(cards, SessionKind.Review, Now, Seed);

    private static ReviewCard Card(int id, SrsStage stage, params ReviewQuestion[] questions) => new()
    {
        Item = new Vocabulary { Id = id },
        Srs = new SrsItem { Id = id, StudyItemId = id, Stage = stage },
        Questions = questions.ToList(),
    };

    private static ReviewQuestion Meaning(string expected) => new()
    {
        Type = QuestionType.Meaning,
        Prompt = "Meaning",
        ExpectedPrimary = expected,
    };

    private static ReviewQuestion Reading(string expected) => new()
    {
        Type = QuestionType.Reading,
        Prompt = "Reading",
        ExpectedPrimary = expected,
    };

    private static ReviewQuestion Conjugation(string expected) => new()
    {
        Type = QuestionType.Conjugation,
        Prompt = "Past · Affirmative",
        ExpectedPrimary = expected,
        Form = ConjugationForm.Past,
        Polarity = Polarity.Affirmative,
    };

    private static string Wrong(ReviewQuestion q) =>
        q.Type == QuestionType.Meaning ? "qqqqqqqqqqqqqq" : "ぬぬぬぬぬ";

    private static string Close(ReviewQuestion q) =>
        q.Type == QuestionType.Meaning ? "   " : "x";

    private static void AnswerAllCorrectly(ReviewSession session)
    {
        while (!session.IsComplete)
        {
            session.Submit(session.CurrentQuestion!.ExpectedPrimary);
        }
    }
}
