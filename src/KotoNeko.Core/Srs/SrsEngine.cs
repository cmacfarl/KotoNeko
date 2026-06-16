using KotoNeko.Core.Domain;

namespace KotoNeko.Core.Srs;

/// <summary>
/// Pure SRS state transitions, following WaniKani's advance/penalty rules.
/// Holds no state of its own so it is trivially testable.
/// </summary>
public static class SrsEngine
{
    private const int FirstReviewStage = (int)SrsStage.Apprentice1;
    private const int GuruStage = (int)SrsStage.Guru1;
    private const int BurnedStage = (int)SrsStage.Burned;

    /// <summary>
    /// Unlock a locked item via a lesson: it enters at Apprentice I, due after the
    /// Apprentice I interval.
    /// </summary>
    public static void Unlock(SrsItem item, DateTime now)
    {
        item.Stage = SrsStage.Apprentice1;
        item.UnlockedAt = now;
        item.NextReviewAt = now + SrsSchedule.Intervals[SrsStage.Apprentice1];
        item.BurnedAt = null;
    }

    /// <summary>
    /// Apply the result of a completed review for an item.
    /// </summary>
    /// <param name="item">The SRS item being reviewed.</param>
    /// <param name="incorrectAnswers">
    /// The number of questions answered incorrectly across this item's review
    /// (reading, meaning, conjugation). Zero means the item passed.
    /// </param>
    /// <param name="now">The current time.</param>
    public static void ApplyReview(SrsItem item, int incorrectAnswers, DateTime now)
    {
        if (incorrectAnswers <= 0)
        {
            Advance(item, now);
            item.CorrectCount++;
        }
        else
        {
            Drop(item, incorrectAnswers, now);
            item.IncorrectCount++;
        }
    }

    /// <summary>Move the item up one stage, scheduling the next review.</summary>
    private static void Advance(SrsItem item, DateTime now)
    {
        int next = Math.Min((int)item.Stage + 1, BurnedStage);
        item.Stage = (SrsStage)next;
        ScheduleNext(item, now);
    }

    /// <summary>
    /// Drop the item using WaniKani's penalty: the number of incorrect answers is
    /// halved and rounded up to get the adjustment count, doubled for items at Guru
    /// or above, then subtracted from the current stage. Never falls below
    /// Apprentice I.
    /// </summary>
    private static void Drop(SrsItem item, int incorrectAnswers, DateTime now)
    {
        int adjustmentCount = (int)Math.Ceiling(incorrectAnswers / 2.0);
        int penaltyFactor = (int)item.Stage >= GuruStage ? 2 : 1;
        int next = (int)item.Stage - (adjustmentCount * penaltyFactor);
        if (next < FirstReviewStage)
        {
            next = FirstReviewStage;
        }

        item.Stage = (SrsStage)next;
        ScheduleNext(item, now);
    }

    /// <summary>Set <see cref="SrsItem.NextReviewAt"/> from the current stage.</summary>
    private static void ScheduleNext(SrsItem item, DateTime now)
    {
        if (item.Stage == SrsStage.Burned)
        {
            item.NextReviewAt = null;
            item.BurnedAt = now;
            return;
        }

        item.NextReviewAt = now + SrsSchedule.Intervals[item.Stage];
        item.BurnedAt = null;
    }
}
