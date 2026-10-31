using KotoNeko.Core.Domain;
using KotoNeko.Data;
using Microsoft.EntityFrameworkCore;

namespace KotoNeko.Web.Services;

/// <summary>Reads and saves the single <see cref="StudySettings"/> row.</summary>
public class SettingsService
{
    private readonly IDbContextFactory<KotoNekoDbContext> _factory;

    public SettingsService(IDbContextFactory<KotoNekoDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<StudySettings> GetAsync()
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        return await LoadAsync(db);
    }

    /// <summary>Clamp the values to sensible ranges and save them.</summary>
    public async Task SaveAsync(StudySettings input)
    {
        await using KotoNekoDbContext db = await _factory.CreateDbContextAsync();
        StudySettings? row = await db.StudySettings.FindAsync(StudySettings.SingletonId);
        if (row is null) {
            row = new StudySettings();
            db.StudySettings.Add(row);
        }

        row.DailyMinutesTarget = Math.Clamp(input.DailyMinutesTarget, 5, 600);
        row.DailyLessonCap = Math.Clamp(input.DailyLessonCap, 0, 500);
        row.DailyGrammarCap = Math.Clamp(input.DailyGrammarCap, 0, 20);
        row.LessonBatchSize = Math.Clamp(input.LessonBatchSize, 1, 50);
        row.DailyDictationCap = Math.Clamp(input.DailyDictationCap, 0, 200);
        row.DailyListeningMinutes = Math.Clamp(input.DailyListeningMinutes, 0, 240);
        string model = (input.ListeningScriptModel ?? "").Trim();
        row.ListeningScriptModel = model.Length > StudySettings.MaxModelChoiceLength ? "" : model;
        row.ListeningEnabled = input.ListeningEnabled;
        row.ListeningCountsTowardSrs = input.ListeningCountsTowardSrs;
        row.SpeechSpeed = Enum.IsDefined(input.SpeechSpeed) ? input.SpeechSpeed : SpeechSpeed.Slow;
        row.KnownWordStage = StudySettings.KnownWordStages.Contains(input.KnownWordStage) ? input.KnownWordStage : SrsStage.Guru1;

        await db.SaveChangesAsync();
    }

    /// <summary>The settings row, or the defaults if it is somehow missing.</summary>
    internal static async Task<StudySettings> LoadAsync(KotoNekoDbContext db) =>
        await db.StudySettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == StudySettings.SingletonId)
        ?? new StudySettings();

    /// <summary>The UTC instant the user's local day began.</summary>
    internal static DateTime StartOfLocalDay(DateTime utcNow) =>
        DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).ToLocalTime().Date.ToUniversalTime();
}
