using System.Text.Json;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Core.Review;
using KotoNeko.Core.Srs;
using KotoNeko.Data;
using KotoNeko.Llm;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>
/// A due grammar point as its review shows it: its cues, the sentences already written
/// for it, and at Master the similar point to write a second sentence with.
/// </summary>
public sealed record GrammarReviewItem(GrammarPoint Point, GrammarCues Cues, IReadOnlyList<string> Earlier, ContrastChoice? Contrast);

/// <summary>What the learner wrote for one of a review's sentences, and what they meant by it.</summary>
public sealed record ReviewSentenceInput(string Sentence, string Intent);

/// <summary>One graded sentence of a review, with the second opinion if one was asked.</summary>
/// <param name="SecondProblem">Why a second opinion that was wanted couldn't be had, or null.</param>
public sealed record GradedSentence(
    long AttemptId, GradingTask Task, bool IsContrast, GradingVerdict Verdict,
    GradingVerdict? Second, string? SecondModel, string? SecondProblem)
{
    /// <summary>The verdict that counts: the second opinion when there is one.</summary>
    public GradingVerdict Final => Second ?? Verdict;
}

/// <summary>What a review's sentences came to.</summary>
/// <param name="SavedNote">Where a passing sentence was saved, or null.</param>
public sealed record GrammarReviewResult(
    IReadOnlyList<GradedSentence> Sentences, GrammarReviewEffect Effect, SrsStage StageBefore, SrsStage StageAfter,
    DateTime ReviewedAt, string? SavedNote);

/// <summary>A review sentence that had a second opinion or a dispute, for the point's page.</summary>
public sealed record CheckedAttempt(
    DateTime AttemptedAt, string Sentence, string? ContrastPattern, string Intent,
    GradingVerdict? First, GradingVerdict? Second, string? SecondModel,
    bool Disputed, string DisputeComment, GradingVerdict? DisputeVerdict, string? DisputeModel, bool Overturned);

/// <summary>A disputed sentence and the fresh verdict on it.</summary>
public sealed record DisputedSentence(GradedSentence Sentence, GradingVerdict Verdict, string Model);

/// <summary>What a dispute came to.</summary>
public sealed record DisputeResult(IReadOnlyList<DisputedSentence> Rechecked, bool Overturned, SrsStage StageAfter, string Note);

/// <summary>
/// Grammar reviews (Phase 5): free composition with a point, graded by the Grade job.
/// A point's first graded answer in a review moves it on the SRS: an unsure miss gets
/// a second opinion (the model chosen for second opinions) first, and only a confident miss drops it. A
/// miss can be disputed once, for a fresh second opinion; if that passes the sentence,
/// the review counts as a pass. Rewrites after the review is decided are practice.
/// Every graded sentence is kept, and passing ones go into the point's sentence bank.
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

    public LlmRoute? Route => _grading.Route(LlmFeature.ReviewGrading);
    public LlmRoute? SecondOpinionRoute => _grading.Route(LlmFeature.SecondOpinion);
    public LlmRoute? DisputeRoute => _grading.Route(LlmFeature.Dispute);

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

    /// <summary>A learned point with its cues, earlier sentences and contrast, or null when it's gone or not learned.</summary>
    public async Task<GrammarReviewItem?> GetAsync(int pointId)
    {
        if (await _points.GetAsync(pointId) is not { Srs: { } srs, Entry: { } entry } point) {
            return null;
        }
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        ContrastChoice? contrast = null;
        if (GrammarReview.AsksForContrast(srs.Stage) && point.Contrasts.Count > 0) {
            List<GrammarListPoint> list = await db.GrammarList.AsNoTracking()
                .Where(e => !e.IsPruned && e.Id != entry.Id)
                .Select(e => new GrammarListPoint(e.Pattern, e.Meaning, e.Level, e.Status))
                .ToListAsync();
            contrast = GrammarReview.ChooseContrast(entry.Level, point.Contrasts, list, Random.Shared);
        }
        return new GrammarReviewItem(point, GrammarReview.CuesFor(srs.Stage), await EarlierAsync(db, pointId), contrast);
    }

    /// <summary>
    /// Sentences already written with a point, newest first: review attempts with it,
    /// and the examples that are the user's own (written by hand, or compositions).
    /// </summary>
    private static async Task<List<string>> EarlierAsync(KotoNekoDbContext db, int pointId)
    {
        List<string> attempts = await db.GrammarReviewAttempts.AsNoTracking()
            .Where(a => a.GrammarPointId == pointId && a.ContrastPattern == null)
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
    /// Grade the current point's sentences (one, or at Master the point's then the
    /// contrast's) and save what they did. <paramref name="onSecondOpinion"/> is called
    /// before a second opinion is asked for (it can take a while). Returns the result,
    /// or a problem (a repeat, a missing sentence) without grading. Throws
    /// <see cref="LlmException"/> when the first grading fails; nothing is saved then.
    /// </summary>
    public async Task<(GrammarReviewResult? Result, string? Problem)> GradeAsync(
        TutorSession session, GrammarReviewItem item, IReadOnlyList<ReviewSentenceInput> inputs,
        Func<Task>? onSecondOpinion = null, CancellationToken cancellationToken = default)
    {
        int pointId = item.Point.Id;
        if (session.Current != pointId) {
            return (null, "The session has moved on from this point.");
        }
        GrammarListEntry entry = item.Point.Entry!;
        List<(string Pattern, string Meaning, bool IsContrast)> asked = new() { (entry.Pattern, entry.Meaning, false) };
        if (item.Contrast is { } contrast) {
            asked.Add((contrast.Pattern, contrast.Meaning, true));
        }
        string missing = asked.Count == 1 ? "Write a sentence first." : "Write both sentences first.";
        if (inputs.Count != asked.Count) {
            return (null, missing);
        }

        List<GradingTask> tasks = new();
        for (int i = 0; i < asked.Count; i++) {
            string sentence = GrammarContent.CleanSentence(inputs[i].Sentence);
            string intent = inputs[i].Intent.Trim();
            if (sentence.Length == 0) {
                return (null, missing);
            }
            if (sentence.Length > GrammarReviewAttempt.MaxSentenceLength || intent.Length > GrammarReviewAttempt.MaxIntentLength) {
                return (null, $"Keep each sentence under {GrammarReviewAttempt.MaxSentenceLength} characters and what you mean under {GrammarReviewAttempt.MaxIntentLength}.");
            }
            tasks.Add(new GradingTask(asked[i].Pattern, asked[i].Meaning, "", sentence, intent));
        }
        if (tasks.Count > 1 && GrammarReview.Normalise(tasks[0].Sentence) == GrammarReview.Normalise(tasks[1].Sentence)) {
            return (null, "Write two different sentences: one with each point.");
        }

        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            List<string> earlier = await EarlierAsync(db, pointId);
            List<string> drafted = await db.GrammarExamples.AsNoTracking()
                .Where(x => x.GrammarPointId == pointId && x.Source == GrammarExampleSource.Generated)
                .Select(x => x.Text)
                .ToListAsync(cancellationToken);
            foreach (GradingTask task in tasks) {
                if (GrammarReview.IsRepeat(task.Sentence, earlier)) {
                    return (null, $"You've written {task.Sentence} for this point before. Try a new one.");
                }
                if (GrammarReview.IsRepeat(task.Sentence, drafted)) {
                    return (null, $"{task.Sentence} is one of this point's lesson examples. Try a sentence of your own.");
                }
            }
        }

        LlmResult<GradingVerdict>[] graded = await Task.WhenAll(tasks.Select(t =>
            _grading.GradeAsync(t, LlmFeature.ReviewGrading, cancellationToken: cancellationToken)));

        // A review not yet decided: unsure misses get a second opinion before they count.
        var seconds = new (GradingVerdict? Verdict, string? Model, string? Problem)[tasks.Count];
        List<int> unsure = Enumerable.Range(0, tasks.Count).Where(i => GrammarReview.NeedsSecondOpinion(graded[i].Value)).ToList();
        if (session.Decided is null && unsure.Count > 0) {
            if (SecondOpinionRoute is not { IsConfigured: true }) {
                foreach (int i in unsure) {
                    seconds[i] = (null, null, "No model is set up for second opinions (see the Models page), so this unsure miss doesn't count either way.");
                }
            } else {
                if (onSecondOpinion is not null) {
                    await onSecondOpinion();
                }
                await Task.WhenAll(unsure.Select(async i => {
                    try {
                        LlmResult<GradingVerdict> second = await _grading.RecheckAsync(tasks[i], LlmFeature.SecondOpinion, cancellationToken: cancellationToken);
                        seconds[i] = (second.Value, $"{second.Route.Provider} · {second.Route.Model}", null);
                    } catch (LlmException ex) {
                        // The first verdict is already paid for: keep it, as an unsure miss.
                        seconds[i] = (null, null, $"The second grader couldn't be asked ({ex.Message}), so this unsure miss doesn't count either way.");
                    }
                }));
            }
        }

        GrammarReviewEffect effect = session.Record(GrammarReview.Combine(
            Enumerable.Range(0, tasks.Count).Select(i => GrammarReview.Decide(graded[i].Value, seconds[i].Verdict))));

        // To the microsecond, as the database keeps it: a dispute finds this review's log row by its time.
        DateTime now = DateTime.UtcNow;
        now = new DateTime(now.Ticks - now.Ticks % 10, DateTimeKind.Utc);
        SrsStage before;
        SrsStage after;
        List<GrammarReviewAttempt> attempts = new();
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            SrsItem srs = await db.SrsItems.FirstAsync(s => s.StudyItemId == pointId, CancellationToken.None);
            before = srs.Stage;
            if (effect != GrammarReviewEffect.Practice) {
                GrammarReview.Apply(srs, effect, now);
                db.ReviewLogs.Add(new ReviewLog {
                    StudyItemId = pointId,
                    ReviewedAt = now,
                    QuestionType = QuestionType.Composition,
                    WasCorrect = effect == GrammarReviewEffect.Advanced,
                    StageAtReview = before,
                });
            }
            after = srs.Stage;
            for (int i = 0; i < tasks.Count; i++) {
                GradingVerdict verdict = graded[i].Value;
                GrammarReviewAttempt attempt = new() {
                    GrammarPointId = pointId,
                    AttemptedAt = now,
                    Sentence = tasks[i].Sentence,
                    Intent = tasks[i].Intent,
                    ContrastPattern = asked[i].IsContrast ? Trim(asked[i].Pattern, GrammarReviewAttempt.MaxContrastLength) : null,
                    StageBefore = before,
                    StageAfter = after,
                    Effect = effect,
                    TargetUsedCorrectly = verdict.TargetUsedCorrectly,
                    Natural = verdict.Natural,
                    Confidence = verdict.Confidence,
                    VerdictJson = graded[i].Json,
                    PromptVersion = Grading.PromptVersion,
                    SecondVerdictJson = seconds[i].Verdict is { } second ? JsonSerializer.Serialize(second) : null,
                    SecondModel = seconds[i].Model is { } model ? Trim(model, GrammarReviewAttempt.MaxModelLength) : null,
                };
                attempts.Add(attempt);
                db.GrammarReviewAttempts.Add(attempt);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }

        List<GradedSentence> sentences = Enumerable.Range(0, tasks.Count)
            .Select(i => new GradedSentence(attempts[i].Id, tasks[i], asked[i].IsContrast, graded[i].Value,
                seconds[i].Verdict, seconds[i].Model, seconds[i].Problem))
            .ToList();
        string? saved = await BankAsync(pointId, sentences[0].Task, sentences[0].Final);
        return (new GrammarReviewResult(sentences, effect, before, after, now, saved), null);
    }

    /// <summary>
    /// Dispute a review's miss: each sentence that missed gets a fresh second opinion
    /// (the model chosen for disputes), with the learner's comment. If every one passes, the miss is
    /// overturned and the review counts as a pass. Once per review. Throws
    /// <see cref="LlmException"/> when a second opinion can't be had; nothing changes then.
    /// </summary>
    public async Task<DisputeResult> DisputeAsync(
        TutorSession session, int pointId, GrammarReviewResult review, string comment, CancellationToken cancellationToken = default)
    {
        comment = Trim(comment.Trim(), GrammarReviewAttempt.MaxIntentLength);
        List<GradedSentence> missed = review.Sentences.Where(s => !s.Final.TargetUsedCorrectly).ToList();
        if (session.Current != pointId || missed.Count == 0
            || session.Decided is not (GrammarReviewEffect.Dropped or GrammarReviewEffect.Kept)
            || review.Effect is not (GrammarReviewEffect.Dropped or GrammarReviewEffect.Kept)) {
            return new DisputeResult(Array.Empty<DisputedSentence>(), false, review.StageAfter, "Only the current review's miss can be disputed.");
        }
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            List<long> ids = missed.Select(s => s.AttemptId).ToList();
            if (await db.GrammarReviewAttempts.AnyAsync(a => ids.Contains(a.Id) && a.Disputed, cancellationToken)) {
                return new DisputeResult(Array.Empty<DisputedSentence>(), false, review.StageAfter, "This review has already been disputed.");
            }
        }

        DisputedSentence[] rechecked = await Task.WhenAll(missed.Select(async s => {
            LlmResult<GradingVerdict> second = await _grading.RecheckAsync(s.Task, LlmFeature.Dispute, comment, cancellationToken);
            return new DisputedSentence(s, second.Value, $"{second.Route.Provider} · {second.Route.Model}");
        }));
        bool overturned = rechecked.All(r => r.Verdict.TargetUsedCorrectly);

        SrsStage stageAfter = review.StageAfter;
        string note;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            if (overturned) {
                SrsItem srs = await db.SrsItems.FirstAsync(s => s.StudyItemId == pointId, CancellationToken.None);
                if (SrsEngine.OverturnMiss(srs, review.StageBefore, review.StageAfter,
                        missCounted: review.Effect == GrammarReviewEffect.Dropped, review.ReviewedAt)) {
                    stageAfter = srs.Stage;
                    ReviewLog? log = await db.ReviewLogs.FirstOrDefaultAsync(r => r.StudyItemId == pointId
                        && r.QuestionType == QuestionType.Composition && r.ReviewedAt == review.ReviewedAt, CancellationToken.None);
                    if (log is not null) {
                        log.WasCorrect = true;
                    }
                    note = $"The second grader says it's right, so this review counts as a pass: {SrsSchedule.DisplayName(review.StageBefore)} → {SrsSchedule.DisplayName(stageAfter)}.";
                } else {
                    overturned = false;
                    note = "The second grader says it's right, but the point has moved on since, so its stage is left as it is.";
                }
            } else {
                note = rechecked.Length == 1
                    ? "The second grader agrees it's a miss, so the review stands."
                    : "The second grader doesn't pass both sentences, so the review stands.";
            }

            foreach (DisputedSentence r in rechecked) {
                GrammarReviewAttempt attempt = await db.GrammarReviewAttempts.FirstAsync(a => a.Id == r.Sentence.AttemptId, CancellationToken.None);
                attempt.Disputed = true;
                attempt.DisputeComment = comment;
                attempt.DisputeVerdictJson = JsonSerializer.Serialize(r.Verdict);
                attempt.DisputeModel = Trim(r.Model, GrammarReviewAttempt.MaxModelLength);
                attempt.Overturned = overturned;
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }

        if (overturned) {
            session.Overturned();
            if (rechecked.FirstOrDefault(r => !r.Sentence.IsContrast) is { } main
                && await BankAsync(pointId, main.Sentence.Task, main.Verdict) is { } saved) {
                note += " " + saved;
            }
        }
        return new DisputeResult(rechecked, overturned, stageAfter, note);
    }

    private static readonly JsonSerializerOptions ReadVerdict = new() { PropertyNameCaseInsensitive = true };

    /// <summary>A point's review sentences that had a second opinion or were disputed, newest first.</summary>
    public async Task<List<CheckedAttempt>> CheckedAttemptsAsync(int pointId)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<GrammarReviewAttempt> rows = await db.GrammarReviewAttempts.AsNoTracking()
            .Where(a => a.GrammarPointId == pointId && (a.SecondVerdictJson != null || a.Disputed))
            .OrderByDescending(a => a.AttemptedAt).ThenBy(a => a.Id)
            .ToListAsync();
        return rows.Select(a => new CheckedAttempt(
            a.AttemptedAt, a.Sentence, a.ContrastPattern, a.Intent,
            Parse(a.VerdictJson), Parse(a.SecondVerdictJson), a.SecondModel,
            a.Disputed, a.DisputeComment, Parse(a.DisputeVerdictJson), a.DisputeModel, a.Overturned)).ToList();
    }

    private static GradingVerdict? Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) {
            return null;
        }
        try {
            return JsonSerializer.Deserialize<GradingVerdict>(json, ReadVerdict);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// Put a passing sentence with the point into its sentence bank: approved when the
    /// grader found nothing else to fix, otherwise a draft to look over (the grader can
    /// miss things). Returns a note, or null when nothing was saved.
    /// </summary>
    private async Task<string?> BankAsync(int pointId, GradingTask task, GradingVerdict verdict)
    {
        if (!verdict.TargetUsedCorrectly) {
            return null;
        }
        bool clean = verdict.Natural && verdict.Errors is not { Count: > 0 };
        (bool added, _) = await _points.AddExampleAsync(pointId, task.Sentence, task.Intent, GrammarExampleSource.Composition,
            clean ? GrammarContentStatus.Approved : GrammarContentStatus.Draft, voice: false);
        return !added ? null
            : clean ? "Saved to your sentences for this point."
            : "Saved to this point's sentences as a draft, since the grader suggested changes: approve or edit it on the point's page.";
    }

    private static string Trim(string text, int max) => text.Length > max ? text[..max] : text;
}
