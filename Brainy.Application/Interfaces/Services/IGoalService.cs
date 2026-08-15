using Brainy.Application.DTOs.Goals;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing Goals.</summary>
public interface IGoalService
{
    /// <summary>Returns all non-archived, non-abandoned goals.</summary>
    Task<IReadOnlyList<GoalDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all non-archived goals.</summary>
    Task<IReadOnlyList<GoalDto>> GetAllNonArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all archived goals.</summary>
    Task<IReadOnlyList<GoalDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all non-archived goals for a specific Area.</summary>
    Task<IReadOnlyList<GoalDto>> GetByAreaAsync(Guid areaId, CancellationToken cancellationToken = default);

    /// <summary>Returns the full goal workspace including milestones and linked projects.</summary>
    Task<GoalDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GoalDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<GoalDto> CreateAsync(CreateGoalDto dto, CancellationToken cancellationToken = default);

    Task<GoalDto> UpdateAsync(UpdateGoalDto dto, CancellationToken cancellationToken = default);

    /// <summary>Archives the goal — removes it from active views.</summary>
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null);

    /// <summary>Restores an archived goal back to Planned status.</summary>
    Task<GoalDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a goal and all its milestones.</summary>
    Task DeleteAsync(Guid id, byte[]? rowVersion, CancellationToken cancellationToken = default);

    /// <summary>Returns progress percentage (0–100) based on completed milestones.</summary>
    Task<int> GetProgressAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns strategic metrics across all goals for the current user.</summary>
    Task<GoalMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns non-archived, non-achieved goals with TargetDate within the next <paramref name="daysAhead"/> days.</summary>
    Task<IReadOnlyList<GoalDto>> GetDueSoonAsync(int daysAhead = 7, CancellationToken cancellationToken = default);

    /// <summary>Returns non-archived, non-achieved goals whose TargetDate is in the past.</summary>
    Task<IReadOnlyList<GoalDto>> GetOverdueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full activity log for a goal in chronological order (oldest first).
    /// Includes edits, status changes, and lifecycle events recorded by the service layer.
    /// </summary>
    Task<IReadOnlyList<GoalActivityDto>> GetActivitiesAsync(Guid goalId, CancellationToken cancellationToken = default);
}
