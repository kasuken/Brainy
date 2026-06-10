using Brainy.Application.DTOs;
using Brainy.Application.DTOs.Tasks;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Provides all aggregated task data for the Tasks Hub dashboard.</summary>
public interface ITasksHubService
{
    /// <summary>Returns the full hub aggregate for all active tasks.</summary>
    Task<TasksHubAggregateDto> GetHubAggregateAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all active (non-archived, non-done) top-level tasks.</summary>
    Task<IReadOnlyList<TasksHubTaskDto>> GetActiveTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns high-priority (High + Critical) active tasks, ordered by priority then due date.</summary>
    Task<IReadOnlyList<TasksHubTaskDto>> GetHighPriorityTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns tasks with Waiting status (on hold).</summary>
    Task<IReadOnlyList<TasksHubTaskDto>> GetOnHoldTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns active tasks past their due date, oldest first.</summary>
    Task<IReadOnlyList<TasksHubTaskDto>> GetOverdueTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns tasks needing immediate attention: overdue + due today + critical priority.</summary>
    Task<IReadOnlyList<TasksHubTaskDto>> GetTasksNeedingAttentionAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns active tasks without a due date.</summary>
    Task<IReadOnlyList<TasksHubTaskDto>> GetTasksWithoutDueDateAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns active tasks not updated in 30+ days (stale).</summary>
    Task<IReadOnlyList<TasksHubTaskDto>> GetStaleTasksAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns task status distribution counts.</summary>
    Task<TaskStatusSummaryDto> GetStatusSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes an attention score (0–100) for a task based on overdue days, priority, and due date proximity.
    /// Higher score = needs more attention.
    /// </summary>
    int ComputeAttentionScore(TasksHubTaskDto task);

    /// <summary>Searches active tasks by title, description. Supports pagination.</summary>
    Task<PagedResult<TasksHubTaskDto>> SearchTasksAsync(string searchTerm, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>Filters and paginates active tasks by the provided criteria.</summary>
    Task<PagedResult<TasksHubTaskDto>> GetFilteredTasksAsync(TasksHubFilterDto filter, CancellationToken cancellationToken = default);

    /// <summary>Performs bulk operations on the specified tasks.</summary>
    Task<int> BulkOperationAsync(BulkTaskOperationDto dto, CancellationToken cancellationToken = default);
}
