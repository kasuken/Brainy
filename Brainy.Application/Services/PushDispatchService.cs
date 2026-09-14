using Brainy.Application.Common;
using Brainy.Application.DTOs.Push;
using Brainy.Application.DTOs.Today;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Brainy.Application.Services;

/// <summary>
/// Implements <see cref="IPushDispatchService"/>. For each opted-in user this evaluates the
/// three push categories and sends whichever are due, reusing <see cref="ITodayNotificationService"/>
/// and <see cref="ITodayService"/> exactly as they run for the Today screen — no separate
/// notification model. Each user is evaluated in its own DI scope, impersonated via
/// <see cref="IBackgroundUserContextAccessor"/>, because those services are scoped and depend
/// on <see cref="ICurrentUserService"/>.
/// </summary>
internal sealed class PushDispatchService(
    IApplicationDbContext context,
    IServiceScopeFactory scopeFactory,
    IPushNotificationSender sender,
    TimeProvider timeProvider) : IPushDispatchService
{
    public async Task<PushDispatchRunResultDto> DispatchDueNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var candidateUserIds = await context.PushNotificationPreferences
            .AsNoTracking()
            .Where(p => p.Enabled)
            .Select(p => p.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var usersEvaluated = 0;
        var notificationsSent = 0;
        var subscriptionsPruned = 0;

        foreach (var userId in candidateUserIds)
        {
            var hasSubscription = await context.PushSubscriptions
                .AnyAsync(s => s.UserId == userId, cancellationToken)
                .ConfigureAwait(false);
            if (!hasSubscription)
                continue;

            usersEvaluated++;
            var (sent, pruned) = await DispatchForUserAsync(userId, cancellationToken).ConfigureAwait(false);
            notificationsSent += sent;
            subscriptionsPruned += pruned;
        }

        return new PushDispatchRunResultDto(usersEvaluated, notificationsSent, subscriptionsPruned);
    }

    private async Task<(int Sent, int Pruned)> DispatchForUserAsync(string userId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        // Every scoped service resolved below (IApplicationDbContext, ITodayNotificationService,
        // ITodayService, IUserTimeZoneService) depends transitively on ICurrentUserService;
        // setting this before resolving anything else makes them all evaluate as this user.
        services.GetRequiredService<IBackgroundUserContextAccessor>().UserId = userId;

        var scopedContext = services.GetRequiredService<IApplicationDbContext>();
        var timeZoneService = services.GetRequiredService<IUserTimeZoneService>();
        var todayNotificationService = services.GetRequiredService<ITodayNotificationService>();
        var todayService = services.GetRequiredService<ITodayService>();

        var preference = await scopedContext.PushNotificationPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (preference is null || !preference.Enabled)
            return (0, 0);

        var timeZone = await timeZoneService.GetTimeZoneAsync(cancellationToken).ConfigureAwait(false);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone);
        var localToday = localNow.Date;

        if (preference.QuietHoursEnabled &&
            IsWithinQuietHours(TimeOnly.FromDateTime(localNow), preference.QuietHoursStart, preference.QuietHoursEnd))
        {
            return (0, 0);
        }

        var payloads = new List<(PushNotificationCategory Category, PushNotificationPayload Payload)>();

        if (preference.OverdueTaskEnabled &&
            !await AlreadySentAsync(scopedContext, userId, PushNotificationCategory.OverdueTask, localToday, timeZone, cancellationToken).ConfigureAwait(false))
        {
            var notifications = await todayNotificationService.GetNotificationsAsync(cancellationToken).ConfigureAwait(false);
            var overdue = notifications.FirstOrDefault(n => n.Kind == TodayNotificationKind.OverdueTasks);
            if (overdue is not null)
            {
                payloads.Add((
                    PushNotificationCategory.OverdueTask,
                    new PushNotificationPayload("Overdue", overdue.Message, PushNotificationCategory.OverdueTask)));
            }
        }

        if (preference.DailyFocusNudgeEnabled &&
            localNow.Hour >= preference.DailyFocusNudgeHour &&
            !await AlreadySentAsync(scopedContext, userId, PushNotificationCategory.DailyFocusNudge, localToday, timeZone, cancellationToken).ConfigureAwait(false))
        {
            var notifications = await todayNotificationService.GetNotificationsAsync(cancellationToken).ConfigureAwait(false);
            var dueToday = notifications.FirstOrDefault(n => n.Kind == TodayNotificationKind.DueToday);
            if (dueToday is not null)
            {
                payloads.Add((
                    PushNotificationCategory.DailyFocusNudge,
                    new PushNotificationPayload("Today's focus", dueToday.Message, PushNotificationCategory.DailyFocusNudge)));
            }
        }

        if (preference.WeeklyReviewReminderEnabled &&
            localNow.DayOfWeek == preference.WeeklyReviewDayOfWeek &&
            localNow.Hour >= preference.WeeklyReviewHour &&
            !await AlreadySentThisWeekAsync(scopedContext, userId, localToday, timeZone, cancellationToken).ConfigureAwait(false))
        {
            var planned = await todayService.GetPlannedThisWeekAsync(cancellationToken).ConfigureAwait(false);
            var body = planned.SelectedTaskCount > 0
                ? $"You completed {planned.CompletedTaskCount} of {planned.SelectedTaskCount} planned task(s) this week. Time to plan next week."
                : "Time for your weekly review.";
            payloads.Add((
                PushNotificationCategory.WeeklyReviewReminder,
                new PushNotificationPayload("Weekly review", body, PushNotificationCategory.WeeklyReviewReminder)));
        }

        if (payloads.Count == 0)
            return (0, 0);

        var subscriptions = await scopedContext.PushSubscriptions
            .Where(s => s.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var sentCount = 0;
        var prunedCount = 0;

        foreach (var (category, payload) in payloads)
        {
            var anySentForCategory = false;

            foreach (var subscription in subscriptions.ToList())
            {
                var result = await sender.SendAsync(
                    new PushSubscriptionEndpointDto(subscription.Endpoint, subscription.P256dh, subscription.Auth),
                    payload,
                    cancellationToken).ConfigureAwait(false);

                switch (result.Outcome)
                {
                    case PushSendOutcome.Sent:
                        subscription.LastSuccessAtUtc = nowUtc;
                        anySentForCategory = true;
                        break;
                    case PushSendOutcome.Expired:
                        scopedContext.PushSubscriptions.Remove(subscription);
                        subscriptions.Remove(subscription);
                        prunedCount++;
                        break;
                    case PushSendOutcome.Failed:
                    default:
                        break;
                }
            }

            // One delivery-log row per (user, category) per attempt, regardless of how many
            // of the user's devices actually accepted it — the frequency cap is "we tried to
            // reach this user about this category today/this week", not "every device got it".
            scopedContext.PushNotificationDeliveryLogs.Add(new PushNotificationDeliveryLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Category = category,
            });

            if (anySentForCategory)
                sentCount++;
        }

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return (sentCount, prunedCount);
    }

    private static bool IsWithinQuietHours(TimeOnly now, TimeOnly start, TimeOnly end) =>
        start <= end
            ? now >= start && now < end
            : now >= start || now < end;

    private static async Task<bool> AlreadySentAsync(
        IApplicationDbContext context,
        string userId,
        PushNotificationCategory category,
        DateTime localToday,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var lastSentUtc = await context.PushNotificationDeliveryLogs
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Category == category)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => (DateTime?)l.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lastSentUtc is null)
            return false;

        var lastSentLocalDate = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(lastSentUtc.Value, DateTimeKind.Utc), timeZone).Date;
        return lastSentLocalDate == localToday;
    }

    private static async Task<bool> AlreadySentThisWeekAsync(
        IApplicationDbContext context,
        string userId,
        DateTime localToday,
        TimeZoneInfo timeZone,
        CancellationToken cancellationToken)
    {
        var lastSentUtc = await context.PushNotificationDeliveryLogs
            .AsNoTracking()
            .Where(l => l.UserId == userId && l.Category == PushNotificationCategory.WeeklyReviewReminder)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => (DateTime?)l.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (lastSentUtc is null)
            return false;

        var lastSentLocalDate = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(lastSentUtc.Value, DateTimeKind.Utc), timeZone).Date;
        var currentWeekStart = WeekDateHelper.GetWeekContaining(localToday).WeekStartDate;
        var lastSentWeekStart = WeekDateHelper.GetWeekContaining(lastSentLocalDate).WeekStartDate;
        return lastSentWeekStart == currentWeekStart;
    }
}
