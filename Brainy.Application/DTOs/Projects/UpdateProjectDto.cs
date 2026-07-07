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
    Guid? GoalId = null,
    string? Emoji = null,
    /// <summary>
    /// Concurrency token from the loaded project. When provided, the update fails with a
    /// <see cref="Common.ConcurrencyConflictException"/> if the project changed since it was loaded.
    /// </summary>
    byte[]? RowVersion = null);
