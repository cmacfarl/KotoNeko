using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Data;
using KotoNeko.Data.Vocab;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services.Llm;

/// <summary>
/// Generates practice-sentence candidates for a word with the Generate job's LLM.
/// The request carries the word, a sample of the known-words pool and the Known
/// grammar points; the candidates are saved as drafts, which the sentence editor
/// checks offline and the user approves, edits or rejects.
/// </summary>
public sealed class SentenceGenerationService
{
    private readonly LlmService _llm;
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly GrammarService _grammar;
    private readonly PracticeSentenceService _sentences;

    public SentenceGenerationService(
        LlmService llm, IDbContextFactory<KotoNekoDbContext> factory, GrammarService grammar, PracticeSentenceService sentences)
    {
        _llm = llm;
        _factory = factory;
        _grammar = grammar;
        _sentences = sentences;
    }

    /// <summary>Where generation goes, or null when no route is configured.</summary>
    public LlmRoute? Route => _llm.Route(LlmJob.Generate);

    /// <summary>
    /// Ask for new candidates for a word and save them as drafts. Returns how many
    /// were added and a message for the user. Throws <see cref="LlmException"/> when
    /// the model gave no usable answer.
    /// </summary>
    public async Task<(int Added, string Message)> GenerateAsync(int vocabularyId, CancellationToken cancellationToken = default)
    {
        SentenceTarget target;
        List<string> known;
        List<string> existing;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken)) {
            Vocabulary word = await db.Vocabulary.AsNoTracking().Include(v => v.Meanings)
                .FirstOrDefaultAsync(v => v.Id == vocabularyId, cancellationToken)
                ?? throw new InvalidOperationException("That word no longer exists.");
            target = new SentenceTarget(word.Japanese, word.Reading,
                word.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).ToList());

            SrsStage threshold = (await SettingsService.LoadAsync(db)).KnownWordStage;
            List<string> pool = await db.Vocabulary.AsNoTracking().InPool(threshold)
                .Select(v => v.Japanese).ToListAsync(cancellationToken);
            known = pool.Where(w => w != word.Japanese).Distinct().OrderBy(_ => Random.Shared.Next())
                .Take(SentenceGeneration.KnownWordSample).ToList();

            existing = await db.PracticeSentences.AsNoTracking().Where(s => s.VocabularyId == vocabularyId)
                .Select(s => s.Text).ToListAsync(cancellationToken);
        }

        List<(string, string)> grammar = (await _grammar.KnownAsync()).Select(g => (g.Pattern, g.Meaning)).ToList();
        LlmRequest request = SentenceGeneration.Build(target, known, grammar, existing);
        GeneratedSentences answer = await _llm.CallAsync<GeneratedSentences>(request, SentenceGeneration.Validate, cancellationToken);

        List<GeneratedSentence> candidates = SentenceGeneration.Clean(answer, existing);
        int added = await _sentences.AddDraftsAsync(vocabularyId, candidates);
        return (added, added == 0
            ? "The model only suggested sentences this word already has. Try again for different ones."
            : $"{added} new draft{(added == 1 ? "" : "s")} below. Each is checked against your known words; approve, edit or reject them.");
    }
}
