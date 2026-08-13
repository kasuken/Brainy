using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Goals;

public record UpdateGoalDto(
    Guid Id,
    string Title,
    Guid? AreaId,
    string? Description,
    DateTime? TargetDate,
    GoalStatus Status,
    /// <summary>
    /// Concurrency token from the loaded goal. When provided, the update fails with a
    /// <see cref="Common.ConcurrencyConflictException"/> if the goal changed after it was loaded.
    /// </summary>
    byte[]? RowVersion = null
);
