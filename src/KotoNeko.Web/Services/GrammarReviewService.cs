using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Core.Review;
using KotoNeko.Data;
using KotoNeko.Llm;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>A due grammar point as its review shows it, with the sentences already written for it.</summary>
public sealed record GrammarReviewItem(GrammarPoint Point, GrammarCues Cues, IReadOnlyList<string> Earlier);

/// <summary>What a graded review sentence came to.</summary>
/// <param name="SavedNote">Where a passing sentence was saved, or null.</param>
public sealed record GrammarReviewResult(
    GradingVerdict Verdict, GrammarReviewEffect Effect, SrsStage StageBefore, SrsStage StageAfter, string? SavedNote);

/// <summary>
/// Grammar reviews (Phase 5): free composition with a point, graded by the Grade job.
/// A point's first verdict in a review moves it on the SRS (only a confident miss
/// drops it); rewrites after that are practice. Every graded sentence is kept, and
/// passing ones go into the point's sentence bank.
/// </summary>
public sealed class GrammarReviewService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly GradingService _grading;
    private readonly GrammarPointService _points;

    public GrammarReviewService(IDbContextFactory<KotoNekoDbContext> factory, GradingService grading, GrammarPointService points)
    {
        _factory = factory;
        _grading = grading;
        _points = points;
    }

    public LlmRoute? Route => _grading.Route;

    /// <summary>Learned points due now: not asleep, not burned, their list entry not pruned.</summary>
    private static IQueryable<GrammarPoint> Due(KotoNekoDbContext db, DateTime now) =>
        db.GrammarPoints.Where(p => !p.IsAsleep && !p.Entry!.IsPruned && p.Srs != null
            && p.Srs.Stage != SrsStage.Locked && p.Srs.Stage != SrsStage.Burned
            && p.Srs.NextReviewAt != null && p.Srs.NextReviewAt <= now);

    internal static Task<int> CountDueAsync(KotoNekoDbContext db, DateTime now) => Due(db, now).CountAsync();

    /// <summary>A session of the points due now, longest overdue first.</summary>
    public async Task<TutorSession> StartAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<int> ids = await Due(db, now).OrderBy(p => p.Srs!.NextReviewAt).ThenBy(p => p.Id).Select(p => p.Id).ToListAsync();
        return new TutorSession(ids);
    }

    /// <summary>A learned point with its cues and earlier sentences, or null when it's gone or not learned.</summary>
    public async Task<GrammarReviewItem?> GetAsync(int pointId)
    {
        if (await _points.GetAsync(pointId) is not { Srs: { } srs } point) {
            return null;
        }
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return new GrammarReviewItem(point, GrammarReview.CuesFor(srs.Stage), await EarlierAsync(db, pointId));
    }

    /// <summary>
    /// Sentences already written for a point, newest first: review attempts, and the
    /// examples that are the user's own (written by hand, or a lesson's compositions).
    /// </summary>
    private static async Task<List<string>> EarlierAsync(KotoNekoDbContext db, int pointId)
    {
        List<string> attempts = await db.GrammarReviewAttempts.AsNoTracking()
            .Where(a => a.GrammarPointId == pointId)
            .OrderByDescending(a => a.AttemptedAt)
            .Select(a => a.Sentence)
            .ToListAsync();
        List<string> examples = await db.GrammarExamples.AsNoTracking()
            .Where(x => x.GrammarPointId == pointId && x.Source != GrammarExampleSource.Generated)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Text)
            .ToListAsync();
        return attempts.Concat(examples).Distinct().ToList();
    }

    /// <summary>
    /// Grade a sentence for the session's current point and save what it did. Returns
    /// the result, or a problem (a repeat, or a sentence too long) without grading.
    /// Throws <see cref="LlmException"/> when it couldn't be graded; nothing is saved then.
    /// </summary>
    public async Task<(GrammarReviewResult? Result, string? Problem)> GradeAsync(
        TutorSession session, string sentence, string intent, CancellationToken cancellationToken = default)
    {
        if (session.Current is not int pointId) {
            return (null, "The session is over.");
        }
        sentence = GrammarContent.CleanSentence(sentence);
        intent = intent.Trim();
        if (sentence.Length == 0) {
            return (null, "Write a sentence first.");
        }
        if (sentence.Length > GrammarReviewAttempt.MaxSentenceLength || intent.Length > GrammarReviewAttempt.MaxIntentLength) {
            return (null, $"Keep the sentence under {GrammarReviewAttempt.MaxSentenceLength} characters and what you mean under {GrammarReviewAttempt.MaxIntentLength}.");
        }

        GrammarListEntry entry;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            GrammarPoint? point = await db.GrammarPoints.AsNoTracking().Include(p => p.Entry).FirstOrDefaultAsync(p => p.Id == pointId, cancellationToken);
            if (point?.Entry is null) {
                return (null, "That grammar point no longer exists.");
            }
            entry = point.Entry;
            if (GrammarReview.IsRepeat(sentence, await EarlierAsync(db, pointId))) {
                return (null, "You've written that sentence for this point before. Try a new one.");
            }
            List<string> drafted = await db.GrammarExamples.AsNoTracking()
                .Where(x => x.GrammarPointId == pointId && x.Source == GrammarExampleSource.Generated)
                .Select(x => x.Text)
                .ToListAsync(cancellationToken);
            if (GrammarReview.IsRepeat(sentence, drafted)) {
                return (null, "That's one of this point's lesson examples. Try a sentence of your own.");
            }
        }

        LlmResult<GradingVerdict> graded = await _grading.GradeAsync(
            new GradingTask(entry.Pattern, entry.Meaning, "", sentence, intent), LlmFeature.ReviewGrading, cancellationToken: cancellationToken);
        GradingVerdict verdict = graded.Value;
        GrammarReviewEffect effect = session.Record(verdict);

        DateTime now = DateTime.UtcNow;
        SrsStage before;
        SrsStage after;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            SrsItem srs = await db.SrsItems.FirstAsync(s => s.StudyItemId == pointId, CancellationToken.None);
            before = srs.Stage;
            if (effect != GrammarReviewEffect.Practice) {
                GrammarReview.Apply(srs, effect, now);
                db.ReviewLogs.Add(new ReviewLog {
                    StudyItemId = pointId,
                    ReviewedAt = now,
                    QuestionType = QuestionType.Composition,
                    WasCorrect = verdict.TargetUsedCorrectly,
                    StageAtReview = before,
                });
            }
            after = srs.Stage;
            db.GrammarReviewAttempts.Add(new GrammarReviewAttempt {
                GrammarPointId = pointId,
                AttemptedAt = now,
                Sentence = sentence,
                Intent = intent,
                StageBefore = before,
                StageAfter = after,
                Effect = effect,
                TargetUsedCorrectly = verdict.TargetUsedCorrectly,
                Natural = verdict.Natural,
                Confidence = verdict.Confidence,
                VerdictJson = graded.Json,
                PromptVersion = Grading.PromptVersion,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }

        string? saved = null;
        if (verdict.TargetUsedCorrectly) {
            // Into the sentence bank: approved when the grader found nothing else to fix,
            // otherwise a draft for the user to look over (the grader can miss things).
            bool clean = verdict.Natural && verdict.Errors is not { Count: > 0 };
            (bool added, _) = await _points.AddExampleAsync(pointId, sentence, intent, GrammarExampleSource.Composition,
                clean ? GrammarContentStatus.Approved : GrammarContentStatus.Draft, voice: false);
            saved = !added ? null
                : clean ? "Saved to your sentences for this point."
                : "Saved to this point's sentences as a draft, since the grader suggested changes: approve or edit it on the point's page.";
        }
        return (new GrammarReviewResult(verdict, effect, before, after, saved), null);
    }
}
