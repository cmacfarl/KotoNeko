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

    /// <summary>How many items one lesson session teaches.</summary>
    public int LessonBatchSize { get; set; } = 5;

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
}
