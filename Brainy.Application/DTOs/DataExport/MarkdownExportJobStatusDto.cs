namespace Brainy.Application.DTOs.DataExport;

/// <summary>Lifecycle state of a background Markdown/Obsidian export job.</summary>
public enum MarkdownExportJobState
{
    Queued,
    Running,
    Completed,
    Failed
}

/// <summary>
/// Point-in-time status of a Markdown export job, safe to expose to the owning user (no
/// file bytes — see <see cref="MarkdownExportFileDto"/> for the completed download).
/// </summary>
public sealed record MarkdownExportJobStatusDto(
    Guid JobId,
    MarkdownExportJobState State,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? FileName,
    string? ErrorMessage);
