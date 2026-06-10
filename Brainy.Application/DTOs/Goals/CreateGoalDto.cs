namespace Brainy.Application.DTOs.Goals;

public record CreateGoalDto(
    string Title,
    Guid? AreaId = null,
    string? Description = null,
    DateTime? TargetDate = null
);
