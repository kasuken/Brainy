namespace Brainy.Application.DTOs.Goals;

public record GoalMetricsDto(
    int ActiveGoals,
    int AchievedGoals,
    int AbandonedGoals,
    IReadOnlyList<AreaGoalCountDto> GoalsByArea,
    double AverageCompletionRate
);

public record AreaGoalCountDto(Guid AreaId, string AreaName, int GoalCount);
