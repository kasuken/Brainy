namespace Brainy.Application.DTOs.Push;

/// <summary>Current-user view of their push notification settings.</summary>
public record PushNotificationPreferenceDto(
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
