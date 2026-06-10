using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Goals;

public record GoalDetailDto(
    Guid Id,
    string Title,
    string? Description,
    Guid? AreaId,
    string? AreaName,
    GoalStatus Status,
    DateTime? TargetDate,
    DateTime? AchievedDate,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    int TotalMilestones,
    int CompletedMilestones,
    int ProgressPercent,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<GoalMilestoneDto> Milestones,
    IReadOnlyList<LinkedProjectDto> Projects
);
