using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Projects;

/// <summary>Payload for updating an existing project.</summary>
public record UpdateProjectDto(
    Guid Id,
    string Name,
    Guid AreaId,
    string? Description,
    string? DesiredOutcome,
    ProjectStatus Status,
    ProjectPriority Priority,
    DateTime? StartDate,
    DateTime? DueDate,
    Guid? GoalId = null);
