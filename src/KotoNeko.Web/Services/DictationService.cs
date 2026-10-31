using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>Where a dictated sentence comes from.</summary>
public enum DictationSource
{
    /// <summary>A vocabulary word's practice or context sentence.</summary>
    Word,

    /// <summary>A grammar point's example, drafted or written by hand.</summary>
    GrammarExample,

    /// <summary>A sentence you wrote with a grammar point and passed (a composition).</summary>
    YourSentence,

    /// <summary>A casual-speech version of one of your sentences (Phase 6c).</summary>
    Casual,
}

/// <summary>One sentence to dictate.</summary>
/// <param name="StudyItemId">The vocabulary word, or for a grammar sentence the grammar point.</param>
/// <param name="Japanese">The word, or the grammar point's pattern.</param>
/// <param name="TargetStart">Where the item's word is in the sentence (−1 when not found, or none).</param>
/// <param name="Partial">Only the target word is typed; the rest of the sentence is shown.</param>
/// <param name="PracticeSentenceId">The practice sentence, or null for the word's context sentence (or a grammar sentence).</param>
/// <param name="KanaLength">The sentence's spoken length in kana.</param>
/// <param name="GrammarExampleId">For a grammar sentence, the example it is.</param>
/// <param name="CasualVersionId">For a casual version, the version it is.</param>
/// <param name="Original">For a casual version, the standard sentence it's a version of.</param>
/// <param name="DetailUrl">For a casual version, the page of the word or grammar point it belongs to.</param>
public sealed record DictationItem(
    int StudyItemId,
    string Japanese,
    string Meaning,
    SentenceView Sentence,
    int TargetStart,
    int TargetLength,
    bool Partial,
    int? PracticeSentenceId,
    string AudioUrl,
    int KanaLength,
    DictationSource Source = DictationSource.Word,
    int? GrammarExampleId = null,
    int? CasualVersionId = null,
    string? Original = null,
    string? DetailUrl = null);

/// <summary>Today's dictation cap and what is available.</summary>
/// <param name="LevelKana">The length ladder's current rung: the longest sentence dictated in full.</param>
public sealed record DictationAllowance(int Cap, int DoneToday, int SentencesAvailable, bool HasDictionary, int LevelKana)
{
    public int LeftToday => Math.Max(0, Cap - DoneToday);
}

/// <summary>How a dictation answer compares with the sentence.</summary>
/// <param name="ExpectedKana">The kana the answer was checked against (from the stored readings).</param>
public sealed record DictationCheck(
    DictationComparison Sound,
    IReadOnlyList<KanjiNote> Writing,
    string ExpectedKana,
    IReadOnlyList<FuriganaPiece> Answer);

/// <summary>
/// Dictation practice, from the practice sentences of words being studied, with
/// each word's context sentence as a fallback. A sentence is dictated in full when
/// every other word in it is known and it fits the length ladder
/// (<see cref="DictationLadder"/>); otherwise only its target word is typed.
/// Up to a third of a session (Dictation II, Phase 6b) is grammar sentences: the
/// approved examples of learned grammar points, your own compositions first, always
/// in full and within the ladder, voiced when picked if they have no audio yet. Up to
/// a fifth (Phase 6c) is approved casual versions of those sentences, in full.
/// Practice only: results are logged and steer what comes up, but never touch the
/// SRS. Answers are checked by sound against the sentence's stored readings.
/// </summary>
public class DictationService
{
    private static readonly TimeSpan EarWindow = TimeSpan.FromDays(90);

    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly SentenceReadingService _readings;
    private readonly PracticeSentenceService _sentences;
    private readonly GrammarPointService _grammar;

    public DictationService(
        IDbContextFactory<KotoNekoDbContext> factory, SentenceReadingService readings, PracticeSentenceService sentences,
        GrammarPointService grammar)
    {
        _factory = factory;
        _readings = readings;
        _sentences = sentences;
        _grammar = grammar;
    }

    public async Task<DictationAllowance> GetAllowanceAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        StudySettings settings = await SettingsService.LoadAsync(db);
        DateTime dayStart = SettingsService.StartOfLocalDay(now);
        int done = await db.DictationAttempts.CountAsync(a => a.PracticedAt >= dayStart);
        int available = await Candidates(db).CountAsync() + await GrammarCandidates(db).CountAsync()
            + (await CasualService.UsableAsync(db)).Count;
        bool hasDictionary = await db.DictionaryImports.AnyAsync();
        return new DictationAllowance(settings.DailyDictationCap, done, available, hasDictionary, await LevelAsync(db));
    }

    /// <summary>The ladder's rung, from full dictations recorded with their length.</summary>
    private static async Task<int> LevelAsync(KotoNekoDbContext db)
    {
        List<double> scores = await db.DictationAttempts.AsNoTracking()
            .Where(a => !a.Partial && a.KanaLength > 0)
            .OrderBy(a => a.PracticedAt)
            .Select(a => a.Score)
            .ToListAsync();
        return DictationLadder.Level(scores);
    }

    /// <summary>
    /// Pick today's sentences: up to a third grammar sentences
    /// (<see cref="GrammarSentencesAsync"/>), up to a fifth casual versions
    /// (<see cref="CasualSentencesAsync"/>), the rest words' sentences
    /// (<see cref="WordSentencesAsync"/>), each filling in when another runs short,
    /// with the grammar and casual sentences spread through the session.
    /// </summary>
    public async Task<List<DictationItem>> BuildSessionAsync(DateTime now, int count)
    {
        if (count <= 0) {
            return new List<DictationItem>();
        }
        int level;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            level = await LevelAsync(db);
        }
        List<DictationItem> grammar = await GrammarSentencesAsync(level, count);
        List<DictationItem> casual = await CasualSentencesAsync(level, count);
        int grammarWanted = Math.Min(grammar.Count, DictationMix.GrammarShare(count));
        int casualWanted = Math.Min(casual.Count, DictationMix.CasualShare(count));
        List<DictationItem> words = await WordSentencesAsync(now, count - grammarWanted - casualWanted);
        // Short of words: grammar sentences fill in first, then casual versions.
        int grammarUsed = Math.Min(grammar.Count, count - words.Count - casualWanted);
        int casualUsed = Math.Min(casual.Count, count - words.Count - grammarUsed);
        return DictationMix.Spread(DictationMix.Spread(words, grammar.Take(grammarUsed).ToList()), casual.Take(casualUsed).ToList());
    }

    /// <summary>
    /// Casual versions: approved and voiced, their sentence still in use and unchanged;
    /// never dictated first, then lowest last score, then longest ago. Always in full
    /// and within the ladder. The known-words check was passed by the sentence they
    /// come from, so it isn't applied again (contracted endings could trip it).
    /// </summary>
    private async Task<List<DictationItem>> CasualSentencesAsync(int level, int count)
    {
        List<CasualView> usable;
        Dictionary<int, (double Score, DateTime At)> last;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            usable = await CasualService.UsableAsync(db);
            last = (await db.DictationAttempts.AsNoTracking()
                    .Where(a => a.CasualVersionId != null)
                    .Select(a => new { a.CasualVersionId, a.Score, a.PracticedAt })
                    .ToListAsync())
                .GroupBy(a => a.CasualVersionId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.PracticedAt).Select(a => (a.Score, a.PracticedAt)).First());
        }
        if (usable.Count == 0 || count <= 0) {
            return new List<DictationItem>();
        }

        List<string> texts = usable.Select(u => u.Version.Text).Distinct().ToList();
        await _readings.EnsureAsync(texts);
        Dictionary<string, SentenceView> views = await _readings.GetManyAsync(texts);

        Random random = new();
        List<DictationItem> items = new();
        foreach (CasualView u in usable
                     .OrderBy(u => last.ContainsKey(u.Version.Id) ? 1 : 0)
                     .ThenBy(u => last.TryGetValue(u.Version.Id, out var a) ? Math.Round(a.Score, 1) : 0)
                     .ThenBy(u => last.TryGetValue(u.Version.Id, out var a) ? a.At : DateTime.MinValue)
                     .ThenBy(_ => random.Next())) {
            if (items.Count >= count) {
                break;
            }
            if (!views.TryGetValue(u.Version.Text, out SentenceView? view)) {
                continue;
            }
            int kana = SentenceCheck.KanaLength(view.Pieces);
            if (kana == 0 || kana > level) {
                continue;
            }
            CasualCandidate source = u.Source!;
            items.Add(new DictationItem(source.StudyItemId, source.Label, source.LabelMeaning, view, -1, 0, Partial: false, null,
                u.AudioUrl, kana, DictationSource.Casual, CasualVersionId: u.Version.Id, Original: u.Version.Original,
                DetailUrl: source.GrammarExampleId is null ? $"/vocab/{source.StudyItemId}" : $"/grammar/point/{source.StudyItemId}"));
        }
        return items;
    }

    /// <summary>
    /// Grammar sentences: approved examples of learned, awake grammar points, your own
    /// first (compositions, then ones written by hand), then never dictated, lowest
    /// last score and longest ago. Always dictated in full and within the ladder; a
    /// drafted example also has to use only words you know (your own needn't: you
    /// wrote them). Ones with no audio are voiced as they're picked; any that can't
    /// be are skipped.
    /// </summary>
    private async Task<List<DictationItem>> GrammarSentencesAsync(int level, int count)
    {
        var examples = new List<(int Id, string Text, GrammarExampleSource Source, bool HasAudio, int PointId, string Pattern, string Meaning)>();
        Dictionary<int, (double Score, DateTime At)> last;
        Func<string, bool> isKnown;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            examples = (await GrammarCandidates(db)
                    .Select(x => new { x.Id, x.Text, x.Source, x.HasAudio, x.GrammarPointId, x.GrammarPoint!.Entry!.Pattern, x.GrammarPoint.Entry.Meaning })
                    .ToListAsync())
                .Select(x => (x.Id, x.Text, x.Source, x.HasAudio, x.GrammarPointId, x.Pattern, x.Meaning))
                .ToList();
            last = (await db.DictationAttempts.AsNoTracking()
                    .Where(a => a.GrammarExampleId != null)
                    .Select(a => new { a.GrammarExampleId, a.Score, a.PracticedAt })
                    .ToListAsync())
                .GroupBy(a => a.GrammarExampleId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.PracticedAt).Select(a => (a.Score, a.PracticedAt)).First());
            isKnown = await PracticeSentenceService.PoolAsync(db);
        }
        if (examples.Count == 0) {
            return new List<DictationItem>();
        }

        List<string> texts = examples.Select(x => x.Text).Distinct().ToList();
        await _readings.EnsureAsync(texts);
        Dictionary<string, SentenceView> views = await _readings.GetManyAsync(texts);

        Random random = new();
        List<DictationItem> items = new();
        foreach (var x in examples
                     .OrderBy(x => DictationMix.SourceOrder(x.Source))
                     .ThenBy(x => last.ContainsKey(x.Id) ? 1 : 0)
                     .ThenBy(x => last.TryGetValue(x.Id, out var a) ? Math.Round(a.Score, 1) : 0)
                     .ThenBy(x => last.TryGetValue(x.Id, out var a) ? a.At : DateTime.MinValue)
                     .ThenBy(_ => random.Next())) {
            if (items.Count >= count) {
                break;
            }
            if (!views.TryGetValue(x.Text, out SentenceView? view)) {
                continue;
            }
            SentenceCheckResult check = PracticeSentenceService.Check(view, string.Empty, isKnown);
            bool own = x.Source != GrammarExampleSource.Generated;
            if (check.KanaLength == 0 || check.KanaLength > level || (!own && !check.Comprehensible)) {
                continue;
            }
            if (!x.HasAudio && (await _grammar.GenerateExampleAudioAsync(x.Id) is not null)) {
                // Couldn't be voiced (no TTS key, or a failure): skip it this time.
                continue;
            }
            items.Add(new DictationItem(x.PointId, x.Pattern, x.Meaning, view, -1, 0, Partial: false, null,
                $"/api/grammar-example/{x.Id}/audio", check.KanaLength,
                x.Source == GrammarExampleSource.Composition ? DictationSource.YourSentence : DictationSource.GrammarExample, x.Id));
        }
        return items;
    }

    /// <summary>
    /// Words' sentences, one per word. Words come never-practised first, then
    /// by lowest last score, then weakest by ear, then longest unpractised. For
    /// each word: the longest practice sentence it can dictate in full, else its
    /// context sentence in full, else a sentence where only the word is typed.
    /// </summary>
    private async Task<List<DictationItem>> WordSentencesAsync(DateTime now, int count)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        var candidates = await Candidates(db)
            .Select(v => new {
                v.Id, v.Japanese, v.ContextSentence, v.HasSentenceAudio,
                Meaning = v.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).FirstOrDefault(),
            })
            .ToListAsync();
        if (candidates.Count == 0 || count <= 0) {
            return new List<DictationItem>();
        }

        var attempts = (await db.DictationAttempts.AsNoTracking()
                .Select(a => new { a.StudyItemId, a.Score, a.PracticedAt })
                .ToListAsync())
            .GroupBy(a => a.StudyItemId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.PracticedAt).First());

        DateTime since = now - EarWindow;
        var ear = (await db.ReviewLogs.AsNoTracking()
                .Where(r => r.QuestionType == QuestionType.Listening && r.ReviewedAt >= since)
                .Select(r => new { r.StudyItemId, r.WasCorrect })
                .ToListAsync())
            .GroupBy(r => r.StudyItemId)
            .ToDictionary(g => g.Key, g => g.Average(r => r.WasCorrect ? 1.0 : 0.0));

        Random random = new();
        // A few spare words, in case some sentences can't be used.
        var ordered = candidates
            .OrderBy(c => attempts.ContainsKey(c.Id) ? 1 : 0)
            .ThenBy(c => attempts.TryGetValue(c.Id, out var a) ? Math.Round(a.Score, 1) : 0)
            .ThenBy(c => ear.GetValueOrDefault(c.Id, 1.0))
            .ThenBy(c => attempts.TryGetValue(c.Id, out var a) ? a.PracticedAt : DateTime.MinValue)
            .ThenBy(_ => random.Next())
            .Take(count * 2 + 5)
            .ToList();

        int level = await LevelAsync(db);
        Dictionary<int, List<CheckedSentence>> practice = await _sentences.CheckedForAsync(
            ordered.Select(c => c.Id).ToList(), inUseOnly: true, ensureReadings: true);

        // Readings are normally there already; make sure, then skip any that can't be read.
        List<string> contexts = ordered.Where(c => c.HasSentenceAudio && !string.IsNullOrWhiteSpace(c.ContextSentence))
            .Select(c => c.ContextSentence!).ToList();
        await _readings.EnsureAsync(contexts);
        Dictionary<string, SentenceView> contextViews = await _readings.GetManyAsync(contexts);
        Func<string, bool> isKnown = await PracticeSentenceService.PoolAsync(db);

        List<DictationItem> items = new();
        foreach (var c in ordered) {
            if (items.Count >= count) {
                break;
            }

            // Every sentence this word could use, practice sentences first.
            List<(SentenceView View, SentenceCheckResult Check, int? Id, string Url)> options = new();
            foreach (CheckedSentence p in practice.GetValueOrDefault(c.Id) ?? new List<CheckedSentence>()) {
                if (p.Sentence.HasAudio && p.View is not null && p.Check is not null) {
                    options.Add((p.View, p.Check, p.Sentence.Id, p.AudioUrl));
                }
            }
            if (c.HasSentenceAudio && c.ContextSentence is not null
                && contextViews.TryGetValue(c.ContextSentence, out SentenceView? contextView)) {
                options.Add((contextView, PracticeSentenceService.Check(contextView, c.Japanese, isKnown), null, $"/api/vocab/{c.Id}/audio/sentence"));
            }

            var full = options
                .Where(o => o.Check.HasTarget && o.Check.Comprehensible && o.Check.KanaLength <= level)
                .OrderBy(o => o.Id is null ? 1 : 0)
                .ThenByDescending(o => o.Check.KanaLength)
                .ThenBy(_ => random.Next())
                .FirstOrDefault();
            var partial = options
                .Where(o => o.Check.HasTarget)
                .OrderBy(o => o.Id is null ? 1 : 0)
                .ThenBy(o => o.Check.KanaLength)
                .FirstOrDefault();

            var (view, check, id, url) = full.View is not null ? full : partial;
            if (view is null) {
                continue;
            }
            items.Add(new DictationItem(c.Id, c.Japanese, c.Meaning ?? string.Empty, view,
                check.TargetStart, check.TargetLength, Partial: full.View is null, id, url, check.KanaLength));
        }
        return items;
    }

    /// <summary>Check an answer against the sentence (or just its target word).</summary>
    public async Task<DictationCheck> CheckAsync(DictationItem item, JapaneseText typed)
    {
        SentenceView view = item.Sentence;
        int start = item.Partial ? item.TargetStart : 0;
        int end = item.Partial ? item.TargetStart + item.TargetLength : view.Text.Length;

        IReadOnlyList<FuriganaPiece> answer = Slice(view.Pieces, start, end);
        HashSet<int> particles = view.ParticlePositions().Where(p => p >= start && p < end).Select(p => p - start).ToHashSet();
        (string expectedKana, HashSet<int> particleKana) = DictationComparer.Speech(answer, particles);

        // What was typed, as words with readings: from the input when it knows the
        // kana, otherwise worked out the same way as the sentence's readings.
        SentenceReader reader = new(new DictionaryService(_factory));
        IReadOnlyList<SentenceWord> typedWords = await reader.ReadAsync(typed.Text);
        string typedKana = typed.Kana ?? Furigana.KanaText(SentenceReader.ToFurigana(typedWords));

        DictationComparison sound = DictationComparer.Compare(expectedKana, typedKana, particleKana);

        List<(string, string)> expectedWords = view.Words
            .Where(w => w.Start >= start && w.Start + w.Length <= end)
            .Select(w => (view.Text.Substring(w.Start, w.Length),
                Furigana.KanaText(Slice(view.Pieces, w.Start, w.Start + w.Length))))
            .ToList();
        IReadOnlyList<KanjiNote> writing = DictationComparer.CompareWriting(
            expectedWords, typedWords.Select(w => (w.Text, w.Reading ?? w.Text)));

        return new DictationCheck(sound, writing, expectedKana, answer);
    }

    public async Task RecordAsync(DictationItem item, string typed, double score, DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        db.DictationAttempts.Add(new DictationAttempt {
            StudyItemId = item.StudyItemId,
            PracticeSentenceId = item.PracticeSentenceId,
            GrammarExampleId = item.GrammarExampleId,
            CasualVersionId = item.CasualVersionId,
            KanaLength = item.KanaLength,
            Sentence = item.Sentence.Text,
            Typed = typed.Length > SentenceReading.MaxTextLength ? typed[..SentenceReading.MaxTextLength] : typed,
            Partial = item.Partial,
            Score = score,
            PracticedAt = now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Learned, awake vocabulary with something to dictate: a voiced practice
    /// sentence in use, or a voiced context sentence.
    /// </summary>
    private static IQueryable<Vocabulary> Candidates(KotoNekoDbContext db) =>
        db.Vocabulary.AsNoTracking().Where(v =>
            !v.IsAsleep
            && v.Srs != null && v.Srs.Stage != SrsStage.Locked
            && ((v.HasSentenceAudio && v.ContextSentence != null && v.ContextSentence != "")
                || db.PracticeSentences.Any(s => s.VocabularyId == v.Id && s.HasAudio
                    && (s.Status == PracticeSentenceStatus.Approved || s.Status == PracticeSentenceStatus.NativeChecked))));

    /// <summary>Approved examples of learned, awake grammar points whose list entry isn't pruned.</summary>
    private static IQueryable<GrammarExample> GrammarCandidates(KotoNekoDbContext db) =>
        db.GrammarExamples.AsNoTracking().Where(x =>
            x.Status == GrammarContentStatus.Approved
            && x.GrammarPoint!.Srs != null && x.GrammarPoint.Srs.Stage != SrsStage.Locked
            && !x.GrammarPoint.IsAsleep && !x.GrammarPoint.Entry!.IsPruned);

    /// <summary>The pieces covering characters [start, end) of the text.</summary>
    public static IReadOnlyList<FuriganaPiece> Slice(IReadOnlyList<FuriganaPiece> pieces, int start, int end)
    {
        List<FuriganaPiece> result = new();
        int position = 0;
        foreach (FuriganaPiece p in pieces) {
            int pieceEnd = position + p.Text.Length;
            if (pieceEnd > start && position < end) {
                if (p.Reading is null) {
                    // Plain text can be cut exactly.
                    int from = Math.Max(start, position) - position;
                    int to = Math.Min(end, pieceEnd) - position;
                    result.Add(new FuriganaPiece(p.Text[from..to], null));
                } else {
                    result.Add(p);
                }
            }
            position = pieceEnd;
        }
        return result;
    }
}
