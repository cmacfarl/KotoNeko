using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Srs;

/// <summary>
/// The WaniKani-style SRS interval table and stage metadata. Intervals match
/// WaniKani's default schedule.
/// </summary>
public static class SrsSchedule
{
    /// <summary>
    /// Time until the next review after <em>arriving</em> at a given stage.
    /// Locked and Burned have no scheduled review.
    /// </summary>
    public static readonly IReadOnlyDictionary<SrsStage, TimeSpan> Intervals =
        new Dictionary<SrsStage, TimeSpan>
        {
            [SrsStage.Apprentice1] = TimeSpan.FromHours(4),
            [SrsStage.Apprentice2] = TimeSpan.FromHours(8),
            [SrsStage.Apprentice3] = TimeSpan.FromHours(23),
            [SrsStage.Apprentice4] = TimeSpan.FromHours(47),
            [SrsStage.Guru1] = TimeSpan.FromHours(167),    // ~1 week
            [SrsStage.Guru2] = TimeSpan.FromHours(335),    // ~2 weeks
            [SrsStage.Master] = TimeSpan.FromHours(719),   // ~1 month
            [SrsStage.Enlightened] = TimeSpan.FromHours(2879), // ~4 months
        };

    /// <summary>The broad band a stage belongs to (for dashboard grouping/colours).</summary>
    public static string BandName(SrsStage stage) => stage switch
    {
        SrsStage.Apprentice1 or SrsStage.Apprentice2 or SrsStage.Apprentice3 or SrsStage.Apprentice4 => "Apprentice",
        SrsStage.Guru1 or SrsStage.Guru2 => "Guru",
        SrsStage.Master => "Master",
        SrsStage.Enlightened => "Enlightened",
        SrsStage.Burned => "Burned",
        _ => "Locked",
    };

    /// <summary>A human label for a stage, e.g. "Apprentice II".</summary>
    public static string DisplayName(SrsStage stage) => stage switch
    {
        SrsStage.Locked => "Locked",
        SrsStage.Apprentice1 => "Apprentice I",
        SrsStage.Apprentice2 => "Apprentice II",
        SrsStage.Apprentice3 => "Apprentice III",
        SrsStage.Apprentice4 => "Apprentice IV",
        SrsStage.Guru1 => "Guru I",
        SrsStage.Guru2 => "Guru II",
        SrsStage.Master => "Master",
        SrsStage.Enlightened => "Enlightened",
        SrsStage.Burned => "Burned",
        _ => stage.ToString(),
    };
}
