using KotoNeko.Core.Domain;
using KotoNeko.Core.Srs;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>A recently-missed item shown on the dashboard.</summary>
public record MissedItem(int VocabularyId, string Japanese, string Reading, string Meaning, DateTime ReviewedAt, QuestionType QuestionType);

/// <summary>An upcoming review session: a batch of items that become due at the same time.</summary>
public record UpcomingSession(DateTime AvailableAt, int Count);

/// <summary>A word answered better by sight (meaning/reading) than by ear (listening).</summary>
public record EarGapItem(int VocabularyId, string Japanese, string Reading, string Meaning, int SightPercent, int EarPercent);

/// <summary>A word being studied with no practice sentence listening can use yet.</summary>
public record NeedsSentenceItem(int VocabularyId, string Japanese, string Reading, string Meaning);

/// <summary>Correct answers out of total, as a whole percentage (null when nothing was answered).</summary>
public record Accuracy(int Correct, int Total)
{
    public int? Percent => Total == 0 ? null : (int)Math.Round(100.0 * Correct / Total);
}

/// <summary>Aggregated data for the home dashboard.</summary>
public class DashboardSummary
{
    public int DueNow { get; init; }
    public int LessonsAvailable { get; init; }

    /// <summary>Lessons the daily cap still allows today.</summary>
    public int LessonsLeftToday { get; init; }

    /// <summary>Lessons that can be started now: available, within today's cap.</summary>
    public int LessonsOpenNow => Math.Min(LessonsAvailable, LessonsLeftToday);

    /// <summary>Approved grammar points waiting for their lesson.</summary>
    public int GrammarLessonsAvailable { get; init; }

    /// <summary>Grammar lessons the daily grammar cap still allows today.</summary>
    public int GrammarLessonsLeftToday { get; init; }

    public int GrammarLessonsOpenNow => Math.Min(GrammarLessonsAvailable, GrammarLessonsLeftToday);

    /// <summary>Learned grammar points due for review now.</summary>
    public int GrammarReviewsDue { get; init; }

    /// <summary>Recent accuracy hearing words (listening questions).</summary>
    public Accuracy ByEar { get; init; } = new(0, 0);

    /// <summary>Recent accuracy on meaning, reading and production questions.</summary>
    public Accuracy BySight { get; init; } = new(0, 0);

    /// <summary>Words known by sight but not yet by ear, biggest gap first.</summary>
    public List<EarGapItem> EarGaps { get; init; } = new();
    public int TotalItems { get; init; }
    public int AsleepItems { get; init; }

    /// <summary>Item counts grouped by SRS band (Apprentice, Guru, ...).</summary>
    public Dictionary<string, int> BandCounts { get; init; } = new();

    public List<MissedItem> RecentlyMissed { get; init; } = new();

    /// <summary>The next few upcoming review sessions, soonest first.</summary>
    public List<UpcomingSession> UpcomingSessions { get; init; } = new();

    /// <summary>Words being studied that have no usable practice sentence, due soonest first.</summary>
    public List<NeedsSentenceItem> NeedsSentence { get; init; } = new();

    /// <summary>How many words being studied have no usable practice sentence.</summary>
    public int NeedsSentenceCount { get; init; }
}

public class DashboardService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly PracticeSentenceService _sentences;

    public DashboardService(IDbContextFactory<KotoNekoDbContext> factory, PracticeSentenceService sentences)
    {
        _factory = factory;
        _sentences = sentences;
    }

    public async Task<DashboardSummary> GetSummaryAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();

        // Vocabulary only: grammar points have their own lessons and reviews.
        IQueryable<StudyItem> words = db.StudyItems.Where(i => i.Kind == StudyItemKind.Vocabulary);
        IQueryable<SrsItem> wordSrs = db.SrsItems.Where(s => s.StudyItem!.Kind == StudyItemKind.Vocabulary);

        int total = await words.CountAsync();
        int asleep = await words.CountAsync(i => i.IsAsleep);
        int lessons = await words.CountAsync(i => !i.IsAsleep && i.Srs == null);

        int due = await wordSrs.CountAsync(s =>
            !s.StudyItem!.IsAsleep
            && s.Stage != SrsStage.Locked
            && s.Stage != SrsStage.Burned
            && s.NextReviewAt != null
            && s.NextReviewAt <= now);

        // Counts by SRS stage -> rolled into bands.
        List<StageCount> stageCounts = await wordSrs
            .Where(s => !s.StudyItem!.IsAsleep)
            .GroupBy(s => s.Stage)
            .Select(g => new StageCount(g.Key, g.Count()))
            .ToListAsync();

        Dictionary<string, int> bands = new()
        {
            ["Apprentice"] = 0,
            ["Guru"] = 0,
            ["Master"] = 0,
            ["Enlightened"] = 0,
            ["Burned"] = 0,
        };
        foreach (StageCount sc in stageCounts)
        {
            string band = SrsSchedule.BandName(sc.Stage);
            if (bands.ContainsKey(band))
            {
                bands[band] += sc.Count;
            }
        }

        // Recently missed (distinct latest wrong answers). Vocabulary is the only
        // kind with a display shape so far.
        List<MissedItem> missed = await db.ReviewLogs
            .Where(r => !r.WasCorrect && r.StudyItem is Vocabulary)
            .OrderByDescending(r => r.ReviewedAt)
            .Select(r => new MissedItem(
                r.StudyItemId,
                ((Vocabulary)r.StudyItem!).Japanese,
                ((Vocabulary)r.StudyItem!).Reading,
                ((Vocabulary)r.StudyItem!).Meanings
                    .OrderBy(m => m.SortOrder)
                    .Select(m => m.Text)
                    .FirstOrDefault() ?? string.Empty,
                r.ReviewedAt,
                r.QuestionType))
            .Take(12)
            .ToListAsync();

        // Upcoming sessions: items that become due group into a session by their
        // shared availability time. Take the next few sessions, soonest first.
        List<DateTime> upcoming = await wordSrs
            .Where(s => !s.StudyItem!.IsAsleep
                && s.NextReviewAt != null
                && s.NextReviewAt > now)
            .Select(s => s.NextReviewAt!.Value)
            .ToListAsync();

        List<UpcomingSession> sessions = upcoming
            .GroupBy(d => d)
            .OrderBy(g => g.Key)
            .Take(UpcomingSessionCount)
            .Select(g => new UpcomingSession(DateTime.SpecifyKind(g.Key, DateTimeKind.Utc), g.Count()))
            .ToList();

        LessonAllowance allowance = await ReviewService.GetLessonAllowanceAsync(db, now);
        GrammarAllowance grammarAllowance = await GrammarPointService.GetAllowanceAsync(db, now);
        int grammarLessons = await GrammarPointService.CountLessonsAvailableAsync(db);
        int grammarReviews = await GrammarReviewService.CountDueAsync(db, now);

        DateTime accuracySince = now - AccuracyWindow;
        Accuracy byEar = await AccuracyAsync(db, accuracySince, ListeningTypes);
        Accuracy bySight = await AccuracyAsync(db, accuracySince, SightTypes);
        List<EarGapItem> earGaps = await EarGapsAsync(db, now - EarGapWindow);
        (List<NeedsSentenceItem> needsSentence, int needsSentenceCount) = await NeedsSentenceAsync(db);

        return new DashboardSummary
        {
            DueNow = due,
            LessonsAvailable = lessons,
            LessonsLeftToday = allowance.LeftToday,
            GrammarLessonsAvailable = grammarLessons,
            GrammarLessonsLeftToday = grammarAllowance.LeftToday,
            GrammarReviewsDue = grammarReviews,
            ByEar = byEar,
            BySight = bySight,
            EarGaps = earGaps,
            TotalItems = total,
            AsleepItems = asleep,
            BandCounts = bands,
            RecentlyMissed = missed,
            UpcomingSessions = sessions,
            NeedsSentence = needsSentence,
            NeedsSentenceCount = needsSentenceCount,
        };
    }

    private const int NeedsSentenceShown = 10;

    /// <summary>
    /// Words being studied (awake, past their lesson, not burned) with no practice
    /// sentence a listening question can use: the queue for adding sentences.
    /// </summary>
    private async Task<(List<NeedsSentenceItem> Items, int Count)> NeedsSentenceAsync(KotoNekoDbContext db)
    {
        var studying = await db.Vocabulary.AsNoTracking()
            .Where(v => !v.IsAsleep && v.Srs != null && v.Srs.Stage != SrsStage.Locked && v.Srs.Stage != SrsStage.Burned)
            .OrderBy(v => v.Srs!.NextReviewAt)
            .Select(v => new {
                v.Id, v.Japanese, v.Reading,
                Meaning = v.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).FirstOrDefault(),
            })
            .ToListAsync();

        Dictionary<int, List<CheckedSentence>> sentences = await _sentences.CheckedForAsync(
            studying.Select(v => v.Id).ToList(), inUseOnly: true, ensureReadings: false);
        var lacking = studying
            .Where(v => !(sentences.GetValueOrDefault(v.Id)?.Any(c => c.UsableForListening) ?? false))
            .ToList();
        return (lacking.Take(NeedsSentenceShown)
                .Select(v => new NeedsSentenceItem(v.Id, v.Japanese, v.Reading, v.Meaning ?? string.Empty))
                .ToList(),
            lacking.Count);
    }

    /// <summary>How many upcoming review sessions to surface on the dashboard.</summary>
    private const int UpcomingSessionCount = 8;

    /// <summary>How far back the by-ear and by-sight accuracy tiles look.</summary>
    private static readonly TimeSpan AccuracyWindow = TimeSpan.FromDays(30);

    /// <summary>How far back the sight-versus-sound comparison looks.</summary>
    private static readonly TimeSpan EarGapWindow = TimeSpan.FromDays(90);

    private const int EarGapCount = 12;

    private static readonly QuestionType[] ListeningTypes = { QuestionType.Listening };

    /// <summary>Questions answered from the written word or its meaning.</summary>
    private static readonly QuestionType[] SightTypes =
        { QuestionType.Meaning, QuestionType.Reading, QuestionType.Production };

    private static async Task<Accuracy> AccuracyAsync(KotoNekoDbContext db, DateTime since, QuestionType[] types)
    {
        IQueryable<ReviewLog> logs = db.ReviewLogs
            .Where(r => r.ReviewedAt >= since && types.Contains(r.QuestionType));
        int total = await logs.CountAsync();
        int correct = total == 0 ? 0 : await logs.CountAsync(r => r.WasCorrect);
        return new Accuracy(correct, total);
    }

    /// <summary>
    /// Words with at least one recent listening miss whose by-sight accuracy is
    /// higher than their by-ear accuracy, sorted by the size of that gap.
    /// </summary>
    private static async Task<List<EarGapItem>> EarGapsAsync(KotoNekoDbContext db, DateTime since)
    {
        var perItem = await db.ReviewLogs
            .Where(r => r.ReviewedAt >= since && r.StudyItem is Vocabulary)
            .GroupBy(r => r.StudyItemId)
            .Select(g => new {
                Id = g.Key,
                EarTotal = g.Count(r => r.QuestionType == QuestionType.Listening),
                EarCorrect = g.Count(r => r.QuestionType == QuestionType.Listening && r.WasCorrect),
                SightTotal = g.Count(r => r.QuestionType == QuestionType.Meaning
                    || r.QuestionType == QuestionType.Reading
                    || r.QuestionType == QuestionType.Production),
                SightCorrect = g.Count(r => (r.QuestionType == QuestionType.Meaning
                    || r.QuestionType == QuestionType.Reading
                    || r.QuestionType == QuestionType.Production) && r.WasCorrect),
            })
            .ToListAsync();

        var gaps = perItem
            .Where(s => s.EarTotal > 0 && s.EarCorrect < s.EarTotal && s.SightTotal > 0)
            .Select(s => new {
                s.Id,
                Sight = new Accuracy(s.SightCorrect, s.SightTotal).Percent!.Value,
                Ear = new Accuracy(s.EarCorrect, s.EarTotal).Percent!.Value,
            })
            .Where(s => s.Sight > s.Ear)
            .OrderByDescending(s => s.Sight - s.Ear)
            .Take(EarGapCount)
            .ToList();

        if (gaps.Count == 0) {
            return new List<EarGapItem>();
        }

        List<int> ids = gaps.Select(g => g.Id).ToList();
        Dictionary<int, Vocabulary> words = await db.Vocabulary
            .AsNoTracking()
            .Include(v => v.Meanings)
            .Where(v => ids.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id);

        return gaps
            .Where(g => words.ContainsKey(g.Id))
            .Select(g => {
                Vocabulary v = words[g.Id];
                return new EarGapItem(v.Id, v.Japanese, v.Reading, v.PrimaryMeaning, g.Sight, g.Ear);
            })
            .ToList();
    }

    private readonly record struct StageCount(SrsStage Stage, int Count);
}
