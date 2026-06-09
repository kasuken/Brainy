using Brainy.Application.DTOs.Tasks;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Provides pre-filtered task aggregations for the Today screen.
/// All methods exclude archived tasks and tasks belonging to archived projects.
/// Only top-level tasks (no subtasks) are returned.
/// </summary>
public interface ITodayService
{
    /// <summary>
    /// Returns tasks currently in progress (<see cref="Domain.Enums.TaskItemStatus.InProgress"/>),
    /// ordered by priority then due date.
    /// </summary>
    Task<IReadOnlyList<TodayTaskItemDto>> GetCurrentTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-done tasks with High or Critical priority, ordered by priority then due date.
    /// Capped at 5 to avoid overwhelming the Today view.
    /// </summary>
    Task<IReadOnlyList<TodayTaskItemDto>> GetHighPriorityTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-done tasks whose due date is today, ordered by priority.
    /// </summary>
    Task<IReadOnlyList<TodayTaskItemDto>> GetDueTodayAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-done tasks whose due date is in the past (overdue), ordered by due date ascending.
    /// </summary>
    Task<IReadOnlyList<TodayTaskItemDto>> GetOverdueAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-done tasks due within the next 6 days (excluding today), ordered by due date.
    /// </summary>
    Task<IReadOnlyList<TodayTaskItemDto>> GetDueThisWeekAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-done tasks due 7–21 days from today, capped at 10, ordered by due date.
    /// </summary>
    Task<IReadOnlyList<TodayTaskItemDto>> GetNextTasksAsync(CancellationToken cancellationToken = default);
}
