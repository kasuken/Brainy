namespace Brainy.Application.DTOs.Projects;

/// <summary>Payload for creating a new project.</summary>
public record CreateProjectDto(
    string Name,
    string? Description = null,
    DateTime? DueDate = null,
    bool IsPriority = false,
    Guid? AreaId = null);
