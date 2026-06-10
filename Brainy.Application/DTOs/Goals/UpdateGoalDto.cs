using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Goals;

public record UpdateGoalDto(
    Guid Id,
    string Title,
    Guid? AreaId,
    string? Description,
    DateTime? TargetDate,
    GoalStatus Status
);
