using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Projects;

/// <summary>
/// Project data enriched with live task statistics — used by the Project List page.
/// </summary>
public record ProjectSummaryDto(
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
    DateTime? ArchivedAtUtc,
    int TotalTaskCount,
    int OpenTaskCount,
    int DoneTaskCount,
    double ProgressPercent,
    int OverdueTaskCount = 0);
