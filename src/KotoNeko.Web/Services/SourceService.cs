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
}
