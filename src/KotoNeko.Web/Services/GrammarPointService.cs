using KotoNeko.Core.Domain;
using KotoNeko.Core.Srs;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>Where a grammar list entry is with its lesson content, for the grammar list.</summary>
public sealed record GrammarPointSummary(int PointId, GrammarContentStatus Status, bool Learned, int ApprovedExamples);

/// <summary>An example with its stored readings and the words in it the user doesn't know yet.</summary>
public sealed record GrammarExampleView(GrammarExample Example, SentenceView? View, IReadOnlyList<UnknownWord> Unknown)
{
    public string AudioUrl => $"/api/grammar-example/{Example.Id}/audio";
}

/// <summary>How many new grammar lessons today's cap still allows.</summary>
public sealed record GrammarAllowance(int Cap, int DoneToday)
{
    public int LeftToday => Math.Max(0, Cap - DoneToday);
}

/// <summary>
/// Grammar points as study items: writing and approving their lesson content (by
/// hand or from a draft), their examples and audio, and the lessons that put them
/// into the SRS. Grammar reviews arrive in Phase 5.
/// </summary>
public sealed class GrammarPointService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly SentenceReadingService _readings;
    private readonly JapaneseTtsService _tts;
    private readonly ILogger<GrammarPointService> _logger;

    public GrammarPointService(
        IDbContextFactory<KotoNekoDbContext> factory, SentenceReadingService readings, JapaneseTtsService tts,
        ILogger<GrammarPointService> logger)
    {
        _factory = factory;
        _readings = readings;
        _tts = tts;
        _logger = logger;
    }

    /// <summary>The lesson content of these list entries that have any, keyed by entry id.</summary>
    public async Task<Dictionary<int, GrammarPointSummary>> SummariesAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.GrammarPoints.AsNoTracking()
            .Select(p => new {
                p.GrammarListEntryId,
                Summary = new GrammarPointSummary(p.Id, p.Status, p.Srs != null,
                    p.Examples.Count(x => x.Status == GrammarContentStatus.Approved)),
            })
            .ToDictionaryAsync(p => p.GrammarListEntryId, p => p.Summary);
    }

    /// <summary>A point with its entry, SRS state and examples (in order).</summary>
    public async Task<GrammarPoint?> GetAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarPoint? point = await db.GrammarPoints.AsNoTracking()
            .Include(p => p.Entry)
            .Include(p => p.Srs)
            .Include(p => p.Examples)
            .FirstOrDefaultAsync(p => p.Id == id);
        point?.Examples.Sort((a, b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : a.Id.CompareTo(b.Id));
        return point;
    }

    /// <summary>The point teaching a list entry, creating an empty draft if there isn't one. Null when the entry is gone.</summary>
    public async Task<int?> EnsureForEntryAsync(int entryId)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.GrammarPoints.Where(p => p.GrammarListEntryId == entryId).Select(p => (int?)p.Id).FirstOrDefaultAsync() is int id) {
            return id;
        }
        if (!await db.GrammarList.AnyAsync(e => e.Id == entryId)) {
            return null;
        }
        DateTime now = DateTime.UtcNow;
        GrammarPoint point = new() { GrammarListEntryId = entryId, Status = GrammarContentStatus.Draft, CreatedAt = now, UpdatedAt = now };
        db.GrammarPoints.Add(point);
        await db.SaveChangesAsync();
        return point.Id;
    }

    /// <summary>
    /// Save a point's content. Blank rows are dropped. Returns an error, or null when
    /// saved. Saving never approves; an approved point stays approved if it still can be.
    /// </summary>
    public async Task<string?> SaveContentAsync(
        int id, string explanation, string formation, GrammarFormality formality,
        IEnumerable<GrammarContrast> contrasts, IEnumerable<GrammarMistake> mistakes, IEnumerable<GrammarSituation> situations,
        string? draftedBy = null)
    {
        explanation = explanation.Trim().Replace("\r\n", "\n");
        formation = GrammarContent.JoinFormation(GrammarContent.FormationRules(formation));
        if (explanation.Length > GrammarPoint.MaxExplanationLength) {
            return $"Keep the explanation under {GrammarPoint.MaxExplanationLength} characters.";
        }
        if (formation.Length > GrammarPoint.MaxFormationLength) {
            return $"Keep \"how it attaches\" under {GrammarPoint.MaxFormationLength} characters.";
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarPoint? point = await db.GrammarPoints.Include(p => p.Examples).FirstOrDefaultAsync(p => p.Id == id);
        if (point is null) {
            return "That grammar point no longer exists.";
        }
        point.Explanation = explanation;
        point.Formation = formation;
        point.Formality = formality;
        point.Contrasts = GrammarContent.Clean(contrasts);
        point.Mistakes = GrammarContent.Clean(mistakes);
        point.Situations = GrammarContent.Clean(situations);
        if (draftedBy is not null) {
            point.DraftedBy = draftedBy.Length > GrammarPoint.MaxDraftedByLength ? draftedBy[..GrammarPoint.MaxDraftedByLength] : draftedBy;
        }
        if (point.Status == GrammarContentStatus.Approved && GrammarContent.ApprovalProblems(point).Count > 0) {
            point.Status = GrammarContentStatus.Draft;
        }
        point.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Approve a point (lessons may teach it) or send it back to draft. Returns what's missing, or null.</summary>
    public async Task<string?> SetStatusAsync(int id, GrammarContentStatus status)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarPoint? point = await db.GrammarPoints.Include(p => p.Examples).FirstOrDefaultAsync(p => p.Id == id);
        if (point is null) {
            return "That grammar point no longer exists.";
        }
        if (status == GrammarContentStatus.Approved && GrammarContent.ApprovalProblems(point) is { Count: > 0 } problems) {
            return "Not ready to approve: " + string.Join(" ", problems);
        }
        point.Status = status;
        point.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Delete a point that hasn't been learned. Returns an error, or null.</summary>
    public async Task<string?> DeleteAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarPoint? point = await db.GrammarPoints.Include(p => p.Srs).FirstOrDefaultAsync(p => p.Id == id);
        if (point is null) {
            return null;
        }
        if (point.Srs is not null) {
            return "You've already learned this point, so it can't be deleted. Edit it instead.";
        }
        db.GrammarPoints.Remove(point);
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>A point's examples with their readings worked out, and the words the user doesn't know yet.</summary>
    public async Task<List<GrammarExampleView>> ExampleViewsAsync(IReadOnlyList<GrammarExample> examples)
    {
        List<string> texts = examples.Select(x => x.Text).ToList();
        await _readings.EnsureAsync(texts);
        Dictionary<string, SentenceView> views = await _readings.GetManyAsync(texts);
        Func<string, bool> isKnown;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            isKnown = await PracticeSentenceService.PoolAsync(db);
        }
        return examples.Select(x => {
            SentenceView? view = views.GetValueOrDefault(x.Text);
            // A grammar example has no single target word: every word is checked.
            IReadOnlyList<UnknownWord> unknown = view is null
                ? Array.Empty<UnknownWord>()
                : PracticeSentenceService.Check(view, string.Empty, isKnown).Unknown;
            return new GrammarExampleView(x, view, unknown);
        }).ToList();
    }

    /// <summary>Furigana for a lesson: only over words the user doesn't know yet.</summary>
    public async Task<HashSet<string>> KnownWordsAsync() => await _readings.KnownWordsAsync();

    /// <summary>
    /// Add an example. Drafts wait for approval; the user's own are approved and
    /// voiced. Returns an error, or a note when its audio couldn't be made.
    /// </summary>
    public async Task<(bool Added, string? Message)> AddExampleAsync(
        int pointId, string text, string english, GrammarExampleSource source, GrammarContentStatus status, bool voice = true)
    {
        text = GrammarContent.CleanSentence(text);
        english = english.Trim();
        if (text.Length == 0) {
            return (false, "Write the example in Japanese first.");
        }
        if (text.Length > GrammarExample.MaxTextLength || english.Length > GrammarExample.MaxEnglishLength) {
            return (false, $"Keep the example under {GrammarExample.MaxTextLength} characters and its English under {GrammarExample.MaxEnglishLength}.");
        }

        int id;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            if (!await db.GrammarPoints.AnyAsync(p => p.Id == pointId)) {
                return (false, "That grammar point no longer exists.");
            }
            if (await db.GrammarExamples.AnyAsync(x => x.GrammarPointId == pointId && x.Text == text)) {
                return (false, "This point already has that example.");
            }
            int last = await db.GrammarExamples.Where(x => x.GrammarPointId == pointId).MaxAsync(x => (int?)x.SortOrder) ?? 0;
            GrammarExample example = new() {
                GrammarPointId = pointId, Text = text, English = english, Source = source, Status = status,
                SortOrder = last + 1, CreatedAt = DateTime.UtcNow,
            };
            db.GrammarExamples.Add(example);
            await db.SaveChangesAsync();
            id = example.Id;
        }
        _readings.RequestBackfill();
        return (true, voice && status == GrammarContentStatus.Approved ? await GenerateExampleAudioAsync(id) : null);
    }

    /// <summary>Change an example's text or English. New text loses its old audio. Returns an error, or null.</summary>
    public async Task<string?> UpdateExampleAsync(int id, string text, string english)
    {
        text = GrammarContent.CleanSentence(text);
        english = english.Trim();
        if (text.Length == 0 || text.Length > GrammarExample.MaxTextLength || english.Length > GrammarExample.MaxEnglishLength) {
            return $"An example needs Japanese, under {GrammarExample.MaxTextLength} characters, and English under {GrammarExample.MaxEnglishLength}.";
        }
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarExample? example = await db.GrammarExamples.Include(x => x.Audio).FirstOrDefaultAsync(x => x.Id == id);
        if (example is null) {
            return "That example no longer exists.";
        }
        if (example.Text != text) {
            example.Text = text;
            example.HasAudio = false;
            if (example.Audio is not null) {
                db.GrammarExampleAudios.Remove(example.Audio);
            }
        }
        example.English = english;
        await db.SaveChangesAsync();
        _readings.RequestBackfill();
        return null;
    }

    /// <summary>Approve an example (voicing it if it has no audio) or send it back to draft. Returns a note, or null.</summary>
    public async Task<string?> SetExampleStatusAsync(int id, GrammarContentStatus status)
    {
        bool needsAudio;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            GrammarExample? example = await db.GrammarExamples.FindAsync(id);
            if (example is null) {
                return null;
            }
            example.Status = status;
            await db.SaveChangesAsync();
            needsAudio = status == GrammarContentStatus.Approved && !example.HasAudio;

            // An approved point needs an approved example; withdrawing the last sends the point back to draft.
            GrammarPoint point = await db.GrammarPoints.Include(p => p.Examples).FirstAsync(p => p.Id == example.GrammarPointId);
            if (point.Status == GrammarContentStatus.Approved && GrammarContent.ApprovalProblems(point).Count > 0) {
                point.Status = GrammarContentStatus.Draft;
                await db.SaveChangesAsync();
            }
        }
        return needsAudio ? await GenerateExampleAudioAsync(id) : null;
    }

    public async Task DeleteExampleAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.GrammarExamples.FindAsync(id) is { } example) {
            db.GrammarExamples.Remove(example);
            await db.SaveChangesAsync();
            GrammarPoint point = await db.GrammarPoints.Include(p => p.Examples).FirstAsync(p => p.Id == example.GrammarPointId);
            if (point.Status == GrammarContentStatus.Approved && GrammarContent.ApprovalProblems(point).Count > 0) {
                point.Status = GrammarContentStatus.Draft;
                await db.SaveChangesAsync();
            }
        }
    }

    /// <summary>Voice an example at the default speed. Returns a note when it fails.</summary>
    public async Task<string?> GenerateExampleAudioAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarExample? example = await db.GrammarExamples.Include(x => x.Audio).FirstOrDefaultAsync(x => x.Id == id);
        if (example is null) {
            return null;
        }
        try {
            SpeechSpeed speed = (await SettingsService.LoadAsync(db)).SpeechSpeed;
            byte[] clip = (await _tts.SynthesiseAsync(example.Text, speed)).Audio;
            if (example.Audio is null) {
                example.Audio = new GrammarExampleAudio { Audio = clip };
            } else {
                example.Audio.Audio = clip;
            }
            example.HasAudio = clip.Length > 0;
            await db.SaveChangesAsync();
            return null;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Couldn't generate audio for a grammar example.");
            return $"The example was saved, but its audio couldn't be generated ({ex.Message}). Try ▶ Generate audio later.";
        }
    }

    public async Task<byte[]?> GetExampleAudioAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.GrammarExampleAudios.AsNoTracking().Where(a => a.GrammarExampleId == id)
            .Select(a => a.Audio).FirstOrDefaultAsync();
    }

    /// <summary>Today's grammar lesson cap and how much of it is used (by the local day each point was learned).</summary>
    public async Task<GrammarAllowance> GetAllowanceAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await GetAllowanceAsync(db, now);
    }

    internal static async Task<GrammarAllowance> GetAllowanceAsync(KotoNekoDbContext db, DateTime now)
    {
        StudySettings settings = await SettingsService.LoadAsync(db);
        DateTime dayStart = SettingsService.StartOfLocalDay(now);
        int doneToday = await db.SrsItems.CountAsync(s =>
            s.StudyItem!.Kind == StudyItemKind.Grammar && s.UnlockedAt != null && s.UnlockedAt >= dayStart);
        return new GrammarAllowance(settings.DailyGrammarCap, doneToday);
    }

    /// <summary>Approved points not learned yet: the lesson queue, easiest level first, in study order.</summary>
    private static IQueryable<GrammarPoint> LessonQueue(KotoNekoDbContext db) =>
        db.GrammarPoints.Where(p => p.Status == GrammarContentStatus.Approved && !p.IsAsleep && p.Srs == null && !p.Entry!.IsPruned)
            .OrderByDescending(p => p.Entry!.Level).ThenBy(p => p.Entry!.SortOrder).ThenBy(p => p.Id);

    internal static Task<int> CountLessonsAvailableAsync(KotoNekoDbContext db) => LessonQueue(db).CountAsync();

    /// <summary>The next point to learn, or null when none is approved and waiting.</summary>
    public async Task<int?> NextLessonAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await LessonQueue(db).Select(p => (int?)p.Id).FirstOrDefaultAsync();
    }

    /// <summary>
    /// Finish a point's lesson: it enters the SRS at Apprentice I (its reviews come
    /// with Phase 5) and its list entry becomes Studying. Returns an error, or null.
    /// </summary>
    public async Task<string?> CompleteLessonAsync(int id, DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarPoint? point = await db.GrammarPoints.Include(p => p.Srs).Include(p => p.Entry).FirstOrDefaultAsync(p => p.Id == id);
        if (point is null) {
            return "That grammar point no longer exists.";
        }
        if (point.Srs is not null) {
            return null;
        }
        SrsItem srs = new() { StudyItemId = point.Id };
        SrsEngine.Unlock(srs, now);
        db.SrsItems.Add(srs);
        if (point.Entry is { Status: GrammarStatus.NotYet } entry) {
            entry.Status = GrammarStatus.Studying;
        }
        await db.SaveChangesAsync();
        return null;
    }
}
