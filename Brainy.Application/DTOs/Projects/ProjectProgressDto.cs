namespace Brainy.Application.DTOs.Projects;

/// <summary>
/// Lightweight snapshot of a project's completion state.
/// Returned by <see cref="Interfaces.Services.IProjectService.GetProjectProgressAsync"/>.
/// </summary>
public record ProjectProgressDto(
    Guid ProjectId,
    int TotalTaskCount,
    int DoneTaskCount,
    int OpenTaskCount,
    double ProgressPercent,
    DateTime CalculatedAtUtc);
