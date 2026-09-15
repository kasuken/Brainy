using Brainy.Application.DTOs.DataExport;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Starts and tracks background Markdown/Obsidian export jobs for the current user. Large
/// accounts export without tying up the request: <see cref="StartExportAsync"/> enqueues the
/// work and returns immediately, and the caller polls <see cref="GetStatusAsync"/> until a
/// download is ready.
/// </summary>
public interface IMarkdownExportJobService
{
    /// <summary>
    /// Enqueues a vault export for the current user. Returns the current user's already
    /// in-flight job instead of starting a second one if one is queued or running.
    /// </summary>
    Task<MarkdownExportJobStatusDto> StartExportAsync(CancellationToken cancellationToken = default);

    /// <summary>Status for <paramref name="jobId"/>, scoped to the current user (null if it belongs to someone else or does not exist).</summary>
    Task<MarkdownExportJobStatusDto?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default);
}
