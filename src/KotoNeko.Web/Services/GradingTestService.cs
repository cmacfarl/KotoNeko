using KotoNeko.Core.Domain;
using KotoNeko.Data;
using KotoNeko.Data.Grammar;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>Progress on one grammar point's test sentences.</summary>
public sealed record GradingTestPoint(
    string GrammarKey, string Pattern, string Meaning, int Total, int ByPerson, int AiAgreed, int NeedPerson, int Excluded)
{
    public int NotYet => Total - ByPerson - AiAgreed - NeedPerson - Excluded;
}

/// <summary>Which test sentences the page lists.</summary>
public enum GradingTestFilter
{
    NeedsPerson,
    NotLabelled,
    AiAgreed,
    ByPerson,
    All,
}

/// <summary>
/// The grading test set: seeding the drafted candidates, labelling them, and adding
/// sentences of the user's own. The eval (tests/KotoNeko.Evals) scores a grader
/// against these labels.
/// </summary>
public sealed class GradingTestService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public GradingTestService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<GradingTestPoint>> PointsAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        var rows = await db.GradingTestItems.AsNoTracking()
            .Select(i => new { i.GrammarKey, i.Pattern, i.Meaning, i.Label, i.LabelSource, i.NeedsPerson, i.Excluded })
            .ToListAsync();
        return rows.GroupBy(r => r.GrammarKey)
            .Select(g => new GradingTestPoint(g.Key, g.First().Pattern, g.First().Meaning, g.Count(),
                g.Count(r => !r.Excluded && r.Label != null && r.LabelSource == LabelSource.Person),
                g.Count(r => !r.Excluded && r.Label != null && r.LabelSource == LabelSource.AiAgreed),
                g.Count(r => !r.Excluded && r.Label == null && r.NeedsPerson),
                g.Count(r => r.Excluded)))
            .OrderBy(p => p.Pattern, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The items in labelling order (a fixed shuffle). <paramref name="grammarKey"/> null means all points.</summary>
    public async Task<List<GradingTestItem>> ListAsync(string? grammarKey, GradingTestFilter filter)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        IQueryable<GradingTestItem> query = db.GradingTestItems.AsNoTracking();
        if (grammarKey is not null) {
            query = query.Where(i => i.GrammarKey == grammarKey);
        }
        query = filter switch {
            GradingTestFilter.NeedsPerson => query.Where(i => !i.Excluded && i.Label == null && i.NeedsPerson),
            GradingTestFilter.NotLabelled => query.Where(i => !i.Excluded && i.Label == null),
            GradingTestFilter.AiAgreed => query.Where(i => !i.Excluded && i.Label != null && i.LabelSource == LabelSource.AiAgreed),
            GradingTestFilter.ByPerson => query.Where(i => i.Excluded || (i.Label != null && i.LabelSource == LabelSource.Person)),
            _ => query,
        };
        return await query.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToListAsync();
    }

    public async Task<int> CandidatesNotAddedAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<string?> keys = await db.GradingTestItems.Select(i => i.SeedKey).ToListAsync();
        return GradingTestSet.Plan(GradingTestSet.Candidates(), GrammarList.Drafted(), keys, DateTime.UtcNow).Count;
    }

    public async Task<int> SeedAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await GradingTestSet.SeedAsync(db, DateTime.UtcNow);
    }

    /// <summary>
    /// A person labels an item (or marks it not useful): it becomes person-labelled,
    /// and pre-labelling never changes it again.
    /// </summary>
    public async Task SetAsync(int id, GradingLabel? label, bool excluded, string? note)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.GradingTestItems.FindAsync(id) is not { } item) {
            return;
        }
        item.Label = excluded ? null : label;
        item.LabelSource = LabelSource.Person;
        item.NeedsPerson = item.NeedsPerson && label is null && !excluded;
        item.Excluded = excluded;
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        item.Note = note is { Length: > GradingTestItem.MaxNoteLength } ? note[..GradingTestItem.MaxNoteLength] : note;
        item.LabelledAt = label is null && !excluded ? null : DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Add a sentence of the user's own (from a tutor, say) for a grammar point already in the set.</summary>
    public async Task<string?> AddAsync(string grammarKey, string task, string sentence)
    {
        task = task.Trim();
        sentence = sentence.Trim();
        if (sentence.Length == 0) {
            return "Type the sentence first.";
        }
        if (sentence.Length > GradingTestItem.MaxSentenceLength || task.Length > GradingTestItem.MaxTaskLength) {
            return "That's too long for a test sentence.";
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GradingTestItem? sample = await db.GradingTestItems.AsNoTracking().FirstOrDefaultAsync(i => i.GrammarKey == grammarKey);
        if (sample is null) {
            return "Pick one of the grammar points in the test set.";
        }
        db.GradingTestItems.Add(new GradingTestItem {
            GrammarKey = grammarKey,
            Pattern = sample.Pattern,
            Meaning = sample.Meaning,
            Task = task,
            Sentence = sentence,
            SortOrder = GradingTestSet.ShuffledOrder(Guid.NewGuid().ToString()),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>Change only the note, keeping the label and who gave it.</summary>
    public async Task SetNoteAsync(int id, string? note)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.GradingTestItems.FindAsync(id) is { } item) {
            note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
            item.Note = note is { Length: > GradingTestItem.MaxNoteLength } ? note[..GradingTestItem.MaxNoteLength] : note;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>The pre-labelling votes on an item: shown only when asked for.</summary>
    public async Task<List<GradingPrelabel>> VotesAsync(int itemId)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.GradingPrelabels.AsNoTracking().Where(v => v.ItemId == itemId).OrderBy(v => v.Id).ToListAsync();
    }

    /// <summary>Eval runs, newest first (summaries only; per-item verdicts stay in the eval's report).</summary>
    public async Task<List<GradingEvalRun>> RunsAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.GradingEvalRuns.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(20).ToListAsync();
    }
}
