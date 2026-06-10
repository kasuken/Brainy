namespace Brainy.Application.DTOs.Goals;

public record GoalMilestoneDto(
    Guid Id,
    Guid GoalId,
    string Title,
    bool IsCompleted,
    DateTime? CompletedAtUtc,
    DateTime CreatedAtUtc
);
