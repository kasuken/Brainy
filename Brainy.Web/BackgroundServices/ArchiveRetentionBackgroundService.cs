using Brainy.Application.Interfaces.Services;

namespace Brainy.Web.BackgroundServices;

/// <summary>
/// Hosted background service that enforces archive retention policies for all users once per day.
/// Runs on a 24-hour <see cref="PeriodicTimer"/> and delegates the actual deletion work to
/// <see cref="IArchiveRetentionService.EnforceRetentionAsync"/> via a fresh DI scope on every tick,
/// because <see cref="IArchiveRetentionService"/> is registered as scoped while this service is singleton.
/// </summary>
internal sealed class ArchiveRetentionBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<ArchiveRetentionBackgroundService> logger) : BackgroundService
{
    // Short startup delay so the host is fully initialised before the first database hit.
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Period = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Archive retention background service started. First run in {InitialDelay}.",
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

        // Run immediately once the initial delay has elapsed, then on each 24-hour tick.
        await RunRetentionAsync(stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(Period);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunRetentionAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunRetentionAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Archive retention job started.");

        try
        {
            // Create a scoped lifetime so EF Core DbContext and scoped services are properly owned.
            await using var scope = scopeFactory.CreateAsyncScope();
            var retentionService = scope.ServiceProvider.GetRequiredService<IArchiveRetentionService>();

            var purgedCount = await retentionService
                .EnforceRetentionAsync(stoppingToken)
                .ConfigureAwait(false);

            logger.LogInformation("Purged {Count} archived items.", purgedCount);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown — let the cancellation propagate gracefully.
            logger.LogInformation("Archive retention job cancelled (host shutting down).");
        }
        catch (Exception ex)
        {
            // Log and continue; a single failure must not bring down the host.
            logger.LogError(ex, "Archive retention job failed: {Message}", ex.Message);
        }
    }
}
