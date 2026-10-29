using KotoNeko.Core.Domain;
using KotoNeko.Data;
using KotoNeko.Data.Grammar;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>A known grammar point, as sentence generation is told about it.</summary>
public sealed record KnownGrammarPoint(string Pattern, string Meaning, JlptLevel Level);

/// <summary>
/// The user's grammar list: seeding it from the drafted list, pruning it, setting
/// what's known, and adding their own points.
/// </summary>
public sealed class GrammarService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public GrammarService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>The entries matching the filters, by level (easiest first) then study order.</summary>
    public async Task<List<GrammarListEntry>> ListAsync(JlptLevel? level, GrammarStatus? status, bool pruned, string? search)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        IQueryable<GrammarListEntry> query = db.GrammarList.AsNoTracking().Where(e => e.IsPruned == pruned);
        if (level is not null) {
            query = query.Where(e => e.Level == level);
        }
        if (status is not null) {
            query = query.Where(e => e.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(search)) {
            string term = search.Trim();
            query = query.Where(e => e.Pattern.Contains(term) || e.Meaning.Contains(term));
        }
        return await query.OrderByDescending(e => e.Level).ThenBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync();
    }

    /// <summary>Entry counts by level and status, pruned entries left out.</summary>
    public async Task<Dictionary<(JlptLevel Level, GrammarStatus Status), int>> CountsAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.GrammarList.AsNoTracking()
            .Where(e => !e.IsPruned)
            .GroupBy(e => new { e.Level, e.Status })
            .Select(g => new { g.Key.Level, g.Key.Status, Count = g.Count() })
            .ToDictionaryAsync(g => (g.Level, g.Status), g => g.Count);
    }

    /// <summary>How many drafted points aren't on the list yet (pruned ones count as on it).</summary>
    public async Task<int> DraftedNotAddedAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<string?> keys = await db.GrammarList.Select(e => e.SeedKey).ToListAsync();
        return GrammarList.Plan(GrammarList.Drafted(), keys, DateTime.UtcNow).Count;
    }

    /// <summary>Add the drafted points not yet on the list. Returns how many were added.</summary>
    public async Task<int> SeedAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await GrammarList.SeedAsync(db, DateTime.UtcNow);
    }

    public async Task SetStatusAsync(IReadOnlyCollection<int> ids, GrammarStatus status)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        foreach (int[] chunk in ids.Chunk(1000)) {
            foreach (GrammarListEntry e in await db.GrammarList.Where(e => chunk.Contains(e.Id)).ToListAsync()) {
                e.Status = status;
            }
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Prune an entry (hide it, and keep seeding from adding it back), or restore it.</summary>
    public async Task SetPrunedAsync(int id, bool pruned)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.GrammarList.FindAsync(id) is { } entry) {
            entry.IsPruned = pruned;
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Add a point of the user's own (Id 0) or change one's pattern, meaning and
    /// level. Returns an error, or null when saved.
    /// </summary>
    public async Task<string?> SaveAsync(int id, string pattern, string meaning, JlptLevel level)
    {
        pattern = pattern.Trim();
        meaning = meaning.Trim();
        if (pattern.Length == 0 || meaning.Length == 0) {
            return "A grammar point needs a pattern and a meaning.";
        }
        if (pattern.Length > GrammarListEntry.MaxPatternLength || meaning.Length > GrammarListEntry.MaxMeaningLength) {
            return $"Keep the pattern under {GrammarListEntry.MaxPatternLength} characters and the meaning under {GrammarListEntry.MaxMeaningLength}.";
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        GrammarListEntry? entry;
        if (id == 0) {
            int last = await db.GrammarList.Where(e => e.Level == level).MaxAsync(e => (int?)e.SortOrder) ?? 0;
            entry = new GrammarListEntry { Status = GrammarStatus.NotYet, SortOrder = last + 1, CreatedAt = DateTime.UtcNow };
            db.GrammarList.Add(entry);
        } else if ((entry = await db.GrammarList.FindAsync(id)) is null) {
            return "That grammar point no longer exists.";
        }
        entry.Pattern = pattern;
        entry.Meaning = meaning;
        entry.Level = level;
        await db.SaveChangesAsync();
        return null;
    }

    /// <summary>The Known points (not pruned), for keeping generated sentences to grammar the user knows.</summary>
    public async Task<List<KnownGrammarPoint>> KnownAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.GrammarList.AsNoTracking()
            .Where(e => !e.IsPruned && e.Status == GrammarStatus.Known)
            .OrderByDescending(e => e.Level).ThenBy(e => e.SortOrder)
            .Select(e => new KnownGrammarPoint(e.Pattern, e.Meaning, e.Level))
            .ToListAsync();
    }
}
