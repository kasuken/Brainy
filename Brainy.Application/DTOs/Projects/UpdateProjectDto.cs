namespace Brainy.Application.DTOs.Projects;

/// <summary>Payload for updating an existing project.</summary>
public record UpdateProjectDto(
    Guid Id,
    string Name,
    string? Description,
    DateTime? DueDate,
    bool IsPriority,
    Guid? AreaId);
