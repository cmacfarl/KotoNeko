using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Data;
using KotoNeko.Data.Vocab;
using KotoNeko.Llm;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services.Llm;

/// <summary>
/// Drafts a grammar point's lesson content with the Generate job's LLM: the point,
/// a sample of the known-words pool and the Known grammar go in; the explanation,
/// formation, register, contrasts, mistakes and situations replace the point's
/// content, and the examples are added as drafts. The point stays a draft until the
/// user approves it.
/// </summary>
public sealed class GrammarDraftingService
{
    private readonly LlmService _llm;
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly GrammarService _grammar;
    private readonly GrammarPointService _points;

    public GrammarDraftingService(
        LlmService llm, IDbContextFactory<KotoNekoDbContext> factory, GrammarService grammar, GrammarPointService points)
    {
        _llm = llm;
        _factory = factory;
        _grammar = grammar;
        _points = points;
    }

    /// <summary>Where drafting goes, or null when no route is configured.</summary>
    public LlmRoute? Route => _llm.Route(LlmFeature.GrammarDrafting);

    /// <summary>
    /// Draft the point's content. Returns a message for the user. Throws
    /// <see cref="LlmException"/> when the model gave no usable answer.
    /// </summary>
    public async Task<string> DraftAsync(int pointId, CancellationToken cancellationToken = default)
    {
        GrammarDraftTarget target;
        List<string> known;
        List<string> existing;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken)) {
            GrammarPoint point = await db.GrammarPoints.AsNoTracking().Include(p => p.Entry).Include(p => p.Examples)
                .FirstOrDefaultAsync(p => p.Id == pointId, cancellationToken)
                ?? throw new InvalidOperationException("That grammar point no longer exists.");
            target = new GrammarDraftTarget(point.Entry!.Pattern, point.Entry.Meaning, point.Entry.Level);
            existing = point.Examples.Select(x => x.Text).ToList();

            SrsStage threshold = (await SettingsService.LoadAsync(db)).KnownWordStage;
            List<string> pool = await db.Vocabulary.AsNoTracking().InPool(threshold)
                .Select(v => v.Japanese).ToListAsync(cancellationToken);
            known = pool.Distinct().OrderBy(_ => Random.Shared.Next()).Take(GrammarDrafting.KnownWordSample).ToList();
        }

        List<(string, string)> grammar = (await _grammar.KnownAsync())
            .Where(g => g.Pattern != target.Pattern)
            .Select(g => (g.Pattern, g.Meaning)).ToList();
        LlmRequest request = GrammarDrafting.Build(target, known, grammar, existing);
        LlmResult<GrammarDraft> answer = await _llm.CallWithUsageAsync<GrammarDraft>(request, GrammarDrafting.Validate, cancellationToken);
        GrammarDraft draft = answer.Value;

        string draftedBy = $"{answer.Route.Provider} · {answer.Route.Model} · {GrammarDrafting.PromptVersion}";
        if (await _points.SaveContentAsync(pointId, draft.Explanation, GrammarContent.JoinFormation(draft.Formation!),
                GrammarDrafting.Formality(draft.Formality),
                draft.Contrasts ?? new(), draft.Mistakes ?? new(), draft.Situations ?? new(), draftedBy) is { } error) {
            throw new InvalidOperationException(error);
        }
        // Back to draft: the new content needs the user's OK before a lesson teaches it.
        await _points.SetStatusAsync(pointId, GrammarContentStatus.Draft);

        int added = 0;
        foreach (DraftedExample x in GrammarDrafting.CleanExamples(draft, existing)) {
            (bool ok, _) = await _points.AddExampleAsync(pointId, x.Japanese, x.English,
                GrammarExampleSource.Generated, GrammarContentStatus.Draft, voice: false);
            added += ok ? 1 : 0;
        }
        return $"Drafted by {answer.Route.Provider} · {answer.Route.Model}. The content above is replaced, and {added} draft example{(added == 1 ? "" : "s")} added. Check it all, edit what's off, approve the examples you want, then approve the point.";
    }
}
