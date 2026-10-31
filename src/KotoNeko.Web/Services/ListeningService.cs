using System.Text.Json;
using KotoNeko.Core.Domain;
using KotoNeko.Core.Llm;
using KotoNeko.Core.Text;
using KotoNeko.Data;
using KotoNeko.Data.Vocab;
using KotoNeko.Llm;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>A script in the listening library.</summary>
public sealed record ListeningSummary(
    int Id, ListeningKind Kind, string Title, string TitleEnglish, string Writer, DateTime CreatedAt,
    int Lines, int LinesWithAudio, bool Flagged, int Listens, ListeningUnderstanding? LastUnderstood);

/// <summary>A script line with its stored reading and the words in it the user doesn't know yet.</summary>
public sealed record ListeningLineView(ListeningLine Line, SentenceView? View, IReadOnlyList<UnknownWord> Unknown)
{
    public string AudioUrl => $"/api/listening-line/{Line.Id}/audio";
}

/// <summary>A script to listen to, with its lines.</summary>
public sealed record ListeningView(ListeningScript Script, IReadOnlyList<ListeningLineView> Lines);

/// <summary>Today's listening against the daily minutes.</summary>
public sealed record ListeningToday(int Minutes, int Cap)
{
    public bool Reached => Cap > 0 && Minutes >= Cap;
}

/// <summary>A graded comprehension answer.</summary>
public sealed record ComprehensionResult(ComprehensionVerdict Verdict, ListeningUnderstanding Understood);

/// <summary>
/// Extensive listening (Phase 6): stories and dialogues written on request by the
/// model chosen in Settings (or the Generate job's), from the user's known words
/// and grammar, then voiced line by line with Google TTS, one voice per speaker
/// (Tts:ListeningVoices). The script is saved as soon as it's written, so a voicing
/// failure loses nothing; lines without audio can be voiced again. Listening time is
/// logged for the daily minutes, and comprehension answers are graded by the Grade job.
/// </summary>
public sealed class ListeningService
{
    private static readonly IReadOnlyDictionary<string, string> DefaultVoices = new Dictionary<string, string> {
        ["A"] = "ja-JP-Chirp3-HD-Kore",
        ["B"] = "ja-JP-Chirp3-HD-Umbriel",
        ["N"] = "ja-JP-Chirp3-HD-Leda",
    };

    /// <summary>Lines voiced at once.</summary>
    private const int VoicingBatch = 3;

    private readonly IDbContextFactory<KotoNekoDbContext> _factory;
    private readonly LlmService _llm;
    private readonly LlmPricing _pricing;
    private readonly JapaneseTtsService _tts;
    private readonly SentenceReadingService _readings;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ListeningService> _logger;

    public ListeningService(
        IDbContextFactory<KotoNekoDbContext> factory, LlmService llm, LlmPricing pricing, JapaneseTtsService tts,
        SentenceReadingService readings, IConfiguration configuration, ILogger<ListeningService> logger)
    {
        _factory = factory;
        _llm = llm;
        _pricing = pricing;
        _tts = tts;
        _readings = readings;
        _configuration = configuration;
        _logger = logger;
    }

    public bool CanVoice => _tts.IsConfigured;

    /// <summary>Models that can write scripts: the priced ones whose provider is set up.</summary>
    public IReadOnlyList<ModelChoice> ScriptModels() => ModelChoices.Available(_pricing.Models, _llm.IsConfigured);

    /// <summary>
    /// Who writes scripts: the model chosen in Settings when it's still available,
    /// otherwise the Generate job's route. Null when neither is set up.
    /// </summary>
    public ModelChoice? Writer(StudySettings settings)
    {
        if (ModelChoice.Parse(settings.ListeningScriptModel) is { } chosen && _llm.IsConfigured(chosen.Provider)
            && ScriptModels().Any(m => m.Key == chosen.Key)) {
            return chosen;
        }
        return _llm.Route(LlmJob.Generate) is { IsConfigured: true } route ? new ModelChoice(route.Provider, route.Model) : null;
    }

    public async Task<ModelChoice?> WriterAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return Writer(await SettingsService.LoadAsync(db));
    }

    public LlmRoute? GraderRoute => _llm.Route(LlmJob.Grade);

    public async Task<List<ListeningSummary>> LibraryAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        var scripts = await db.ListeningScripts.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new {
                s.Id, s.Kind, s.Title, s.TitleEnglish, s.Writer, s.CreatedAt, s.Flagged,
                Lines = s.Lines.Count, Voiced = s.Lines.Count(l => l.HasAudio),
            })
            .ToListAsync();
        Dictionary<int, int> listens = await db.ListeningLogs.AsNoTracking()
            .GroupBy(l => l.ListeningScriptId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);
        Dictionary<int, ListeningUnderstanding> understood = (await db.ListeningAnswers.AsNoTracking()
                .Select(a => new { a.ListeningScriptId, a.AnsweredAt, a.Understood })
                .ToListAsync())
            .GroupBy(a => a.ListeningScriptId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.AnsweredAt).First().Understood);
        return scripts.Select(s => new ListeningSummary(s.Id, s.Kind, s.Title, s.TitleEnglish, s.Writer, s.CreatedAt,
                s.Lines, s.Voiced, s.Flagged, listens.GetValueOrDefault(s.Id),
                understood.TryGetValue(s.Id, out ListeningUnderstanding u) ? u : null))
            .ToList();
    }

    /// <summary>A script with its lines in order, their readings and unknown words; null when it's gone.</summary>
    public async Task<ListeningView?> GetAsync(int id)
    {
        ListeningScript? script;
        Func<string, bool> isKnown;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            script = await db.ListeningScripts.AsNoTracking().Include(s => s.Lines).FirstOrDefaultAsync(s => s.Id == id);
            if (script is null) {
                return null;
            }
            isKnown = await PracticeSentenceService.PoolAsync(db);
        }
        script.Lines.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        List<string> texts = script.Lines.Select(l => l.Text).ToList();
        await _readings.EnsureAsync(texts);
        Dictionary<string, SentenceView> views = await _readings.GetManyAsync(texts);
        List<ListeningLineView> lines = script.Lines.Select(l => {
            SentenceView? view = views.GetValueOrDefault(l.Text);
            // No target word: every word in the line is checked against the pool.
            IReadOnlyList<UnknownWord> unknown = view is null
                ? Array.Empty<UnknownWord>()
                : PracticeSentenceService.Check(view, string.Empty, isKnown).Unknown;
            return new ListeningLineView(l, view, unknown);
        }).ToList();
        return new ListeningView(script, lines);
    }

    /// <summary>Furigana for a transcript: only over words the user doesn't know yet.</summary>
    public Task<HashSet<string>> KnownWordsAsync() => _readings.KnownWordsAsync();

    /// <summary>
    /// Write a new script and voice it. <paramref name="progress"/> hears each step.
    /// Returns the new script's id and a note when some lines couldn't be voiced.
    /// Throws <see cref="LlmException"/> when no usable script came back; nothing is saved then.
    /// </summary>
    public async Task<(int Id, string? Note)> WriteAsync(
        ListeningKind kind, string topic, Func<string, Task> progress, CancellationToken cancellationToken = default)
    {
        topic = topic.Trim();
        if (topic.Length > ListeningScript.MaxTopicLength) {
            topic = topic[..ListeningScript.MaxTopicLength];
        }

        ModelChoice? writer;
        ScriptRequest request;
        List<string> known;
        List<(string, string)> grammar;
        List<string> titles;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken)) {
            StudySettings settings = await SettingsService.LoadAsync(db);
            writer = Writer(settings)
                ?? throw new LlmException(LlmOutcome.NotConfigured, "No model is set up to write scripts: choose one in Settings, or set Llm:Jobs:Generate.");
            List<string> pool = await db.Vocabulary.AsNoTracking().InPool(settings.KnownWordStage)
                .Select(v => v.Japanese).ToListAsync(cancellationToken);
            known = pool.Distinct().OrderBy(_ => Random.Shared.Next()).Take(ListeningScripts.KnownWordSample).ToList();

            var points = await db.GrammarList.AsNoTracking()
                .Where(e => !e.IsPruned && e.Status != GrammarStatus.NotYet)
                .OrderByDescending(e => e.Level).ThenBy(e => e.SortOrder)
                .Select(e => new { e.Pattern, e.Meaning, e.Level, e.Status })
                .ToListAsync(cancellationToken);
            grammar = points.Select(p => (p.Pattern, p.Meaning)).ToList();
            // The level of the hardest grammar being studied (levels count down), else of the
            // hardest known, else N5.
            JlptLevel level = points.Where(p => p.Status == GrammarStatus.Studying).Select(p => (JlptLevel?)p.Level).Min()
                ?? points.Select(p => (JlptLevel?)p.Level).Min()
                ?? JlptLevel.N5;
            request = new ScriptRequest(kind, level, topic);

            titles = await db.ListeningScripts.AsNoTracking().OrderByDescending(s => s.CreatedAt)
                .Take(ListeningScripts.RecentTitles).Select(s => s.TitleEnglish.Length > 0 ? s.TitleEnglish : s.Title)
                .ToListAsync(cancellationToken);
        }

        await progress($"Writing the {(kind == ListeningKind.Dialogue ? "dialogue" : "story")} with {writer.Provider} · {writer.Model}…");
        LlmResult<ScriptDraft> answer = await _llm.CallOnAsync(writer.Provider, writer.Model,
            ListeningScripts.Build(request, known, grammar, titles), ListeningScripts.Validator(kind), cancellationToken);

        // Paid for: save it whatever happens next, even if the page has gone.
        ListeningScript script = ListeningScripts.ToScript(answer.Value, request,
            $"{answer.Route.Provider} · {answer.Route.Model} · {ListeningScripts.PromptVersion}", DateTime.UtcNow);
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            db.ListeningScripts.Add(script);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        _readings.RequestBackfill();

        string? note = await VoiceAsync(script.Id, progress);
        return (script.Id, note);
    }

    /// <summary>
    /// Voice a script's lines that have no audio yet, a few at a time. Returns a note
    /// when some couldn't be voiced (they can be tried again), or null.
    /// </summary>
    public async Task<string?> VoiceAsync(int scriptId, Func<string, Task>? progress = null)
    {
        if (!_tts.IsConfigured) {
            return "No Google TTS key is set (GoogleTtsApiKey), so the script has no audio yet. Add the key, then use \"Generate missing audio\".";
        }
        List<ListeningLine> lines;
        SpeechSpeed speed;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            lines = await db.ListeningLines.AsNoTracking()
                .Where(l => l.ListeningScriptId == scriptId && !l.HasAudio)
                .OrderBy(l => l.SortOrder)
                .ToListAsync();
            speed = (await SettingsService.LoadAsync(db)).SpeechSpeed;
        }

        int done = 0;
        int failed = 0;
        string? firstError = null;
        foreach (ListeningLine[] batch in lines.Chunk(VoicingBatch)) {
            if (progress is not null) {
                await progress($"Voicing line {done + 1} of {lines.Count}…");
            }
            (ListeningLine Line, byte[]? Audio)[] voiced = await Task.WhenAll(batch.Select(async line => {
                try {
                    TtsResult result = await _tts.SynthesiseWithVoiceAsync(line.Text, VoiceFor(line.Speaker), speed);
                    return (line, result.Audio.Length > 0 ? result.Audio : null);
                } catch (Exception ex) {
                    _logger.LogWarning(ex, "Couldn't voice listening line {Id}.", line.Id);
                    firstError ??= ex.Message;
                    return (line, (byte[]?)null);
                }
            }));
            await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
                foreach ((ListeningLine line, byte[]? audio) in voiced) {
                    if (audio is null) {
                        failed++;
                        continue;
                    }
                    ListeningLine row = await db.ListeningLines.Include(l => l.Audio).FirstAsync(l => l.Id == line.Id);
                    if (row.Audio is null) {
                        row.Audio = new ListeningLineAudio { Audio = audio };
                    } else {
                        row.Audio.Audio = audio;
                    }
                    row.HasAudio = true;
                }
                await db.SaveChangesAsync();
            }
            done += batch.Length;
        }
        return failed == 0 ? null
            : $"{failed} of {lines.Count} line{(lines.Count == 1 ? "" : "s")} couldn't be voiced ({firstError}). Use \"Generate missing audio\" to try again.";
    }

    /// <summary>The voice for a speaker: Tts:ListeningVoices:{A, B or N}, or a default.</summary>
    private string VoiceFor(string speaker) =>
        _configuration[$"Tts:ListeningVoices:{speaker}"] is { Length: > 0 } voice ? voice
        : DefaultVoices.TryGetValue(speaker, out string? fallback) ? fallback
        : DefaultVoices["N"];

    public async Task<byte[]?> GetLineAudioAsync(int lineId)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await db.ListeningLineAudios.AsNoTracking().Where(a => a.ListeningLineId == lineId)
            .Select(a => a.Audio).FirstOrDefaultAsync();
    }

    /// <summary>Record seconds of a script heard, for today's minutes.</summary>
    public async Task LogAsync(int scriptId, double seconds)
    {
        int whole = (int)Math.Round(seconds);
        if (whole <= 0) {
            return;
        }
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (!await db.ListeningScripts.AnyAsync(s => s.Id == scriptId)) {
            return;
        }
        db.ListeningLogs.Add(new ListeningLog { ListeningScriptId = scriptId, ListenedAt = DateTime.UtcNow, Seconds = whole });
        await db.SaveChangesAsync();
    }

    /// <summary>Minutes listened today (the local day) against the daily minutes.</summary>
    public async Task<ListeningToday> TodayAsync(DateTime utcNow)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await TodayAsync(db, utcNow);
    }

    internal static async Task<ListeningToday> TodayAsync(KotoNekoDbContext db, DateTime utcNow)
    {
        DateTime dayStart = SettingsService.StartOfLocalDay(utcNow);
        int seconds = await db.ListeningLogs.Where(l => l.ListenedAt >= dayStart).SumAsync(l => (int?)l.Seconds) ?? 0;
        StudySettings settings = await SettingsService.LoadAsync(db);
        return new ListeningToday((int)Math.Round(seconds / 60.0), settings.DailyListeningMinutes);
    }

    /// <summary>Grade an answer to a script's question, and keep it. Throws <see cref="LlmException"/> when it can't be graded.</summary>
    public async Task<ComprehensionResult> CheckAnswerAsync(int scriptId, string answer, CancellationToken cancellationToken = default)
    {
        answer = answer.Trim();
        if (answer.Length > ListeningAnswer.MaxAnswerLength) {
            answer = answer[..ListeningAnswer.MaxAnswerLength];
        }
        ListeningScript script;
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync(cancellationToken)) {
            script = await db.ListeningScripts.AsNoTracking().Include(s => s.Lines).FirstAsync(s => s.Id == scriptId, cancellationToken);
        }
        LlmResult<ComprehensionVerdict> graded = await _llm.CallWithUsageAsync<ComprehensionVerdict>(
            Comprehension.Build(script, answer), Comprehension.Validate, cancellationToken);
        ListeningUnderstanding understood = Comprehension.Parse(graded.Value.Understood);
        await using (KotoNekoDbContext db = await _factory.CreateDbContextAsync()) {
            db.ListeningAnswers.Add(new ListeningAnswer {
                ListeningScriptId = scriptId,
                AnsweredAt = DateTime.UtcNow,
                Answer = answer,
                Understood = understood,
                VerdictJson = graded.Json,
                PromptVersion = Comprehension.PromptVersion,
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return new ComprehensionResult(graded.Value, understood);
    }

    /// <summary>Flag a script as unnatural or wrong (with a note), or clear the flag.</summary>
    public async Task FlagAsync(int scriptId, bool flagged, string note)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.ListeningScripts.FindAsync(scriptId) is { } script) {
            script.Flagged = flagged;
            note = note.Trim();
            script.FlagNote = flagged ? (note.Length > ListeningScript.MaxFlagNoteLength ? note[..ListeningScript.MaxFlagNoteLength] : note) : "";
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(int scriptId)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        if (await db.ListeningScripts.FindAsync(scriptId) is { } script) {
            db.ListeningScripts.Remove(script);
            await db.SaveChangesAsync();
        }
    }
}
