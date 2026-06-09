using Brainy.Application.DTOs.Tasks;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Provides task aggregations for the Today screen.
/// Every query enforces: non-archived task, non-archived project, top-level only.
/// </summary>
internal sealed class TodayService(IApplicationDbContext context, ICurrentUserService currentUser) : ITodayService
{
    // Base active-task predicate: excludes archived tasks, tasks from archived projects,
    // done/archived statuses, and subtasks.
    private static readonly TaskItemStatus[] _inactiveStatuses =
        [TaskItemStatus.Done, TaskItemStatus.Archived];

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetCurrentTasksAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await ActiveBase(userId)
            .Where(t => t.Status == TaskItemStatus.InProgress)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetHighPriorityTasksAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await ActiveBase(userId)
            .Where(t => t.Priority >= TaskPriority.High && !_inactiveStatuses.Contains(t.Status))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Take(5)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetDueTodayAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Today;

        return await ActiveBase(userId)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value.Date == today
                        && !_inactiveStatuses.Contains(t.Status))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Title)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetOverdueAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Today;

        return await ActiveBase(userId)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value.Date < today
                        && !_inactiveStatuses.Contains(t.Status))
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetDueThisWeekAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var tomorrow = DateTime.Today.AddDays(1);
        var weekEnd  = DateTime.Today.AddDays(6);

        return await ActiveBase(userId)
            .Where(t => t.DueDate.HasValue
                        && t.DueDate.Value.Date >= tomorrow
                        && t.DueDate.Value.Date <= weekEnd
                        && !_inactiveStatuses.Contains(t.Status))
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetNextTasksAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var nextWeekStart = DateTime.Today.AddDays(7);
        var horizon       = DateTime.Today.AddDays(21);

        return await ActiveBase(userId)
            .Where(t => t.DueDate.HasValue
                        && t.DueDate.Value.Date >= nextWeekStart
                        && t.DueDate.Value.Date <= horizon
                        && !_inactiveStatuses.Contains(t.Status))
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Take(10)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Base queryable that enforces the "active task" contract:
    /// non-archived, belongs to a non-archived project, top-level only, owned by the user.
    /// </summary>
    private IQueryable<Domain.Entities.TaskItem> ActiveBase(string userId) =>
        context.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && !t.IsArchived
                        && !t.Project.IsArchived
                        && t.ParentTaskId == null);

    private static readonly System.Linq.Expressions.Expression<Func<Domain.Entities.TaskItem, TodayTaskItemDto>> ToDto =
        t => new TodayTaskItemDto(
            t.Id,
            t.Title,
            t.Description,
            t.Status,
            t.Priority,
            t.DueDate,
            t.ProjectId,
            t.Project.Name,
            t.CreatedAtUtc);
}
