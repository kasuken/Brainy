using Brainy.Application.DTOs.Tasks;
using Brainy.Application.DTOs.Today;
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
internal sealed class TodayService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IProjectPrioritizationService projectPrioritizationService) : ITodayService
{
    // Base active-task predicate: excludes archived tasks, tasks from archived projects,
    // done/archived statuses, and subtasks.
    private static readonly TaskItemStatus[] _inactiveStatuses =
        [TaskItemStatus.Done, TaskItemStatus.Archived];

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetCurrentTasksAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Today;

        return await ActiveBase(userId)
            .Where(t => t.Status == TaskItemStatus.InProgress)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Select(BuildToDto(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetHighPriorityTasksAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Today;

        return await ActiveBase(userId)
            .Where(t => t.Priority >= TaskPriority.High && !_inactiveStatuses.Contains(t.Status))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Take(5)
            .Select(BuildToDto(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetDueTodayAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Today;

        return await ActiveBase(userId)
            .Where(t => !_inactiveStatuses.Contains(t.Status)
                        && ((t.DueDate.HasValue && t.DueDate.Value.Date == today)
                            || t.Subtasks.Any(s => !s.IsArchived
                                                   && !_inactiveStatuses.Contains(s.Status)
                                                   && s.DueDate.HasValue
                                                   && s.DueDate.Value.Date == today)))
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Title)
            .Select(BuildToDto(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetOverdueAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = DateTime.Today;

        return await ActiveBase(userId)
            .Where(t => !_inactiveStatuses.Contains(t.Status)
                        && ((t.DueDate.HasValue && t.DueDate.Value.Date < today)
                            || t.Subtasks.Any(s => !s.IsArchived
                                                   && !_inactiveStatuses.Contains(s.Status)
                                                   && s.DueDate.HasValue
                                                   && s.DueDate.Value.Date < today)))
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Select(BuildToDto(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetDueThisWeekAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today    = DateTime.Today;
        var tomorrow = today.AddDays(1);
        var weekEnd  = today.AddDays(6);

        return await ActiveBase(userId)
            .Where(t => t.DueDate.HasValue
                        && t.DueDate.Value.Date >= tomorrow
                        && t.DueDate.Value.Date <= weekEnd
                        && !_inactiveStatuses.Contains(t.Status))
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Select(BuildToDto(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetNextTasksAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today         = DateTime.Today;
        var nextWeekStart = today.AddDays(7);
        var horizon       = today.AddDays(21);

        return await ActiveBase(userId)
            .Where(t => t.DueDate.HasValue
                        && t.DueDate.Value.Date >= nextWeekStart
                        && t.DueDate.Value.Date <= horizon
                        && !_inactiveStatuses.Contains(t.Status))
            .OrderBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .Take(10)
            .Select(BuildToDto(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------
    // New aggregate & inbox methods
    // ---------------------------------------------------------------------------

    public async Task<int> GetInboxCountAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Notes
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId && n.Status == NoteStatus.Inbox, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<TodayAggregateDto> GetTodayAggregateAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // DbContext is not thread-safe, so queries must run sequentially even though
        // the intent is to gather all data in a single pass.
        var currentTask         = await GetCurrentTaskByFlagAsync(userId, cancellationToken).ConfigureAwait(false);
        var highPriorityWork    = await GetHighPriorityTasksAsync(cancellationToken).ConfigureAwait(false);
        var overdue             = await GetOverdueAsync(cancellationToken).ConfigureAwait(false);
        var dueToday            = await GetDueTodayAsync(cancellationToken).ConfigureAwait(false);
        var dueThisWeek         = await GetDueThisWeekAsync(cancellationToken).ConfigureAwait(false);
        var nextTasks           = await GetNextTasksAsync(cancellationToken).ConfigureAwait(false);
        var inboxCount          = await GetInboxCountAsync(cancellationToken).ConfigureAwait(false);
        var prioritizedProjects = await projectPrioritizationService
                                        .GetPrioritizedProjectsAsync(cancellationToken: cancellationToken)
                                        .ConfigureAwait(false);

        var prefs = await context.DashboardPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var threshold = prefs?.InboxWarningThreshold ?? 10;

        return new TodayAggregateDto(
            currentTask,
            highPriorityWork,
            overdue,
            dueToday,
            dueThisWeek,
            nextTasks,
            inboxCount,
            inboxCount >= threshold,
            threshold,
            prioritizedProjects);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the single task the user has designated as their Current Task, or null if none is set.
    /// </summary>
    private Task<TodayTaskItemDto?> GetCurrentTaskByFlagAsync(string userId, CancellationToken cancellationToken)
    {
        var today = DateTime.Today;
        return ActiveBase(userId)
            .Where(t => t.IsCurrentTask)
            .Select(BuildToDto(today))
            .FirstOrDefaultAsync(cancellationToken);
    }

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

    private static System.Linq.Expressions.Expression<Func<Domain.Entities.TaskItem, TodayTaskItemDto>> BuildToDto(DateTime today) =>
        t => new TodayTaskItemDto(
            t.Id,
            t.Title,
            t.Description,
            t.Status,
            t.Priority,
            t.DueDate,
            t.ProjectId,
            t.Project.Name,
            t.CreatedAtUtc,
            t.Subtasks.Count(s => !s.IsArchived
                                   && s.Status != TaskItemStatus.Done
                                   && s.Status != TaskItemStatus.Archived
                                   && s.DueDate.HasValue
                                   && s.DueDate.Value.Date < today),
            t.Subtasks.Count(s => !s.IsArchived
                                   && s.Status != TaskItemStatus.Done
                                   && s.Status != TaskItemStatus.Archived
                                   && s.DueDate.HasValue
                                   && s.DueDate.Value.Date == today));
}
