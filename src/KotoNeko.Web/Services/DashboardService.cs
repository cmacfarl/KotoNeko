using KotoNeko.Core.Domain;
using KotoNeko.Core.Srs;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>A recently-missed item shown on the dashboard.</summary>
public record MissedItem(int VocabularyId, string Japanese, string Reading, string Meaning, DateTime ReviewedAt, QuestionType QuestionType);

/// <summary>An upcoming review session: a batch of items that become due at the same time.</summary>
public record UpcomingSession(DateTime AvailableAt, int Count);

/// <summary>Aggregated data for the home dashboard.</summary>
public class DashboardSummary
{
    public int DueNow { get; init; }
    public int LessonsAvailable { get; init; }
    public int TotalItems { get; init; }
    public int AsleepItems { get; init; }

    /// <summary>Item counts grouped by SRS band (Apprentice, Guru, ...).</summary>
    public Dictionary<string, int> BandCounts { get; init; } = new();

    public List<MissedItem> RecentlyMissed { get; init; } = new();

    /// <summary>The next few upcoming review sessions, soonest first.</summary>
    public List<UpcomingSession> UpcomingSessions { get; init; } = new();
}

public class DashboardService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public DashboardService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DashboardSummary> GetSummaryAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();

        int total = await db.Vocabulary.CountAsync();
        int asleep = await db.Vocabulary.CountAsync(v => v.IsAsleep);
        int lessons = await db.Vocabulary.CountAsync(v => !v.IsAsleep && v.Srs == null);

        int due = await db.SrsItems.CountAsync(s =>
            !s.Vocabulary!.IsAsleep
            && s.Stage != SrsStage.Locked
            && s.Stage != SrsStage.Burned
            && s.NextReviewAt != null
            && s.NextReviewAt <= now);

        // Counts by SRS stage -> rolled into bands.
        List<StageCount> stageCounts = await db.SrsItems
            .Where(s => !s.Vocabulary!.IsAsleep)
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

        // Recently missed (distinct latest wrong answers).
        List<MissedItem> missed = await db.ReviewLogs
            .Where(r => !r.WasCorrect)
            .OrderByDescending(r => r.ReviewedAt)
            .Select(r => new MissedItem(
                r.VocabularyId,
                r.Vocabulary!.Japanese,
                r.Vocabulary.Reading,
                r.Vocabulary.Meanings
                    .OrderBy(m => m.SortOrder)
                    .Select(m => m.Text)
                    .FirstOrDefault() ?? string.Empty,
                r.ReviewedAt,
                r.QuestionType))
            .Take(12)
            .ToListAsync();

        // Upcoming sessions: items that become due group into a session by their
        // shared availability time. Take the next few sessions, soonest first.
        List<DateTime> upcoming = await db.SrsItems
            .Where(s => !s.Vocabulary!.IsAsleep
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

        return new DashboardSummary
        {
            DueNow = due,
            LessonsAvailable = lessons,
            TotalItems = total,
            AsleepItems = asleep,
            BandCounts = bands,
            RecentlyMissed = missed,
            UpcomingSessions = sessions,
        };
    }

    /// <summary>How many upcoming review sessions to surface on the dashboard.</summary>
    private const int UpcomingSessionCount = 8;

    private readonly record struct StageCount(SrsStage Stage, int Count);
}
