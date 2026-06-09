namespace Brainy.Application.DTOs.Projects;

/// <summary>Read-only projection of a <see cref="Domain.Entities.Project"/>.</summary>
public record ProjectDto(
    Guid Id,
    string Name,
    string? Description,
    DateTime? DueDate,
    bool IsArchived,
    bool IsPriority,
    Guid? AreaId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
