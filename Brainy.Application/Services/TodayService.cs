using Brainy.Application.Common;
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
/// Every query enforces: active project, non-archived task, top-level only.
/// </summary>
internal sealed class TodayService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IProjectPrioritizationService projectPrioritizationService,
    IUserTimeZoneService userTimeZone) : ITodayService
{
    // Base active-task predicate: excludes archived tasks, tasks from non-active or archived projects,
    // done/archived statuses, and subtasks.
    private static readonly TaskItemStatus[] _inactiveStatuses =
        [TaskItemStatus.Done, TaskItemStatus.Archived];

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetCurrentTasksAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

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
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

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
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

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
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

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

    public async Task<TodayPlannedWeekDto> GetPlannedThisWeekAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var week = WeekDateHelper.GetWeekContaining(today);

        var selectionStates = await context.WeeklyTaskSelections
            .AsNoTracking()
            .Where(selection =>
                selection.UserId == userId &&
                selection.WeekStartDate == week.WeekStartDate &&
                selection.Task.UserId == userId &&
                selection.Task.Project.UserId == userId)
            .Select(selection => new
            {
                selection.Task.Status,
                IsActionable =
                    !selection.Task.IsArchived &&
                    selection.Task.ParentTaskId == null &&
                    !selection.Task.Project.IsArchived &&
                    selection.Task.Project.Status == ProjectStatus.Active &&
                    (selection.Task.Status == TaskItemStatus.Todo ||
                     selection.Task.Status == TaskItemStatus.InProgress) &&
                    !selection.Task.Dependencies.Any(dependency =>
                        dependency.DependsOnTask.IsArchived ||
                        dependency.DependsOnTask.Status != TaskItemStatus.Done)
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tasks = await context.WeeklyTaskSelections
            .AsNoTracking()
            .Where(selection =>
                selection.UserId == userId &&
                selection.WeekStartDate == week.WeekStartDate &&
                selection.Task.UserId == userId &&
                selection.Task.Project.UserId == userId &&
                !selection.Task.IsArchived &&
                selection.Task.ParentTaskId == null &&
                !selection.Task.Project.IsArchived &&
                selection.Task.Project.Status == ProjectStatus.Active &&
                (selection.Task.Status == TaskItemStatus.Todo ||
                 selection.Task.Status == TaskItemStatus.InProgress) &&
                !selection.Task.Dependencies.Any(dependency =>
                    dependency.DependsOnTask.IsArchived ||
                    dependency.DependsOnTask.Status != TaskItemStatus.Done))
            .OrderByDescending(selection => selection.Task.IsCurrentTask)
            .ThenByDescending(selection => selection.Task.Status == TaskItemStatus.InProgress)
            .ThenByDescending(selection => selection.Task.Priority)
            .ThenBy(selection => selection.Task.DueDate)
            .ThenBy(selection => selection.Task.Title)
            .Select(selection => selection.Task)
            .Select(BuildToDto(today))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var completedTaskCount = selectionStates.Count(state => state.Status == TaskItemStatus.Done);
        var needsReplanningCount = selectionStates.Count(state =>
            state.Status != TaskItemStatus.Done && !state.IsActionable);

        return new TodayPlannedWeekDto(
            selectionStates.Count,
            completedTaskCount,
            tasks.Count,
            needsReplanningCount,
            tasks);
    }

    public async Task<IReadOnlyList<TodayTaskItemDto>> GetDueThisWeekAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today    = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var tomorrow = today.AddDays(1);
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        var weekEnd  = today.AddDays(daysUntilSunday);

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
        var today         = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var daysUntilSunday = ((int)DayOfWeek.Sunday - (int)today.DayOfWeek + 7) % 7;
        var nextWeekStart = today.AddDays(daysUntilSunday + 1);
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
        var today               = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var currentTask         = await GetCurrentTaskByFlagAsync(userId, today, cancellationToken).ConfigureAwait(false);
        var inProgress          = await GetCurrentTasksAsync(cancellationToken).ConfigureAwait(false);
        var overdue              = await GetOverdueAsync(cancellationToken).ConfigureAwait(false);
        var dueToday            = await GetDueTodayAsync(cancellationToken).ConfigureAwait(false);
        var plannedThisWeek     = await GetPlannedThisWeekAsync(cancellationToken).ConfigureAwait(false);
        var highPriorityWork    = await GetHighPriorityTasksAsync(cancellationToken).ConfigureAwait(false);
        var dueThisWeek         = await GetDueThisWeekAsync(cancellationToken).ConfigureAwait(false);
        var nextTasks           = await GetNextTasksAsync(cancellationToken).ConfigureAwait(false);
        var inboxCount          = await GetInboxCountAsync(cancellationToken).ConfigureAwait(false);
        var prioritizedProjects = await projectPrioritizationService
                                        .GetPrioritizedProjectsAsync(cancellationToken: cancellationToken)
                                        .ConfigureAwait(false);

        // Deliberate weekly commitments own their task cards on Today. Generic task
        // sections must not hide that planning context or repeat the same work lower
        // down. Current focus remains a separate execution surface and may therefore
        // also appear in the weekly plan.
        var seen = new HashSet<Guid>();
        plannedThisWeek = plannedThisWeek with { Tasks = ExcludeSeen(plannedThisWeek.Tasks, seen) };
        inProgress = ExcludeSeen(inProgress, seen);
        if (currentTask is not null)
            seen.Add(currentTask.Id);

        overdue          = ExcludeSeen(overdue, seen);
        dueToday         = ExcludeSeen(dueToday, seen);
        highPriorityWork = ExcludeSeen(highPriorityWork, seen);
        dueThisWeek      = ExcludeSeen(dueThisWeek, seen);
        nextTasks        = ExcludeSeen(nextTasks, seen);

        var prefs = await context.DashboardPreferences
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var threshold = prefs?.InboxWarningThreshold ?? 10;

        return new TodayAggregateDto(
            currentTask,
            inProgress,
            highPriorityWork,
            overdue,
            dueToday,
            plannedThisWeek,
            dueThisWeek,
            nextTasks,
            inboxCount,
            inboxCount >= threshold,
            threshold,
            prioritizedProjects);
    }

    /// <summary>
    /// Removes tasks already claimed by a higher-precedence Today section, then
    /// registers the remaining tasks as seen so lower-precedence sections exclude them too.
    /// </summary>
    private static IReadOnlyList<TodayTaskItemDto> ExcludeSeen(
        IReadOnlyList<TodayTaskItemDto> tasks,
        HashSet<Guid> seen)
    {
        if (tasks.Count == 0)
            return tasks;

        var remaining = tasks.Where(t => seen.Add(t.Id)).ToList();
        return remaining.Count == tasks.Count ? tasks : remaining;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the single task the user has designated as their Current Task, or null if none is set.
    /// </summary>
    private Task<TodayTaskItemDto?> GetCurrentTaskByFlagAsync(
        string userId,
        DateTime today,
        CancellationToken cancellationToken)
    {
        return ActiveBase(userId)
            .Where(t => t.IsCurrentTask)
            .Select(BuildToDto(today))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Base queryable that enforces the "active task" contract:
    /// non-archived, belongs to an active non-archived project, top-level only, owned by the user.
    /// </summary>
    private IQueryable<Domain.Entities.TaskItem> ActiveBase(string userId) =>
        context.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && !t.IsArchived
                        && t.Project.Status == ProjectStatus.Active
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
                                   && s.DueDate.Value.Date == today),
            t.Subtasks
                .Where(s => !s.IsArchived && s.Status != TaskItemStatus.Done && s.Status != TaskItemStatus.Archived)
                .OrderBy(s => s.SortOrder)
                .Select(s => s.Title)
                .FirstOrDefault(),
            t.Subtasks
                .Where(s => !s.IsArchived)
                .OrderBy(s => s.SortOrder)
                .Select(s => new TodaySubtaskItemDto(s.Id, s.Title, s.Status, s.DueDate))
                .ToList());
}
