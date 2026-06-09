namespace Brainy.Application.DTOs.Today;

/// <summary>
/// A single actionable notification surfaced on the Today screen.
/// </summary>
public record TodayNotificationDto(
    TodayNotificationKind Kind,
    string Message,
    int Count);

/// <summary>
/// Classifies the reason a Today notification is being shown.
/// </summary>
public enum TodayNotificationKind
{
    OverdueTasks = 1,
    DueToday = 2,
    UpcomingDeadlines = 3,
    GrowingInbox = 4
}
