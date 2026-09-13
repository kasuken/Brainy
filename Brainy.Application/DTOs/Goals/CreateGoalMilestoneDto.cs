namespace Brainy.Application.DTOs.Goals;

public record CreateGoalMilestoneDto(Guid GoalId, string Title, DateTime? DueDate = null);
