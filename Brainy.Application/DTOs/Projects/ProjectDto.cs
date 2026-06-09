using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Projects;

/// <summary>Read-only projection of a <see cref="Domain.Entities.Project"/>.</summary>
public record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    string? DesiredOutcome,
    ProjectStatus Status,
    ProjectPriority Priority,
    DateTime? StartDate,
    DateTime? DueDate,
    DateTime? CompletedDate,
    bool IsArchived,
    Guid? AreaId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? ArchivedAtUtc);
