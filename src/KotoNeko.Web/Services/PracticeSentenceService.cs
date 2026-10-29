using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using KotoNeko.Data.Vocab;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>A practice sentence with its readings and how it fares in the check.</summary>
/// <param name="View">Its stored readings; null until they've been worked out.</param>
/// <param name="Check">The comprehensibility check; null without readings.</param>
public sealed record CheckedSentence(PracticeSentence Sentence, SentenceView? View, SentenceCheckResult? Check)
{
    /// <summary>In use, voiced, and passes the listening check.</summary>
    public bool UsableForListening => Sentence.InUse && Sentence.HasAudio && Check is { UsableForListening: true };

    public string AudioUrl => $"/api/practice-sentence/{Sentence.Id}/audio";
}

/// <summary>A sentence waiting for a native speaker's check, with the word it practises.</summary>
public sealed record ProofreadItem(int Id, string Text, string English, bool HasAudio, string Word, string Reading, string Meaning)
{
    public string AudioUrl => $"/api/practice-sentence/{Id}/audio";
}

/// <summary>
/// Practice sentences: adding, promoting a context sentence, status, audio, and
/// the comprehensibility check against the known-words pool. The check is worked
/// out each time from the stored readings, never stored, because the pool changes
/// as reviews move words past the threshold.
/// </summary>
public sealed class PracticeSentenceService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly SentenceReadingService _readings;
    private readonly JapaneseTtsService _tts;
    private readonly ILogger<PracticeSentenceService> _logger;

    public PracticeSentenceService(
        IDbContextFactory<KotoNekoDbContext> factory, SentenceReadingService readings, JapaneseTtsService tts,
        ILogger<PracticeSentenceService> logger)
    {
        _factory = factory;
        _readings = readings;
        _tts = tts;
        _logger = logger;
    }

    /// <summary>
    /// Whether the user knows a word, by dictionary form: a word in the pool, by its
    /// spelling, or, for a word written in kana, by a pool word's reading (わかる for
    /// 分かる).
    /// </summary>
    public static async Task<Func<string, bool>> PoolAsync(KotoNekoDbContext db)
    {
        SrsStage threshold = (await SettingsService.LoadAsync(db)).KnownWordStage;
        var pool = await db.Vocabulary.AsNoTracking().InPool(threshold)
            .Select(v => new { v.Japanese, v.Reading })
            .ToListAsync();
        return SentenceCheck.Knows(pool.Select(w => (w.Japanese, w.Reading)));
    }

    /// <summary>
    /// The sentences of these words, checked. <paramref name="inUseOnly"/> leaves out
    /// drafts and sentences needing a fix. Readings are only worked out when
    /// <paramref name="ensureReadings"/>; otherwise a sentence without stored
    /// readings comes back unchecked (never while building a review session).
    /// </summary>
    public async Task<Dictionary<int, List<CheckedSentence>>> CheckedForAsync(
        IReadOnlyCollection<int> vocabularyIds, bool inUseOnly, bool ensureReadings)
    {
        Dictionary<int, List<CheckedSentence>> result = new();
        if (vocabularyIds.Count == 0) {
            return result;
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        List<PracticeSentence> sentences = new();
        foreach (int[] chunk in vocabularyIds.Distinct().Chunk(1000)) {
            IQueryable<PracticeSentence> query = db.PracticeSentences.AsNoTracking()
                .Include(s => s.Vocabulary)
                .Where(s => chunk.Contains(s.VocabularyId));
            if (inUseOnly) {
                query = query.Where(s => s.Status == PracticeSentenceStatus.Approved || s.Status == PracticeSentenceStatus.NativeChecked);
            }
            sentences.AddRange(await query.OrderBy(s => s.Id).ToListAsync());
        }
        if (sentences.Count == 0) {
            return result;
        }

        List<string> texts = sentences.Select(s => s.Text).Distinct().ToList();
        if (ensureReadings) {
            await _readings.EnsureAsync(texts);
        }
        Dictionary<string, SentenceView> views = await _readings.GetManyAsync(texts);
        Func<string, bool> isKnown = await PoolAsync(db);

        foreach (PracticeSentence s in sentences) {
            SentenceView? view = views.GetValueOrDefault(s.Text);
            SentenceCheckResult? check = view is null ? null : Check(view, s.Vocabulary!.Japanese, isKnown);
            if (!result.TryGetValue(s.VocabularyId, out List<CheckedSentence>? list)) {
                result[s.VocabularyId] = list = new List<CheckedSentence>();
            }
            list.Add(new CheckedSentence(s, view, check));
        }
        return result;
    }

    /// <summary>Check one sentence's stored readings for practising <paramref name="target"/>.</summary>
    public static SentenceCheckResult Check(SentenceView view, string target, Func<string, bool> isKnown) =>
        SentenceCheck.Check(view.Text, view.Pieces,
            view.Words.Select(w => new CheckWord(w.Start, w.Length, w.Lemma, w.IsParticle)).ToList(),
            target, isKnown);

    /// <summary>A word's sentences, every status, readings worked out if missing.</summary>
    public async Task<List<CheckedSentence>> ListForWordAsync(int vocabularyId) =>
        (await CheckedForAsync(new[] { vocabularyId }, inUseOnly: false, ensureReadings: true))
            .GetValueOrDefault(vocabularyId) ?? new List<CheckedSentence>();

    /// <summary>
    /// Add a sentence the user wrote, approved, and voice it at the default speed.
    /// Returns an error, or a note when it was added but its audio couldn't be made.
    /// </summary>
    public async Task<(bool Added, string? Message)> AddAsync(int vocabularyId, string text, string english)
    {
        text = text.Trim();
        english = english.Trim();
        if (text.Length == 0) {
            return (false, "Type the sentence first.");
        }
        if (text.Length > PracticeSentence.MaxTextLength || english.Length > PracticeSentence.MaxEnglishLength) {
            return (false, $"That's too long for a practice sentence (at most {PracticeSentence.MaxTextLength} characters).");
        }
        return await AddAsync(vocabularyId, text, english, PracticeSentenceSource.Yours, audio: null);
    }

    /// <summary>
    /// Make the word's context sentence a practice sentence too, approved, reusing its
    /// audio when it has some. The context sentence itself is unchanged.
    /// </summary>
    public async Task<(bool Added, string? Message)> PromoteContextAsync(int vocabularyId)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        Vocabulary? word = await db.Vocabulary.AsNoTracking().Include(v => v.Audio).FirstOrDefaultAsync(v => v.Id == vocabularyId);
        string text = word?.ContextSentence?.Trim() ?? string.Empty;
        if (text.Length == 0) {
            return (false, "This word has no context sentence.");
        }
        if (text.Length > PracticeSentence.MaxTextLength) {
            return (false, $"The context sentence is too long for a practice sentence (at most {PracticeSentence.MaxTextLength} characters).");
        }
        byte[]? audio = word!.Audio?.SentenceAudio is { Length: > 0 } clip ? clip : null;
        return await AddAsync(vocabularyId, text, string.Empty, PracticeSentenceSource.Context, audio);
    }

    private async Task<(bool Added, string? Message)> AddAsync(
        int vocabularyId, string text, string english, PracticeSentenceSource source, byte[]? audio)
    {
        int id;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            if (await db.PracticeSentences.AnyAsync(s => s.VocabularyId == vocabularyId && s.Text == text)) {
                return (false, "This word already has that sentence.");
            }
            DateTime now = DateTime.UtcNow;
            PracticeSentence sentence = new() {
                VocabularyId = vocabularyId,
                Text = text,
                English = english,
                Source = source,
                Status = PracticeSentenceStatus.Approved,
                CreatedAt = now,
                UpdatedAt = now,
            };
            if (audio is not null) {
                sentence.Audio = new PracticeSentenceAudio { Audio = audio };
                sentence.HasAudio = true;
            }
            db.PracticeSentences.Add(sentence);
            await db.SaveChangesAsync();
            id = sentence.Id;
        }

        _readings.RequestBackfill();
        return (true, audio is null ? await GenerateAudioAsync(id) : null);
    }

    /// <summary>Voice a sentence at the default speed. Returns a note when it fails.</summary>
    public async Task<string?> GenerateAudioAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        PracticeSentence? sentence = await db.PracticeSentences.Include(s => s.Audio).FirstOrDefaultAsync(s => s.Id == id);
        if (sentence is null) {
            return null;
        }
        try {
            SpeechSpeed speed = (await SettingsService.LoadAsync(db)).SpeechSpeed;
            byte[] clip = (await _tts.SynthesiseAsync(sentence.Text, speed)).Audio;
            if (sentence.Audio is null) {
                sentence.Audio = new PracticeSentenceAudio { Audio = clip };
            } else {
                sentence.Audio.Audio = clip;
            }
            sentence.HasAudio = clip.Length > 0;
            sentence.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return null;
        } catch (Exception ex) {
            _logger.LogWarning(ex, "Couldn't generate audio for a practice sentence.");
            return $"The sentence was saved, but its audio couldn't be generated ({ex.Message}). Try ▶ Generate audio later.";
        }
    }

    public async Task SetStatusAsync(int id, PracticeSentenceStatus status)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.PracticeSentences.FindAsync(id) is { } sentence) {
            sentence.Status = status;
            sentence.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public async Task SetEnglishAsync(int id, string english)
    {
        english = english.Trim();
        if (english.Length > PracticeSentence.MaxEnglishLength) {
            english = english[..PracticeSentence.MaxEnglishLength];
        }
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.PracticeSentences.FindAsync(id) is { } sentence) {
            sentence.English = english;
            sentence.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.PracticeSentences.FindAsync(id) is { } sentence) {
            db.PracticeSentences.Remove(sentence);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>Save generated candidates as drafts (not used until approved). Returns how many were new.</summary>
    public async Task<int> AddDraftsAsync(int vocabularyId, IEnumerable<GeneratedSentence> candidates)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        HashSet<string> have = (await db.PracticeSentences.Where(s => s.VocabularyId == vocabularyId)
            .Select(s => s.Text).ToListAsync()).ToHashSet(StringComparer.Ordinal);
        DateTime now = DateTime.UtcNow;
        int added = 0;
        foreach (GeneratedSentence c in candidates) {
            if (c.Japanese.Length > PracticeSentence.MaxTextLength || !have.Add(c.Japanese)) {
                continue;
            }
            db.PracticeSentences.Add(new PracticeSentence {
                VocabularyId = vocabularyId,
                Text = c.Japanese,
                English = c.English.Length > PracticeSentence.MaxEnglishLength ? c.English[..PracticeSentence.MaxEnglishLength] : c.English,
                Source = PracticeSentenceSource.Generated,
                Status = PracticeSentenceStatus.Draft,
                CreatedAt = now,
                UpdatedAt = now,
            });
            added++;
        }
        await db.SaveChangesAsync();
        if (added > 0) {
            _readings.RequestBackfill();
        }
        return added;
    }

    /// <summary>
    /// Put a sentence in use, voicing it at the default speed if it has no audio.
    /// Returns a note when the audio couldn't be made.
    /// </summary>
    public async Task<string?> ApproveAsync(int id)
    {
        bool needsAudio;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            if (await db.PracticeSentences.FindAsync(id) is not { } sentence) {
                return null;
            }
            sentence.Status = PracticeSentenceStatus.Approved;
            sentence.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            needsAudio = !sentence.HasAudio;
        }
        return needsAudio ? await GenerateAudioAsync(id) : null;
    }

    /// <summary>
    /// Change a draft's or a "needs a fix" sentence's text and English. New text
    /// drops the old audio (it's voiced again on approval). Returns an error, or null.
    /// </summary>
    public async Task<string?> EditAsync(int id, string text, string english)
    {
        text = text.Trim();
        english = english.Trim();
        if (text.Length == 0) {
            return "The sentence can't be empty.";
        }
        if (text.Length > PracticeSentence.MaxTextLength || english.Length > PracticeSentence.MaxEnglishLength) {
            return $"That's too long for a practice sentence (at most {PracticeSentence.MaxTextLength} characters).";
        }

        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        PracticeSentence? sentence = await db.PracticeSentences.Include(s => s.Audio).FirstOrDefaultAsync(s => s.Id == id);
        if (sentence is null) {
            return "That sentence no longer exists.";
        }
        if (sentence.InUse) {
            return "Only drafts and sentences needing a fix can be edited; mark it \"needs a fix\" first.";
        }
        if (sentence.Text != text) {
            if (await db.PracticeSentences.AnyAsync(s => s.VocabularyId == sentence.VocabularyId && s.Id != id && s.Text == text)) {
                return "This word already has that sentence.";
            }
            sentence.Text = text;
            if (sentence.Audio is not null) {
                db.PracticeSentenceAudios.Remove(sentence.Audio);
            }
            sentence.HasAudio = false;
        }
        sentence.English = english;
        sentence.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _readings.RequestBackfill();
        return null;
    }

    /// <summary>
    /// Sentences waiting for a native speaker: approved, not yet checked, and not
    /// promoted context sentences (those were written by native speakers). Newest first.
    /// </summary>
    public async Task<List<ProofreadItem>> ToProofreadAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.PracticeSentences.AsNoTracking()
            .Where(s => s.Status == PracticeSentenceStatus.Approved && s.Source != PracticeSentenceSource.Context)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new ProofreadItem(s.Id, s.Text, s.English, s.HasAudio, s.Vocabulary!.Japanese, s.Vocabulary.Reading,
                s.Vocabulary.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).FirstOrDefault() ?? string.Empty))
            .ToListAsync();
    }

    /// <summary>
    /// A native speaker's verdict: OK marks it native-checked; otherwise it's pulled
    /// from practice as "needs a fix", with their note.
    /// </summary>
    public async Task ProofreadAsync(int id, bool ok, string? note)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.PracticeSentences.FindAsync(id) is not { } sentence) {
            return;
        }
        note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        sentence.Status = ok ? PracticeSentenceStatus.NativeChecked : PracticeSentenceStatus.NeedsFix;
        sentence.ReviewNote = ok ? null : note is { Length: > PracticeSentence.MaxNoteLength } ? note[..PracticeSentence.MaxNoteLength] : note;
        sentence.ReviewedAt = DateTime.UtcNow;
        sentence.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<byte[]?> GetAudioAsync(int id)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.PracticeSentenceAudios.AsNoTracking()
            .Where(a => a.PracticeSentenceId == id)
            .Select(a => a.Audio)
            .FirstOrDefaultAsync();
    }
}
