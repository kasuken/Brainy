using Brainy.Application.DTOs.Goals;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing GoalMilestones.</summary>
public interface IGoalMilestoneService
{
    /// <summary>Returns all milestones for the given goal, ordered by creation date.</summary>
    Task<IReadOnlyList<GoalMilestoneDto>> GetByGoalAsync(Guid goalId, CancellationToken cancellationToken = default);

    Task<GoalMilestoneDto> CreateAsync(CreateGoalMilestoneDto dto, CancellationToken cancellationToken = default);

    Task<GoalMilestoneDto> UpdateAsync(UpdateGoalMilestoneDto dto, CancellationToken cancellationToken = default);

    /// <summary>Marks the milestone as completed and records the completion timestamp.</summary>
    Task<GoalMilestoneDto> CompleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Removes the completed flag from a milestone.</summary>
    Task<GoalMilestoneDto> UncompleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
