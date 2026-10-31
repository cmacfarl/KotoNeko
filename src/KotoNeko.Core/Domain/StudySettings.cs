namespace KotoNeko.Core.Domain;

/// <summary>
/// The user's study preferences: daily caps, listening behaviour and speech
/// generation speed. A single row (<see cref="SingletonId"/>) edited on the Settings page.
/// Caps limit new work only; due reviews are never capped, since holding them back
/// builds a backlog and breaks the schedule.
/// </summary>
public class StudySettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    /// <summary>Target study time per day. Informational for now.</summary>
    public int DailyMinutesTarget { get; set; } = 60;

    /// <summary>New vocabulary lessons allowed per (local) day. 0 pauses lessons.</summary>
    public int DailyLessonCap { get; set; } = 15;

    /// <summary>New grammar lessons allowed per (local) day, apart from vocabulary's cap. 0 pauses them.</summary>
    public int DailyGrammarCap { get; set; } = 2;

    /// <summary>How many items one lesson session teaches.</summary>
    public int LessonBatchSize { get; set; } = 5;

    /// <summary>Dictation sentences per (local) day. Practice only, so this is a plain cap.</summary>
    public int DailyDictationCap { get; set; } = 10;

    /// <summary>
    /// Extensive listening minutes per (local) day. A soft cap: once reached, writing a
    /// new script asks first; replaying is always allowed.
    /// </summary>
    public int DailyListeningMinutes { get; set; } = 13;

    /// <summary>Whether reviews ask the audio-first listening question.</summary>
    public bool ListeningEnabled { get; set; } = true;

    /// <summary>
    /// When false (the default), listening misses are practice-only like
    /// conjugation: logged, but they never change the SRS stage.
    /// </summary>
    public bool ListeningCountsTowardSrs { get; set; }

    /// <summary>
    /// The speed new speech audio is generated at unless another is picked when
    /// generating. Slow matches every clip generated before this setting existed.
    /// </summary>
    public SpeechSpeed SpeechSpeed { get; set; } = SpeechSpeed.Slow;

    /// <summary>
    /// The SRS stage at which a studied word joins the known-words pool:
    /// <see cref="SrsStage.Guru1"/> or <see cref="SrsStage.Master"/>.
    /// </summary>
    public SrsStage KnownWordStage { get; set; } = SrsStage.Guru1;

    /// <summary>The stages <see cref="KnownWordStage"/> may be set to.</summary>
    public static readonly IReadOnlyList<SrsStage> KnownWordStages = new[] { SrsStage.Guru1, SrsStage.Master };
}
