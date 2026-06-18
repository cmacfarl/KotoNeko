using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>
/// Builds review and lesson sessions from the database and persists their results.
/// </summary>
public class ReviewService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public ReviewService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>Count of items currently due for review.</summary>
    public async Task<int> CountDueReviewsAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.SrsItems
            .Where(s => !s.Vocabulary!.IsAsleep
                && s.Stage != SrsStage.Locked
                && s.Stage != SrsStage.Burned
                && s.NextReviewAt != null
                && s.NextReviewAt <= now)
            .CountAsync();
    }

    /// <summary>Count of items waiting to be learned (no SRS row yet).</summary>
    public async Task<int> CountAvailableLessonsAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.Vocabulary
            .Where(v => !v.IsAsleep && v.Srs == null)
            .CountAsync();
    }

    /// <summary>Build a review session of all currently-due items (optionally capped).</summary>
    public async Task<ReviewSession?> BuildReviewSessionAsync(DateTime now, int? max = null)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();

        IQueryable<Vocabulary> query = db.Vocabulary
            .Include(v => v.Srs)
            .Include(v => v.Conjugations)
            .Include(v => v.Meanings)
            .Where(v => !v.IsAsleep
                && v.Srs != null
                && v.Srs.Stage != SrsStage.Locked
                && v.Srs.Stage != SrsStage.Burned
                && v.Srs.NextReviewAt != null
                && v.Srs.NextReviewAt <= now)
            .OrderBy(v => v.Srs!.NextReviewAt);

        if (max is int cap)
        {
            query = query.Take(cap);
        }

        List<Vocabulary> items = await query.ToListAsync();
        if (items.Count == 0)
        {
            return null;
        }

        List<ReviewCard> cards = items.Select(v => BuildCard(v, v.Srs!)).ToList();
        return new ReviewSession(cards, SessionKind.Review, now, Random.Shared.Next());
    }

    /// <summary>Build a lesson session of not-yet-learned items (optionally capped).</summary>
    public async Task<ReviewSession?> BuildLessonSessionAsync(DateTime now, int? max = null)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();

        IQueryable<Vocabulary> query = db.Vocabulary
            .Include(v => v.Srs)
            .Include(v => v.Conjugations)
            .Include(v => v.Meanings)
            .Where(v => !v.IsAsleep && v.Srs == null)
            .OrderBy(v => v.CreatedAt);

        if (max is int cap)
        {
            query = query.Take(cap);
        }

        List<Vocabulary> items = await query.ToListAsync();
        if (items.Count == 0)
        {
            return null;
        }

        List<ReviewCard> cards = items.Select(v =>
        {
            SrsItem srs = v.Srs ?? new SrsItem { VocabularyId = v.Id, Stage = SrsStage.Locked };
            return BuildCard(v, srs);
        }).ToList();

        return new ReviewSession(cards, SessionKind.Lesson, now, Random.Shared.Next());
    }

    /// <summary>Persist the SRS changes and review logs accumulated by a session.</summary>
    public async Task PersistAsync(ReviewSession.PendingResults results)
    {
        if (results.SrsItems.Count == 0 && results.Logs.Count == 0)
        {
            return;
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();

        foreach (SrsItem srs in results.SrsItems)
        {
            if (srs.Id == 0)
            {
                db.SrsItems.Add(srs);
            }
            else
            {
                db.SrsItems.Update(srs);
            }
        }

        foreach (ReviewLog log in results.Logs)
        {
            db.ReviewLogs.Add(log);
        }

        await db.SaveChangesAsync();
    }

    private static ReviewCard BuildCard(Vocabulary v, SrsItem srs)
    {
        // Any one of the stored meanings is accepted: the first is the primary,
        // the rest become accepted alternates for the grader.
        List<string> meanings = v.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).ToList();
        string primaryMeaning = meanings.Count > 0 ? meanings[0] : string.Empty;
        string alternateMeanings = meanings.Count > 1 ? string.Join(';', meanings.Skip(1)) : string.Empty;

        List<ReviewQuestion> questions = new()
        {
            new ReviewQuestion
            {
                Type = QuestionType.Meaning,
                Prompt = "Meaning",
                ExpectedPrimary = primaryMeaning,
                ExpectedAlternates = alternateMeanings,
            },
        };

        // The reading is only quizzed for kanji words whose reading we're actually
        // learning. Kana-only words (no kanji) and furigana items skip it.
        if (v.AsksReading)
        {
            questions.Add(new ReviewQuestion
            {
                Type = QuestionType.Reading,
                Prompt = "Reading",
                ExpectedPrimary = v.Reading,
                ExpectedAlternates = v.AlternateReadings,
            });
        }

        if (v.VerbClass != VerbClass.None && v.Conjugations.Count > 0)
        {
            Conjugation chosen = v.Conjugations[Random.Shared.Next(v.Conjugations.Count)];
            questions.Add(new ReviewQuestion
            {
                Type = QuestionType.Conjugation,
                Prompt = ConjugationLabels.Describe(chosen.Form, chosen.Polarity),
                ExpectedPrimary = chosen.ExpectedKana,
                Form = chosen.Form,
                Polarity = chosen.Polarity,
            });
        }

        return new ReviewCard
        {
            Vocabulary = v,
            Srs = srs,
            Questions = questions,
        };
    }
}
