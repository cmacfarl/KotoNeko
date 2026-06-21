using KotoNeko.Core.Domain;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>CRUD for source materials (the vocabulary "source" dropdown).</summary>
public class SourceService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public SourceService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<SourceMaterial>> GetAllAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.SourceMaterials
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<SourceMaterial?> GetAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.SourceMaterials.FindAsync(id);
    }

    public async Task<SourceMaterial> CreateAsync(string name, SourceKind kind, string? notes)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        SourceMaterial source = new()
        {
            Name = name.Trim(),
            Kind = kind,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            CreatedAt = DateTime.UtcNow,
        };
        db.SourceMaterials.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    public async Task UpdateAsync(SourceMaterial source)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        db.SourceMaterials.Update(source);
        await db.SaveChangesAsync();
    }

    public async Task DeleteAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        SourceMaterial? source = await db.SourceMaterials.FindAsync(id);
        if (source is not null)
        {
            db.SourceMaterials.Remove(source);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Set IsAsleep on every vocabulary item belonging to <paramref name="sourceId"/>.
    /// </summary>
    public async Task SetSourceAsleepAsync(int sourceId, bool asleep)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        await db.Vocabulary
            .Where(v => v.SourceMaterialId == sourceId)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.IsAsleep, asleep));
    }

    /// <summary>
    /// For each source id, returns true when ALL vocabulary in that source is asleep
    /// (and the source has at least one vocabulary item).
    /// </summary>
    public async Task<Dictionary<int, bool>> GetSourceSleepStatusAsync(IReadOnlyList<int> sourceIds)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        var groups = await db.Vocabulary
            .Where(v => v.SourceMaterialId.HasValue && sourceIds.Contains(v.SourceMaterialId.Value))
            .GroupBy(v => v.SourceMaterialId!.Value)
            .Select(g => new { SourceId = g.Key, Total = g.Count(), Asleep = g.Count(v => v.IsAsleep) })
            .ToListAsync();

        return groups.ToDictionary(
            g => g.SourceId,
            g => g.Total > 0 && g.Total == g.Asleep);
    }
}
