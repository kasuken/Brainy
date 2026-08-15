using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Ideas;

/// <summary>Read-only projection of an <see cref="Domain.Entities.Idea"/> for list views.</summary>
public record IdeaDto(
    Guid Id,
    string Title,
    string? Description,
    Guid? AreaId,
    string? AreaName,
    IdeaPriority Priority,
    IdeaStatus Status,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? CommittedProjectId,
    /// <summary>Concurrency token captured at load time; pass back on update or delete.</summary>
    byte[]? RowVersion = null,
    string? ArchivedReason = null);
