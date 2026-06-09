using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Projects;

/// <summary>Payload for creating a new project.</summary>
public record CreateProjectDto(
    string Name,
    string? Description = null,
    string? DesiredOutcome = null,
    ProjectStatus Status = ProjectStatus.NotStarted,
    ProjectPriority Priority = ProjectPriority.Medium,
    DateTime? StartDate = null,
    DateTime? DueDate = null,
    Guid? AreaId = null);
