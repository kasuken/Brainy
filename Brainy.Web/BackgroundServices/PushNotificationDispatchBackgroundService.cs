using Brainy.Application.Interfaces.Services;

namespace Brainy.Web.BackgroundServices;

/// <summary>
/// Hosted background service that evaluates and sends due Web Push notifications for every
/// opted-in user (issue #315). Runs on a fixed interval via a <see cref="PeriodicTimer"/> and
/// delegates to <see cref="IPushDispatchService.DispatchDueNotificationsAsync"/> via a fresh
/// DI scope on every tick, the same pattern <see cref="ArchiveRetentionBackgroundService"/>
/// uses. A single instance on Brainy's B1 App Service plan is sufficient — there is no other
/// instance to coordinate with (see docs/production-runbook.md) — and every tick is
/// idempotent: it re-evaluates from the persisted frequency-cap log, so a run interrupted by
/// an app restart or deploy loses nothing.
/// </summary>
internal sealed class PushNotificationDispatchBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<PushNotificationDispatchBackgroundService> logger) : BackgroundService
{
    // Short startup delay so the host is fully initialised before the first database hit.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);

    // Frequent enough that the hour-gated daily/weekly triggers fire promptly after their
    // configured hour, without being so frequent that a slow tick overlaps the next one.
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Push notification dispatch background service started. First run in {InitialDelay}.",
            InitialDelay);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down before the initial delay elapsed; exit cleanly.
            return;
        }

        await RunDispatchAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Period);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunDispatchAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunDispatchAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dispatchService = scope.ServiceProvider.GetRequiredService<IPushDispatchService>();

            var result = await dispatchService.DispatchDueNotificationsAsync(stoppingToken).ConfigureAwait(false);

            logger.LogInformation(
                "Push dispatch run complete: {UsersEvaluated} user(s) evaluated, {NotificationsSent} notification(s) sent, {SubscriptionsPruned} expired subscription(s) pruned.",
                result.UsersEvaluated, result.NotificationsSent, result.SubscriptionsPruned);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown — let the cancellation propagate gracefully.
            logger.LogInformation("Push dispatch run cancelled (host shutting down).");
        }
        catch (Exception ex)
        {
            // Log and continue; a single failure must not bring down the host or stop
            // future ticks from running.
            logger.LogError(ex, "Push dispatch run failed: {Message}", ex.Message);
        }
    }
}
