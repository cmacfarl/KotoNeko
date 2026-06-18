using KotoNeko.Core.Conjugation;
using KotoNeko.Core.Domain;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>CRUD and conjugation management for vocabulary items.</summary>
public class VocabularyService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public VocabularyService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<List<Vocabulary>> GetAllAsync(bool includeAsleep = true, string? search = null)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        IQueryable<Vocabulary> query = db.Vocabulary
            .Include(v => v.SourceMaterial)
            .Include(v => v.Srs)
            .Include(v => v.Meanings)
            .AsNoTracking();

        if (!includeAsleep)
        {
            query = query.Where(v => !v.IsAsleep);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim();
            query = query.Where(v =>
                v.Japanese.Contains(term) ||
                v.Reading.Contains(term) ||
                v.Meanings.Any(m => m.Text.Contains(term)));
        }

        return await query
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync();
    }

    public async Task<Vocabulary?> GetAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.Vocabulary
            .Include(v => v.Conjugations)
            .Include(v => v.SourceMaterial)
            .Include(v => v.Srs)
            .Include(v => v.Meanings)
            .FirstOrDefaultAsync(v => v.Id == id);
    }

    /// <summary>
    /// Create or update a vocabulary item along with its conjugation set. When
    /// <paramref name="conjugations"/> is supplied it replaces the existing set.
    /// </summary>
    public async Task<int> SaveAsync(Vocabulary input, IReadOnlyList<ConjugationResult>? conjugations)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();

        Vocabulary entity;
        if (input.Id == 0)
        {
            entity = new Vocabulary { CreatedAt = DateTime.UtcNow };
            db.Vocabulary.Add(entity);
        }
        else
        {
            entity = await db.Vocabulary
                .Include(v => v.Conjugations)
                .Include(v => v.Meanings)
                .FirstAsync(v => v.Id == input.Id);
        }

        entity.Japanese = input.Japanese.Trim();
        entity.Reading = input.Reading.Trim();
        entity.AlternateReadings = (input.AlternateReadings ?? string.Empty).Trim();
        entity.ContextSentence = string.IsNullOrWhiteSpace(input.ContextSentence) ? null : input.ContextSentence.Trim();
        entity.Memo = string.IsNullOrWhiteSpace(input.Memo) ? null : input.Memo.Trim();
        entity.VerbClass = input.VerbClass;
        entity.DisplayFurigana = input.DisplayFurigana;
        entity.IsAsleep = input.IsAsleep;
        entity.SourceMaterialId = input.SourceMaterialId;

        // Replace the meanings set with the (trimmed, de-duplicated, non-empty)
        // meanings from the input, preserving order.
        db.VocabularyMeanings.RemoveRange(entity.Meanings);
        entity.Meanings.Clear();
        int order = 0;
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (VocabularyMeaning meaning in input.Meanings)
        {
            string text = (meaning.Text ?? string.Empty).Trim();
            if (text.Length == 0 || !seen.Add(text))
            {
                continue;
            }

            entity.Meanings.Add(new VocabularyMeaning { Text = text, SortOrder = order });
            order++;
        }

        if (conjugations is not null)
        {
            db.Conjugations.RemoveRange(entity.Conjugations);
            entity.Conjugations.Clear();

            if (entity.VerbClass != VerbClass.None)
            {
                foreach (ConjugationResult result in conjugations)
                {
                    entity.Conjugations.Add(new Conjugation
                    {
                        Form = result.Form,
                        Polarity = result.Polarity,
                        ExpectedKana = result.Kana,
                    });
                }
            }
        }

        await db.SaveChangesAsync();
        return entity.Id;
    }

    public async Task SetAsleepAsync(int id, bool asleep)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        Vocabulary? entity = await db.Vocabulary.FindAsync(id);
        if (entity is not null)
        {
            entity.IsAsleep = asleep;
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        Vocabulary? entity = await db.Vocabulary.FindAsync(id);
        if (entity is not null)
        {
            db.Vocabulary.Remove(entity);
            await db.SaveChangesAsync();
        }
    }
}
