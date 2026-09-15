using Brainy.Application.DTOs.DataExport;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Application.Services.MarkdownExport;

/// <summary>
/// Request-scoped façade over the process-local job queue/store: resolves the current user
/// and enforces that a user has at most one Markdown export in flight at a time.
/// </summary>
internal sealed class MarkdownExportJobService(
    ICurrentUserService currentUser,
    IMarkdownExportJobQueue queue,
    IMarkdownExportJobStore store,
    TimeProvider timeProvider) : IMarkdownExportJobService
{
    public async Task<MarkdownExportJobStatusDto> StartExportAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var active = store.FindActiveJobForUser(userId);
        if (active is not null)
            return active;

        var jobId = Guid.NewGuid();
        var createdAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var status = store.CreateQueued(jobId, userId, createdAtUtc);
        queue.Enqueue(new MarkdownExportJobRequest(jobId, userId));
        return status;
    }

    public async Task<MarkdownExportJobStatusDto?> GetStatusAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return store.GetStatus(jobId, userId);
    }
}
