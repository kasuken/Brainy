using Brainy.Application.DTOs.Today;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Application.Services;

/// <summary>
/// Evaluates the user's current task state and emits a prioritised list of notifications
/// for the Today screen (overdue, due-today, upcoming deadlines, growing inbox).
/// </summary>
internal sealed class TodayNotificationService(
    ITodayService todayService,
    IUserDashboardPreferenceService preferenceService) : ITodayNotificationService
{
    public async Task<IReadOnlyList<TodayNotificationDto>> GetNotificationsAsync(
        CancellationToken cancellationToken = default)
    {
        // Queries share the same scoped DbContext, so they must run sequentially.
        var overdue = await todayService.GetOverdueAsync(cancellationToken).ConfigureAwait(false);
        var dueToday = await todayService.GetDueTodayAsync(cancellationToken).ConfigureAwait(false);
        var dueThisWeek = await todayService.GetDueThisWeekAsync(cancellationToken).ConfigureAwait(false);
        var inboxCount = await todayService.GetInboxCountAsync(cancellationToken).ConfigureAwait(false);
        var prefs = await preferenceService.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);

        var notifications = new List<TodayNotificationDto>(4);

        if (overdue.Count > 0)
            notifications.Add(new TodayNotificationDto(
                TodayNotificationKind.OverdueTasks,
                $"You have {overdue.Count} overdue task(s)",
                overdue.Count));

        if (dueToday.Count > 0)
            notifications.Add(new TodayNotificationDto(
                TodayNotificationKind.DueToday,
                $"{dueToday.Count} task(s) due today",
                dueToday.Count));

        if (dueThisWeek.Count > 3)
            notifications.Add(new TodayNotificationDto(
                TodayNotificationKind.UpcomingDeadlines,
                $"{dueThisWeek.Count} tasks due this week",
                dueThisWeek.Count));

        if (inboxCount >= prefs.InboxWarningThreshold)
            notifications.Add(new TodayNotificationDto(
                TodayNotificationKind.GrowingInbox,
                $"Inbox has {inboxCount} unprocessed items",
                inboxCount));

        return notifications;
    }
}
