using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Per-user Web Push settings: the master opt-in, per-category toggles, quiet hours, and
/// the schedule for the two time-based categories. Exactly one row exists per user
/// (enforced by a unique index on <see cref="UserId"/>), created lazily the first time the
/// user opens push settings or toggles anything — a user who never visits push settings has
/// no row at all, which the dispatch query treats identically to "disabled" (issue #315:
/// strictly opt-in, off by default).
/// </summary>
public class PushNotificationPreference : BaseEntity, IUserOwnedEntity
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>Master opt-in switch. False (the default) stops all delivery immediately regardless of the flags below.</summary>
    public bool Enabled { get; set; }

    public bool DailyFocusNudgeEnabled { get; set; } = true;

    public bool OverdueTaskEnabled { get; set; } = true;

    public bool WeeklyReviewReminderEnabled { get; set; } = true;

    /// <summary>When true, no push is sent while the current local time falls in [<see cref="QuietHoursStart"/>, <see cref="QuietHoursEnd"/>).</summary>
    public bool QuietHoursEnabled { get; set; } = true;

    /// <summary>Local start of the quiet-hours window. May be later than <see cref="QuietHoursEnd"/>, meaning the window wraps past midnight.</summary>
    public TimeOnly QuietHoursStart { get; set; } = new(21, 0);

    /// <summary>Local end of the quiet-hours window (exclusive).</summary>
    public TimeOnly QuietHoursEnd { get; set; } = new(8, 0);

    /// <summary>Local hour (0-23) after which the daily focus nudge may be sent.</summary>
    public int DailyFocusNudgeHour { get; set; } = 8;

    /// <summary>Local day of week on which the weekly review reminder may be sent.</summary>
    public DayOfWeek WeeklyReviewDayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>Local hour (0-23) after which the weekly review reminder may be sent.</summary>
    public int WeeklyReviewHour { get; set; } = 17;
}
