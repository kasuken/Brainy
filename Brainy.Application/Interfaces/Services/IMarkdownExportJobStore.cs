using Brainy.Application.DTOs.DataExport;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Process-local record of Markdown export job status and, once complete, its file bytes.
/// </summary>
/// <remarks>
/// Deliberately in-memory rather than a new database table: this feature must ship without
/// a schema change, and job records are inherently short-lived (an export either completes
/// within a session's lifetime or the user re-requests it). A restart loses in-flight and
/// completed-but-undownloaded jobs; the user simply starts the export again. This trades
/// durability for zero migration risk, which is the right trade for a one-off download link
/// that a single web process created moments ago.
/// </remarks>
public interface IMarkdownExportJobStore
{
    /// <summary>Records a newly queued job for <paramref name="userId"/>.</summary>
    MarkdownExportJobStatusDto CreateQueued(Guid jobId, string userId, DateTime createdAtUtc);

    /// <summary>
    /// The user's most recent job that is still queued or running, if any — used to avoid
    /// starting a second export while one is already in flight (this export's equivalent of
    /// the JSON export's rate limiting, which relies on the UI disabling its button while a
    /// synchronous export is running; a background job needs the same guarantee enforced
    /// server-side, since nothing here ties a job to one browser tab).
    /// </summary>
    MarkdownExportJobStatusDto? FindActiveJobForUser(string userId);

    /// <summary>Status for <paramref name="jobId"/>, scoped to <paramref name="userId"/> (never another user's job).</summary>
    MarkdownExportJobStatusDto? GetStatus(Guid jobId, string userId);

    /// <summary>The completed file for <paramref name="jobId"/>, scoped to <paramref name="userId"/>, or null if not ready/not found/not owned.</summary>
    MarkdownExportFileDto? GetCompletedFile(Guid jobId, string userId);

    /// <summary>Marks a job running.</summary>
    void MarkRunning(Guid jobId);

    /// <summary>Marks a job completed and stores its file for later download.</summary>
    void MarkCompleted(Guid jobId, MarkdownExportFileDto file, DateTime completedAtUtc);

    /// <summary>Marks a job failed with a user-safe error message.</summary>
    void MarkFailed(Guid jobId, string errorMessage, DateTime completedAtUtc);
}
