using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Review;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using KotoNeko.Data.Vocab;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>How many new lessons today's cap still allows.</summary>
public sealed record LessonAllowance(int Cap, int DoneToday, int BatchSize)
{
    public int LeftToday => Math.Max(0, Cap - DoneToday);
}

/// <summary>
/// Builds review and lesson sessions from the database and persists their results.
/// </summary>
public class ReviewService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly SentenceReadingService _readings;
    private readonly PracticeSentenceService _sentences;

    public ReviewService(IDbContextFactory<KotoNekoDbContext> factory, SentenceReadingService readings, PracticeSentenceService sentences)
    {
        _factory = factory;
        _readings = readings;
        _sentences = sentences;
    }

    /// <summary>Count of items currently due for review.</summary>
    public async Task<int> CountDueReviewsAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.SrsItems
            .Where(s => !s.StudyItem!.IsAsleep
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
        return await db.StudyItems
            .Where(i => !i.IsAsleep && i.Srs == null)
            .CountAsync();
    }

    /// <summary>
    /// Today's lesson cap and how much of it is used. A lesson counts on the
    /// (local) day its item was unlocked.
    /// </summary>
    public async Task<LessonAllowance> GetLessonAllowanceAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await GetLessonAllowanceAsync(db, now);
    }

    internal static async Task<LessonAllowance> GetLessonAllowanceAsync(KotoNekoDbContext db, DateTime now)
    {
        StudySettings settings = await SettingsService.LoadAsync(db);
        DateTime dayStart = SettingsService.StartOfLocalDay(now);
        int doneToday = await db.SrsItems.CountAsync(s => s.UnlockedAt != null && s.UnlockedAt >= dayStart);
        return new LessonAllowance(settings.DailyLessonCap, doneToday, settings.LessonBatchSize);
    }

    /// <summary>Build a review session of all currently-due items (optionally capped).</summary>
    public async Task<ReviewSession?> BuildReviewSessionAsync(DateTime now, int? max = null)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        StudySettings settings = await SettingsService.LoadAsync(db);

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

        if (max is int cap) {
            query = query.Take(cap);
        }

        List<Vocabulary> items = await query.ToListAsync();
        if (items.Count == 0) {
            return null;
        }

        ILookup<string, Vocabulary> byReading = settings.ListeningEnabled
            ? await LoadHomophonesAsync(db, items)
            : NoHomophones;
        Dictionary<int, ListeningSentence> listening = settings.ListeningEnabled
            ? await ListeningSentencesAsync(items)
            : new Dictionary<int, ListeningSentence>();

        Func<Vocabulary, IReadOnlyList<FuriganaPiece>?> sentences = await ContextSentencesAsync(items);
        List<ReviewCard> cards = items
            .Select(v => BuildCard(v, v.Srs!, SessionKind.Review, settings, byReading, sentences(v), listening.GetValueOrDefault(v.Id)))
            .ToList();
        return new ReviewSession(cards, SessionKind.Review, now, Random.Shared.Next(), PracticeOnlyTypes(settings));
    }

    /// <summary>Build a lesson session of not-yet-learned items (optionally capped).</summary>
    public async Task<ReviewSession?> BuildLessonSessionAsync(DateTime now, int? max = null)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        StudySettings settings = await SettingsService.LoadAsync(db);

        IQueryable<Vocabulary> query = db.Vocabulary
            .Include(v => v.Srs)
            .Include(v => v.Conjugations)
            .Include(v => v.Meanings)
            .Where(v => !v.IsAsleep && v.Srs == null)
            .OrderBy(v => v.CreatedAt);

        if (max is int cap) {
            query = query.Take(cap);
        }

        List<Vocabulary> items = await query.ToListAsync();
        if (items.Count == 0) {
            return null;
        }

        Func<Vocabulary, IReadOnlyList<FuriganaPiece>?> sentences = await ContextSentencesAsync(items);
        List<ReviewCard> cards = items.Select(v => {
            SrsItem srs = v.Srs ?? new SrsItem { StudyItemId = v.Id, Stage = SrsStage.Locked };
            return BuildCard(v, srs, SessionKind.Lesson, settings, NoHomophones, sentences(v), listeningSentence: null);
        }).ToList();

        return new ReviewSession(cards, SessionKind.Lesson, now, Random.Shared.Next(), PracticeOnlyTypes(settings));
    }

    /// <summary>Persist the SRS changes and review logs accumulated by a session.</summary>
    public async Task PersistAsync(ReviewSession.PendingResults results)
    {
        if (results.SrsItems.Count == 0 && results.Logs.Count == 0) {
            return;
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();

        // Set state on the SRS row alone: Add/Update would walk the attached graph
        // and write back the item, its meanings and conjugations as well.
        foreach (SrsItem srs in results.SrsItems) {
            db.Entry(srs).State = srs.Id == 0 ? EntityState.Added : EntityState.Modified;
        }

        foreach (ReviewLog log in results.Logs) {
            db.ReviewLogs.Add(log);
        }

        await db.SaveChangesAsync();
    }

    private static readonly ILookup<string, Vocabulary> NoHomophones =
        Array.Empty<Vocabulary>().ToLookup(v => v.Reading);

    private static IReadOnlySet<QuestionType> PracticeOnlyTypes(StudySettings settings)
    {
        HashSet<QuestionType> types = new(ReviewSession.DefaultPracticeOnlyTypes);
        if (!settings.ListeningCountsTowardSrs) {
            types.Add(QuestionType.Listening);
        }
        return types;
    }

    /// <summary>
    /// Every word sharing a reading with an item that will get a listening
    /// question, keyed by reading, leaving out words held back as unknown (see
    /// <see cref="KnownWords.NotHeldBack"/>). Loaded untracked so it never collides
    /// with the session's tracked items.
    /// </summary>
    private static async Task<ILookup<string, Vocabulary>> LoadHomophonesAsync(
        KotoNekoDbContext db, List<Vocabulary> items)
    {
        List<string> readings = items.Where(v => v.HasWordAudio).Select(v => v.Reading).Distinct().ToList();
        List<Vocabulary> sameSound = await db.Vocabulary
            .AsNoTracking()
            .NotHeldBack()
            .Include(v => v.Meanings)
            .Where(v => readings.Contains(v.Reading))
            .ToListAsync();
        return sameSound.ToLookup(v => v.Reading);
    }

    /// <summary>
    /// For each item that will get a listening question, one practice sentence to
    /// play after the word: in use, voiced, and passing the check (only the word is
    /// unknown, and it's short). Picked at random among those. Only stored readings
    /// are used, so a session never waits on them; an item without such a sentence
    /// keeps the word-only question.
    /// </summary>
    private async Task<Dictionary<int, ListeningSentence>> ListeningSentencesAsync(List<Vocabulary> items)
    {
        List<int> ids = items.Where(v => v.HasWordAudio).Select(v => v.Id).ToList();
        Dictionary<int, List<CheckedSentence>> checkedFor = await _sentences.CheckedForAsync(ids, inUseOnly: true, ensureReadings: false);
        if (checkedFor.Count == 0) {
            return new Dictionary<int, ListeningSentence>();
        }

        HashSet<string> known = await _readings.KnownWordsAsync();
        Dictionary<int, ListeningSentence> result = new();
        foreach ((int id, List<CheckedSentence> list) in checkedFor) {
            List<CheckedSentence> usable = list.Where(c => c.UsableForListening).ToList();
            if (usable.Count > 0) {
                CheckedSentence pick = usable[Random.Shared.Next(usable.Count)];
                result[id] = new ListeningSentence(pick.Sentence.Id, pick.View!.ForReader(known), pick.Sentence.English);
            }
        }
        return result;
    }

    /// <summary>
    /// Stored readings for the items' context sentences, with furigana only over
    /// words the user doesn't know yet. Only reads what is stored: sentences
    /// without readings yet are shown plain rather than holding up the session.
    /// </summary>
    private async Task<Func<Vocabulary, IReadOnlyList<FuriganaPiece>?>> ContextSentencesAsync(List<Vocabulary> items)
    {
        List<string> texts = items
            .Where(v => !string.IsNullOrWhiteSpace(v.ContextSentence))
            .Select(v => v.ContextSentence!)
            .ToList();
        if (texts.Count == 0) {
            return _ => null;
        }

        Dictionary<string, SentenceView> views = await _readings.GetManyAsync(texts);
        HashSet<string> known = await _readings.KnownWordsAsync();
        return v => v.ContextSentence is string text && views.TryGetValue(text, out SentenceView? view)
            ? view.ForReader(known)
            : null;
    }

    private static ReviewCard BuildCard(
        Vocabulary v, SrsItem srs, SessionKind kind, StudySettings settings, ILookup<string, Vocabulary> byReading,
        IReadOnlyList<FuriganaPiece>? contextSentence, ListeningSentence? listeningSentence)
    {
        // Any one of the stored meanings is accepted: the first is the primary,
        // the rest become accepted alternates for the grader.
        List<string> meanings = v.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).ToList();
        string primaryMeaning = meanings.Count > 0 ? meanings[0] : string.Empty;
        string alternateMeanings = meanings.Count > 1 ? string.Join(';', meanings.Skip(1)) : string.Empty;

        string allMeaningsPrompt = string.Join(", ", meanings);

        List<ReviewQuestion> questions = new() {
            new ReviewQuestion() {
                Type = QuestionType.Meaning,
                Prompt = "Meaning",
                ExpectedPrimary = primaryMeaning,
                ExpectedAlternates = alternateMeanings,
            },
            new ReviewQuestion() {
                Type = QuestionType.Production,
                Prompt = allMeaningsPrompt,
                ExpectedPrimary = v.Reading,
                ExpectedAlternates = string.IsNullOrWhiteSpace(v.AlternateReadings) ? null : v.AlternateReadings,
            },
        };

        // The reading is only quizzed for kanji words whose reading we're actually
        // learning. Kana-only words (no kanji) and furigana items skip it.
        if (v.AsksReading) {
            questions.Add(new ReviewQuestion() {
                Type = QuestionType.Reading,
                Prompt = "Reading",
                ExpectedPrimary = v.Reading,
                ExpectedAlternates = v.AlternateReadings,
            });
        }

        if (v.VerbClass != VerbClass.None && v.Conjugations.Count > 0) {
            Conjugation chosen = v.Conjugations[Random.Shared.Next(v.Conjugations.Count)];
            questions.Add(new ReviewQuestion() {
                Type = QuestionType.Conjugation,
                Prompt = ConjugationLabels.Describe(chosen.Form, chosen.Polarity),
                ExpectedPrimary = chosen.ExpectedKana,
                Form = chosen.Form,
                Polarity = chosen.Polarity,
                ConjugationId = chosen.HasAudio ? chosen.Id : null,
            });
        }

        // Hear the word (then a practice sentence, when it has a usable one), type its
        // meaning. Reviews only: a lesson has just shown it. With a sentence, the
        // context decides the meaning, so a homophone's meaning isn't accepted.
        if (kind == SessionKind.Review && settings.ListeningEnabled && v.HasWordAudio) {
            questions.Add(new ReviewQuestion() {
                Type = QuestionType.Listening,
                Prompt = "Listening",
                ExpectedPrimary = primaryMeaning,
                ExpectedAlternates = alternateMeanings,
                Sentence = listeningSentence,
                Homophones = listeningSentence is not null
                    ? Array.Empty<Homophone>()
                    : byReading[v.Reading]
                        .Where(h => h.Id != v.Id)
                        .Select(ToHomophone)
                        .ToList(),
            });
        }

        return new ReviewCard() {
            Item = v,
            Srs = srs,
            Questions = questions,
            ContextSentence = contextSentence,
        };
    }

    private static Homophone ToHomophone(Vocabulary h)
    {
        List<string> meanings = h.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).ToList();
        return new Homophone(
            h.Japanese,
            h.Reading,
            meanings.Count > 0 ? meanings[0] : string.Empty,
            meanings.Count > 1 ? string.Join(';', meanings.Skip(1)) : null);
    }
}
