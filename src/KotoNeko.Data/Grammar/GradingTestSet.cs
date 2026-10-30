using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KotoNeko.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Data.Grammar;

/// <summary>A drafted test sentence, without a label: labels come from a person.</summary>
public sealed record GradingCandidate(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("grammarKey")] string GrammarKey,
    [property: JsonPropertyName("task")] string Task,
    [property: JsonPropertyName("sentence")] string Sentence);

/// <summary>
/// The drafted candidates for the grading test set (grading-candidates.json): about
/// 20 sentences per grammar point, mixing correct uses with typical learner mistakes.
/// They carry no label, and are queued in a shuffled order, so how they were drafted
/// can't steer the person labelling them.
/// </summary>
public static class GradingTestSet
{
    private const string ResourceName = "KotoNeko.Data.Grammar.grading-candidates.json";
    private const string IntentResourceName = "KotoNeko.Data.Grammar.grading-candidates-intent.json";

    /// <summary>The model that drafted the candidates: its drafted intent is a vote attributed to it.</summary>
    public const string DraftedByProvider = "Claude";
    public const string DraftedByModel = "claude-opus-5-5";

    public static IReadOnlyList<GradingCandidate> Candidates()
    {
        using Stream stream = typeof(GradingTestSet).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The grading candidates ({ResourceName}) are missing from the build.");
        return JsonSerializer.Deserialize<List<GradingCandidate>>(stream)
            ?? throw new InvalidDataException("The grading candidates file is empty.");
    }

    /// <summary>
    /// How each candidate was drafted, by key: one pre-labelling vote, attributed to
    /// <see cref="DraftedByModel"/>. Kept apart from the candidates so it's never shown
    /// while labelling.
    /// </summary>
    public static IReadOnlyDictionary<string, GradingLabel> DraftedIntent()
    {
        using Stream stream = typeof(GradingTestSet).Assembly.GetManifestResourceStream(IntentResourceName)
            ?? throw new InvalidOperationException($"The drafted intent ({IntentResourceName}) is missing from the build.");
        Dictionary<string, string> raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException("The drafted intent file is empty.");
        return raw.Where(kv => !kv.Key.StartsWith('_'))
            .ToDictionary(kv => kv.Key, kv => Enum.Parse<GradingLabel>(kv.Value), StringComparer.Ordinal);
    }

    /// <summary>A stable shuffle: the same order every time, unrelated to the file's order.</summary>
    public static int ShuffledOrder(string key) =>
        BitConverter.ToInt32(SHA256.HashData(Encoding.UTF8.GetBytes(key)), 0) & int.MaxValue;

    /// <summary>
    /// The items seeding adds: candidates whose key isn't there yet, with their grammar
    /// point's pattern and meaning from the drafted grammar list.
    /// </summary>
    public static List<GradingTestItem> Plan(
        IEnumerable<GradingCandidate> candidates, IEnumerable<DraftedGrammarPoint> grammar,
        IEnumerable<string?> existingKeys, DateTime now)
    {
        Dictionary<string, DraftedGrammarPoint> points = grammar.ToDictionary(g => g.Key, StringComparer.Ordinal);
        HashSet<string> have = existingKeys.OfType<string>().ToHashSet(StringComparer.Ordinal);
        List<GradingTestItem> items = new();
        foreach (GradingCandidate c in candidates.Where(c => !have.Contains(c.Key))) {
            if (!points.TryGetValue(c.GrammarKey, out DraftedGrammarPoint? point)) {
                throw new InvalidDataException($"Candidate {c.Key} names an unknown grammar point, {c.GrammarKey}.");
            }
            items.Add(new GradingTestItem {
                SeedKey = c.Key,
                GrammarKey = c.GrammarKey,
                Pattern = point.Pattern,
                Meaning = point.Meaning,
                Task = c.Task,
                Sentence = c.Sentence,
                SortOrder = ShuffledOrder(c.Key),
                CreatedAt = now,
            });
        }
        return items;
    }

    /// <summary>Add the candidates not there yet. Returns how many were added.</summary>
    public static async Task<int> SeedAsync(KotoNekoDbContext db, DateTime now, CancellationToken cancellationToken = default)
    {
        List<string?> keys = await db.GradingTestItems.Select(i => i.SeedKey).ToListAsync(cancellationToken);
        List<GradingTestItem> added = Plan(Candidates(), GrammarList.Drafted(), keys, now);
        db.GradingTestItems.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);
        return added.Count;
    }
}
