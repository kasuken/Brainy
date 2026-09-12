using Brainy.Application.Interfaces.Services;

namespace Brainy.Web.BackgroundServices;

/// <summary>
/// Hosted background service that builds Markdown/Obsidian vault exports off the request
/// path: <c>IMarkdownExportJobService.StartExportAsync</c> enqueues a job and returns
/// immediately, and this service does the actual (potentially slow, for large accounts with
/// many images) work of building the zip, so a large export can never time out the request
/// that started it.
/// </summary>
internal sealed class MarkdownExportBackgroundService(
    IServiceScopeFactory scopeFactory,
    IMarkdownExportJobQueue queue,
    TimeProvider timeProvider,
    ILogger<MarkdownExportBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Markdown export background service started.");

        try
        {
            await foreach (var request in queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunJobAsync(request, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task RunJobAsync(MarkdownExportJobRequest request, CancellationToken stoppingToken)
    {
        // Create a scoped lifetime so EF Core DbContext and other scoped services are
        // properly owned for the duration of this one job, independent of any request.
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IMarkdownExportJobStore>();
        var exportService = scope.ServiceProvider.GetRequiredService<IMarkdownExportService>();

        store.MarkRunning(request.JobId);
        logger.LogInformation("Markdown export job {JobId} started for user {UserId}.", request.JobId, request.UserId);

        try
        {
            var file = await exportService.ExportUserAsync(request.UserId, stoppingToken).ConfigureAwait(false);
            store.MarkCompleted(request.JobId, file, timeProvider.GetUtcNow().UtcDateTime);
            logger.LogInformation("Markdown export job {JobId} completed ({Bytes} bytes).", request.JobId, file.Content.Length);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down mid-job; leave the job's state as-is rather than marking it
            // failed, so a still-queued/running record does not claim a false failure reason.
            logger.LogInformation("Markdown export job {JobId} cancelled (host shutting down).", request.JobId);
        }
        catch (Exception ex)
        {
            // A single job's failure must not bring down the consumer loop or the host.
            logger.LogError(ex, "Markdown export job {JobId} failed: {Message}", request.JobId, ex.Message);
            store.MarkFailed(
                request.JobId,
                "Brainy could not build your Markdown export. Try again.",
                timeProvider.GetUtcNow().UtcDateTime);
        }
    }
}
