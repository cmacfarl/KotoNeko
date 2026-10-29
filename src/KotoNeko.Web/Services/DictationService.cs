using KotoNeko.Core.Domain;
using KotoNeko.Core.Ime;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>One sentence to dictate.</summary>
/// <param name="TargetStart">Where the item's word is in the sentence (−1 when not found).</param>
/// <param name="Partial">Only the target word is typed; the rest of the sentence is shown.</param>
public sealed record DictationItem(
    int VocabularyId,
    string Japanese,
    string Meaning,
    SentenceView Sentence,
    int TargetStart,
    int TargetLength,
    bool Partial);

/// <summary>Today's dictation cap and what is available.</summary>
public sealed record DictationAllowance(int Cap, int DoneToday, int SentencesAvailable, bool HasDictionary)
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
/// Dictation practice from vocabulary context sentences that have audio. Practice
/// only: results are logged and steer which sentences come up, but never touch
/// the SRS. Answers are checked by sound against the sentence's stored readings.
/// </summary>
public class DictationService
{
    /// <summary>Sentences longer than this (in kana) dictate only their target word.</summary>
    public const int PartialAboveKana = 25;

    private const int SessionSize = 10;
    private static readonly TimeSpan EarWindow = TimeSpan.FromDays(90);

    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly SentenceReadingService _readings;

    public DictationService(IDbContextFactory<KotoNekoDbContext> factory, SentenceReadingService readings)
    {
        _factory = factory;
        _readings = readings;
    }

    public async Task<DictationAllowance> GetAllowanceAsync(DateTime now)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        StudySettings settings = await SettingsService.LoadAsync(db);
        DateTime dayStart = SettingsService.StartOfLocalDay(now);
        int done = await db.DictationAttempts.CountAsync(a => a.PracticedAt >= dayStart);
        int available = await Candidates(db).CountAsync();
        bool hasDictionary = await db.DictionaryImports.AnyAsync();
        return new DictationAllowance(settings.DailyDictationCap, done, available, hasDictionary);
    }

    /// <summary>
    /// Pick today's sentences: never-practised first, then the lowest last scores,
    /// then words that are weak by ear, then the longest unpractised.
    /// </summary>
    public async Task<List<DictationItem>> BuildSessionAsync(DateTime now, int count)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        var candidates = await Candidates(db)
            .Select(v => new { v.Id, v.Japanese, v.ContextSentence, Meaning = v.Meanings.OrderBy(m => m.SortOrder).Select(m => m.Text).FirstOrDefault() })
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
        var ordered = candidates
            .OrderBy(c => attempts.ContainsKey(c.Id) ? 1 : 0)
            .ThenBy(c => attempts.TryGetValue(c.Id, out var a) ? Math.Round(a.Score, 1) : 0)
            .ThenBy(c => ear.GetValueOrDefault(c.Id, 1.0))
            .ThenBy(c => attempts.TryGetValue(c.Id, out var a) ? a.PracticedAt : DateTime.MinValue)
            .ThenBy(_ => random.Next())
            .Take(count)
            .ToList();

        // Readings are normally there already; make sure, then skip any that can't be read.
        await _readings.EnsureAsync(ordered.Select(c => c.ContextSentence!));
        Dictionary<string, SentenceView> views = await _readings.GetManyAsync(ordered.Select(c => c.ContextSentence!));

        List<DictationItem> items = new();
        foreach (var c in ordered) {
            if (!views.TryGetValue(c.ContextSentence!, out SentenceView? view)) {
                continue;
            }

            (int start, int length) = FindTarget(view, c.Japanese);
            // Measured in spoken kana: punctuation doesn't make a sentence longer to type.
            int kana = DictationComparer.Normalize(Furigana.KanaText(view.Pieces)).Length;
            bool partial = start >= 0 && kana > PartialAboveKana;
            items.Add(new DictationItem(c.Id, c.Japanese, c.Meaning ?? string.Empty, view, start, length, partial));
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
            StudyItemId = item.VocabularyId,
            Sentence = item.Sentence.Text,
            Typed = typed.Length > SentenceReading.MaxTextLength ? typed[..SentenceReading.MaxTextLength] : typed,
            Partial = item.Partial,
            Score = score,
            PracticedAt = now,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Learned, awake vocabulary whose context sentence has audio.</summary>
    private static IQueryable<Vocabulary> Candidates(KotoNekoDbContext db) =>
        db.Vocabulary.AsNoTracking().Where(v =>
            !v.IsAsleep
            && v.HasSentenceAudio
            && v.ContextSentence != null && v.ContextSentence != ""
            && v.Srs != null && v.Srs.Stage != SrsStage.Locked);

    /// <summary>
    /// Where the item's word is in its sentence: a word whose dictionary form is the
    /// item (so 読んだ is found for 読む), else the item written as is.
    /// </summary>
    private static (int Start, int Length) FindTarget(SentenceView view, string japanese)
    {
        StoredWord? word = view.Words.FirstOrDefault(w => w.Lemma == japanese);
        if (word is not null) {
            return (word.Start, word.Length);
        }
        int at = view.Text.IndexOf(japanese, StringComparison.Ordinal);
        return at >= 0 ? (at, japanese.Length) : (-1, 0);
    }

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
