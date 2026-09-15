namespace Brainy.Application.DTOs.Push;

/// <summary>Input for saving push notification settings. Mirrors <see cref="PushNotificationPreferenceDto"/>.</summary>
public record UpdatePushNotificationPreferenceDto(
    bool Enabled,
    bool DailyFocusNudgeEnabled,
    bool OverdueTaskEnabled,
    bool WeeklyReviewReminderEnabled,
    bool QuietHoursEnabled,
    TimeOnly QuietHoursStart,
    TimeOnly QuietHoursEnd,
    int DailyFocusNudgeHour,
    DayOfWeek WeeklyReviewDayOfWeek,
    int WeeklyReviewHour);
