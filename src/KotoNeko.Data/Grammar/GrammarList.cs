using System.Text.Json;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Data.Grammar;

/// <summary>One grammar point in the drafted list (grammar-list.json).</summary>
public sealed record DraftedGrammarPoint(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("level")] JlptLevel Level,
    [property: JsonPropertyName("pattern")] string Pattern,
    [property: JsonPropertyName("meaning")] string Meaning);

/// <summary>
/// The drafted N5–N3 grammar list, shipped with KotoNeko, and seeding it into the
/// user's list. There is no official JLPT grammar list, so this one was drafted by
/// an LLM (the names of grammar points are facts; the meanings are its own words),
/// cross-checked against JLPT Sensei's published lists for coverage and levels,
/// and is meant to be pruned. Keys are ids: a point moved to another level keeps
/// its key.
/// </summary>
public static class GrammarList
{
    private const string ResourceName = "KotoNeko.Data.Grammar.grammar-list.json";

    private static readonly JsonSerializerOptions Options = new() {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>The drafted list, in study order.</summary>
    public static IReadOnlyList<DraftedGrammarPoint> Drafted()
    {
        using Stream stream = typeof(GrammarList).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The drafted grammar list ({ResourceName}) is missing from the build.");
        return JsonSerializer.Deserialize<List<DraftedGrammarPoint>>(stream, Options)
            ?? throw new InvalidDataException("The drafted grammar list is empty.");
    }

    /// <summary>
    /// The entries seeding adds: every drafted point whose key isn't already on the
    /// list (pruned entries included, so pruning sticks). N5 points start Known and
    /// the rest Not yet. Existing entries are never changed.
    /// </summary>
    public static List<GrammarListEntry> Plan(IEnumerable<DraftedGrammarPoint> drafted, IEnumerable<string?> existingKeys, DateTime now)
    {
        HashSet<string> have = existingKeys.OfType<string>().ToHashSet(StringComparer.Ordinal);
        return drafted
            .Select((point, index) => (point, index))
            .Where(x => !have.Contains(x.point.Key))
            .Select(x => new GrammarListEntry {
                SeedKey = x.point.Key,
                Pattern = x.point.Pattern,
                Meaning = x.point.Meaning,
                Level = x.point.Level,
                Status = x.point.Level == JlptLevel.N5 ? GrammarStatus.Known : GrammarStatus.NotYet,
                SortOrder = x.index,
                CreatedAt = now,
            })
            .ToList();
    }

    /// <summary>Add the drafted points not yet on the list. Returns how many were added.</summary>
    public static async Task<int> SeedAsync(KotoNekoDbContext db, DateTime now, CancellationToken cancellationToken = default)
    {
        List<string?> keys = await db.GrammarList.Select(e => e.SeedKey).ToListAsync(cancellationToken);
        List<GrammarListEntry> added = Plan(Drafted(), keys, now);
        db.GrammarList.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return added.Count;
    }
}
