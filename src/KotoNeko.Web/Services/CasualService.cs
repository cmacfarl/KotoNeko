using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Llm;
using Microsoft.EntityFrameworkCore;
using KotoNeko.Data;

namespace KotoNeko.Web.Services;

/// <summary>A sentence a casual version can be written for, and what it belongs to.</summary>
/// <param name="StudyItemId">The word, or the grammar point.</param>
/// <param name="Label">The word, or the grammar point's pattern.</param>
public sealed record CasualCandidate(
    int? PracticeSentenceId, int? GrammarExampleId, string Text, string English, int StudyItemId, string Label, string LabelMeaning);

/// <summary>A casual version with its source, for the review page and dictation.</summary>
/// <param name="Stale">The source's text has changed since it was written.</param>
public sealed record CasualView(CasualVersion Version, CasualCandidate? Source, bool Stale)
{
    public string AudioUrl => $"/api/casual/{Version.Id}/audio";
}

/// <summary>
/// Casual versions of the user's sentences (Phase 6c): written in batches on request
/// by the listening-material model (Settings), approved one by one, voiced on
/// approval, then mixed into dictation. Sources are the practice sentences in use of
/// learned words and the approved examples of learned grammar points; sentences with
/// a polite ending or a full form that casual speech contracts go first.
/// </summary>
public sealed class CasualService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly LlmService _llm;
    private readonly ListeningService _listening;
    private readonly JapaneseTtsService _tts;
    private readonly SentenceReadingService _readings;
    private readonly ILogger<CasualService> _logger;

    public CasualService(
        IDbContextFactory<KotoNekoDbContext> factory, LlmService llm, ListeningService listening, JapaneseTtsService tts,
        SentenceReadingService readings, ILogger<CasualService> logger)
    {
        _factory = factory;
        _llm = llm;
        _listening = listening;
        _tts = tts;
        _readings = readings;
        _logger = logger;
    }

    public Task<ModelChoice?> WriterAsync() => _listening.WriterAsync();

    /// <summary>Every sentence a casual version can come from, now.</summary>
    internal static async Task<List<CasualCandidate>> CandidatesAsync(KotoNekoDbContext db)
    {
        List<CasualCandidate> practice = await db.PracticeSentences.AsNoTracking()
            .Where(s => (s.Status == PracticeSentenceStatus.Approved || s.Status == PracticeSentenceStatus.NativeChecked)
                && !s.Vocabulary!.IsAsleep && s.Vocabulary.Srs != null && s.Vocabulary.Srs.Stage != SrsStage.Locked)
            .Select(s => new CasualCandidate(s.Id, null, s.Text, s.English, s.VocabularyId, s.Vocabulary!.Japanese,
                s.Vocabulary.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).FirstOrDefault() ?? ""))
            .ToListAsync();
        List<CasualCandidate> examples = await db.GrammarExamples.AsNoTracking()
            .Where(x => x.Status == GrammarContentStatus.Approved
                && x.GrammarPoint!.Srs != null && x.GrammarPoint.Srs.Stage != SrsStage.Locked
                && !x.GrammarPoint.IsAsleep && !x.GrammarPoint.Entry!.IsPruned)
            .Select(x => new CasualCandidate(null, x.Id, x.Text, x.English, x.GrammarPointId, x.GrammarPoint!.Entry!.Pattern, x.GrammarPoint.Entry.Meaning))
            .ToListAsync();
        return practice.Concat(examples).ToList();
    }

    private static (int?, int?) Key(CasualCandidate c) => (c.PracticeSentenceId, c.GrammarExampleId);
    private static (int?, int?) Key(CasualVersion v) => (v.PracticeSentenceId, v.GrammarExampleId);

    /// <summary>Drafts to review and approved versions, newest first, each with its source (null when it's no longer in use).</summary>
    public async Task<(List<CasualView> Drafts, List<CasualView> Approved, int Waiting)> ListAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        Dictionary<(int?, int?), CasualCandidate> sources = (await CandidatesAsync(db)).ToDictionary(Key);
        List<CasualVersion> versions = await db.CasualVersions.AsNoTracking()
            .Where(v => v.Status != CasualVersionStatus.Rejected)
            .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id)
            .ToListAsync();
        List<CasualView> views = versions.Select(v => {
            CasualCandidate? source = sources.GetValueOrDefault(Key(v));
            return new CasualView(v, source, source is not null && source.Text != v.Original);
        }).ToList();
        HashSet<(int?, int?)> covered = (await db.CasualVersions.AsNoTracking().Select(v => new { v.PracticeSentenceId, v.GrammarExampleId }).ToListAsync())
            .Select(v => (v.PracticeSentenceId, v.GrammarExampleId)).ToHashSet();
        int waiting = sources.Values.Count(c => !covered.Contains(Key(c)) && CasualRewrite.WouldChange(c.Text));
        return (views.Where(v => v.Version.Status == CasualVersionStatus.Draft).ToList(),
            views.Where(v => v.Version.Status == CasualVersionStatus.Approved).ToList(), waiting);
    }

    /// <summary>
    /// Write casual versions for the next batch of sentences that have none (or whose
    /// version is stale), as drafts. Returns how many were written. Throws
    /// <see cref="LlmException"/> when no usable answer came back.
    /// </summary>
    public async Task<int> WriteAsync(Func<string, Task> progress, CancellationToken cancellationToken = default)
    {
        ModelChoice writer = await _listening.WriterAsync()
            ?? throw new LlmException(LlmOutcome.NotConfigured, "No model is set up to write: choose one in Settings, or set Llm:Jobs:Generate.");
        List<CasualCandidate> batch;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken)) {
            List<CasualCandidate> all = await CandidatesAsync(db);
            Dictionary<(int?, int?), string> have = (await db.CasualVersions.AsNoTracking()
                    .Select(v => new { v.PracticeSentenceId, v.GrammarExampleId, v.Original, v.Status }).ToListAsync())
                .ToDictionary(v => ((int?)v.PracticeSentenceId, (int?)v.GrammarExampleId), v => v.Original);
            batch = all
                .Where(c => !have.TryGetValue(Key(c), out string? original) || original != c.Text)
                .OrderByDescending(c => CasualRewrite.WouldChange(c.Text))
                .ThenBy(_ => Random.Shared.Next())
                .Take(CasualRewrite.BatchSize)
                .ToList();
        }
        if (batch.Count == 0) {
            return 0;
        }

        List<CasualSource> sources = batch.Select((c, i) => new CasualSource(i + 1, c.Text, c.English)).ToList();
        await progress($"Writing casual versions of {batch.Count} sentences with {writer.Provider} · {writer.Model}…");
        LlmResult<CasualRewriteAnswer> answer = await _llm.CallOnAsync<CasualRewriteAnswer>(writer.Provider, writer.Model,
            CasualRewrite.Build(sources), CasualRewrite.Validate, cancellationToken);
        Dictionary<int, string> kept = CasualRewrite.Clean(answer.Value, sources);
        string by = $"{answer.Route.Provider} · {answer.Route.Model} · {CasualRewrite.PromptVersion}";

        // Paid for: save whatever happens next.
        DateTime now = DateTime.UtcNow;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            for (int i = 0; i < batch.Count; i++) {
                CasualCandidate source = batch[i];
                CasualVersion? existing = await db.CasualVersions.Include(v => v.Audio).FirstOrDefaultAsync(v =>
                    v.PracticeSentenceId == source.PracticeSentenceId && v.GrammarExampleId == source.GrammarExampleId);
                if (existing is not null) {
                    db.CasualVersions.Remove(existing);
                    await db.SaveChangesAsync(CancellationToken.None);
                }
                bool rewritten = kept.TryGetValue(i + 1, out string? casual);
                db.CasualVersions.Add(new CasualVersion {
                    PracticeSentenceId = source.PracticeSentenceId,
                    GrammarExampleId = source.GrammarExampleId,
                    Original = source.Text,
                    // Nothing to change: kept as rejected, so it isn't sent again.
                    Text = rewritten ? casual! : source.Text,
                    Status = rewritten ? CasualVersionStatus.Draft : CasualVersionStatus.Rejected,
                    Writer = by.Length > ListeningScript.MaxWriterLength ? by[..ListeningScript.MaxWriterLength] : by,
                    CreatedAt = now,
                });
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        _readings.RequestBackfill();
        return kept.Count;
    }

    /// <summary>
    /// Approve a draft (as edited) and voice it. Returns an error or a note about
    /// the audio, or null when all went well.
    /// </summary>
    public async Task<string?> ApproveAsync(int id, string text)
    {
        text = Core.Domain.GrammarContent.CleanSentence(text);
        if (text.Length == 0 || text.Length > CasualVersion.MaxTextLength) {
            return $"A casual version needs Japanese, under {CasualVersion.MaxTextLength} characters.";
        }
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        CasualVersion? version = await db.CasualVersions.Include(v => v.Audio).FirstOrDefaultAsync(v => v.Id == id);
        if (version is null) {
            return "That casual version no longer exists.";
        }
        if (version.Text != text) {
            version.Text = text;
            version.HasAudio = false;
        }
        version.Status = CasualVersionStatus.Approved;
        await db.SaveChangesAsync();
        await _readings.EnsureAsync(new[] { text });

        if (version.HasAudio) {
            return null;
        }
        try {
            SpeechSpeed speed = (await SettingsService.LoadAsync(db)).SpeechSpeed;
            byte[] clip = (await _tts.SynthesiseAsync(text, speed)).Audio;
            if (version.Audio is null) {
                version.Audio = new CasualVersionAudio { Audio = clip };
            } else {
                version.Audio.Audio = clip;
            }
            version.HasAudio = clip.Length > 0;
            await db.SaveChangesAsync();
            return null;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Couldn't voice casual version {Id}.", id);
            return $"Approved, but its audio couldn't be generated ({ex.Message}). Dictation uses it once it has audio: approve it again to retry.";
        }
    }

    /// <summary>Turn a version down (it's kept, so its sentence isn't sent again), or withdraw an approved one.</summary>
    public async Task SetStatusAsync(int id, CasualVersionStatus status)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.CasualVersions.FindAsync(id) is { } version) {
            version.Status = status;
            await db.SaveChangesAsync();
        }
    }

    public async Task<byte[]?> GetAudioAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.CasualVersionAudios.AsNoTracking().Where(a => a.CasualVersionId == id).Select(a => a.Audio).FirstOrDefaultAsync();
    }

    /// <summary>Approved, voiced versions whose source is still in use and unchanged: what dictation may use.</summary>
    internal static async Task<List<CasualView>> UsableAsync(KotoNekoDbContext db)
    {
        Dictionary<(int?, int?), CasualCandidate> sources = (await CandidatesAsync(db)).ToDictionary(Key);
        List<CasualVersion> versions = await db.CasualVersions.AsNoTracking()
            .Where(v => v.Status == CasualVersionStatus.Approved && v.HasAudio)
            .ToListAsync();
        return versions
            .Select(v => (Version: v, Source: sources.GetValueOrDefault(Key(v))))
            .Where(x => x.Source is not null && x.Source.Text == x.Version.Original)
            .Select(x => new CasualView(x.Version, x.Source, false))
            .ToList();
    }
}
