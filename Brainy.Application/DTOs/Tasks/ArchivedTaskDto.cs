namespace Brainy.Application.DTOs.Tasks;

/// <summary>Read model used by Archives for a soft-archived task.</summary>
public sealed record ArchivedTaskDto(
    Guid Id,
    string Title,
    string? Description,
    Guid ProjectId,
    string ProjectName,
    DateTime ArchivedAtUtc,
    DateTime UpdatedAtUtc,
    bool CanRestore,
    string? ArchivedReason = null);
