namespace Brainy.Domain.Enums;

/// <summary>
/// The small, deliberately boring set of Web Push triggers Brainy supports (issue #315).
/// Each category has its own opt-in flag on <see cref="Entities.PushNotificationPreference"/>
/// and is capped to at most one delivery per user per period (daily for the first two,
/// weekly for the review reminder) via <see cref="Entities.PushNotificationDeliveryLog"/>.
/// </summary>
public enum PushNotificationCategory
{
    /// <summary>A once-daily nudge summarising what is due today, reusing <c>ITodayNotificationService</c>.</summary>
    DailyFocusNudge = 1,

    /// <summary>Sent when the user has at least one overdue task, at most once per day.</summary>
    OverdueTask = 2,

    /// <summary>A weekly cadence reminder to run the guided weekly review, at most once per week.</summary>
    WeeklyReviewReminder = 3
}
