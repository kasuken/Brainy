using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Payload for the Week page's quick project-status mutation.
/// </summary>
/// <param name="ProjectId">The project being changed.</param>
/// <param name="Status">The target status.</param>
/// <param name="RowVersion">The concurrency token captured when the project was loaded.</param>
public record WeekProjectStatusUpdateDto(
    Guid ProjectId,
    ProjectStatus Status,
    byte[]? RowVersion = null);
