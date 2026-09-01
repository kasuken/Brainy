using System.Linq.Expressions;
using Brainy.Application.Caching;
using Brainy.Application.Common;
using Brainy.Application.DTOs;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.Interfaces.Caching;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Provides all aggregated task data and operations for the Tasks Hub dashboard.
/// Active-task queries enforce non-archived task and top-level-only rules, with
/// project lifecycle states controlled by <see cref="TaskHubProjectScope"/>.
/// </summary>
internal sealed class TasksHubService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone,
    ITaskService taskService,
    TimeProvider timeProvider,
    IApplicationCache cache) : ITasksHubService
{
    private static readonly TaskItemStatus[] _inactiveStatuses = [TaskItemStatus.Done, TaskItemStatus.Archived];

    private static readonly Expression<Func<TaskItem, TasksHubTaskDto>> ToDto =
        t => new TasksHubTaskDto(t.Id, t.Title, t.Description, t.Status, t.Priority,
            t.DueDate, t.ProjectId, t.Project.Name, t.CreatedAtUtc, t.UpdatedAtUtc, t.Complexity,
            t.Dependencies.Any(d => d.DependsOnTask.Status != TaskItemStatus.Done));

    // ---------------------------------------------------------------------------
    // Aggregate
    // ---------------------------------------------------------------------------

    public async Task<TasksHubAggregateDto> GetHubAggregateAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        // DbContext is not thread-safe — run sequentially.
        var activeTasks       = await GetActiveTasksAsync(projectScope, cancellationToken).ConfigureAwait(false);
        var highPriorityTasks = await GetHighPriorityTasksAsync(projectScope, cancellationToken).ConfigureAwait(false);
        var onHoldTasks       = await GetOnHoldTasksAsync(projectScope, cancellationToken).ConfigureAwait(false);
        var overdueTasks      = await GetOverdueTasksAsync(projectScope, cancellationToken).ConfigureAwait(false);
        var needingAttention  = await GetTasksNeedingAttentionAsync(projectScope, cancellationToken).ConfigureAwait(false);
        var withoutDueDate    = await GetTasksWithoutDueDateAsync(projectScope, cancellationToken).ConfigureAwait(false);
        var staleTasks        = await GetStaleTasksAsync(projectScope, cancellationToken).ConfigureAwait(false);
        var statusSummary     = await GetStatusSummaryAsync(projectScope, cancellationToken).ConfigureAwait(false);

        return new TasksHubAggregateDto(
            activeTasks, highPriorityTasks, onHoldTasks, overdueTasks,
            needingAttention, withoutDueDate, staleTasks, statusSummary);
    }

    // ---------------------------------------------------------------------------
    // Queries
    // ---------------------------------------------------------------------------

    public async Task<IReadOnlyList<TasksHubTaskDto>> GetActiveTasksAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:active:{projectScope}",
            TaskHubReadTags(),
            async ct => await ActiveBase(userId, projectScope)
                .Where(t => !_inactiveStatuses.Contains(t.Status))
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate)
                .Select(ToDto)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TasksHubTaskDto>> GetHighPriorityTasksAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:high-priority:{projectScope}",
            TaskHubReadTags(),
            async ct => await ActiveBase(userId, projectScope)
                .Where(t => t.Priority >= TaskPriority.High && !_inactiveStatuses.Contains(t.Status))
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate)
                .Select(ToDto)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TasksHubTaskDto>> GetOnHoldTasksAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:on-hold:{projectScope}",
            TaskHubReadTags(),
            async ct => await ActiveBase(userId, projectScope)
                .Where(t => t.Status == TaskItemStatus.Waiting)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.UpdatedAtUtc)
                .Select(ToDto)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TasksHubTaskDto>> GetOverdueTasksAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:overdue:{projectScope}:{today:yyyy-MM-dd}",
            [.. TaskHubReadTags(), ApplicationCacheKey.TimeZoneTag],
            async ct => await ActiveBase(userId, projectScope)
                .Where(t => t.DueDate.HasValue && t.DueDate.Value.Date < today
                            && !_inactiveStatuses.Contains(t.Status))
                .OrderBy(t => t.DueDate)
                .Select(ToDto)
                .ToListAsync(ct)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TasksHubTaskDto>> GetTasksNeedingAttentionAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:attention:{projectScope}:{today:yyyy-MM-dd}",
            [.. TaskHubReadTags(), ApplicationCacheKey.TimeZoneTag],
            ct => GetTasksNeedingAttentionCoreAsync(userId, today, projectScope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<TasksHubTaskDto>> GetTasksNeedingAttentionCoreAsync(
        string userId,
        DateTime today,
        TaskHubProjectScope projectScope,
        CancellationToken cancellationToken)
    {
        // Fetch overdue, due-today, and critical tasks separately then union by Id.
        var overdue = await ActiveBase(userId, projectScope)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value.Date < today
                        && !_inactiveStatuses.Contains(t.Status))
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dueToday = await ActiveBase(userId, projectScope)
            .Where(t => t.DueDate.HasValue && t.DueDate.Value.Date == today
                        && !_inactiveStatuses.Contains(t.Status))
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var critical = await ActiveBase(userId, projectScope)
            .Where(t => t.Priority == TaskPriority.Critical && !_inactiveStatuses.Contains(t.Status))
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var seen = new HashSet<Guid>();
        var result = new List<TasksHubTaskDto>();
        foreach (var t in overdue.Concat(dueToday).Concat(critical))
        {
            if (seen.Add(t.Id))
                result.Add(t);
        }

        return result.OrderByDescending(t => t.Priority).ThenBy(t => t.DueDate).ToList();
    }

    public async Task<IReadOnlyList<TasksHubTaskDto>> GetTasksWithoutDueDateAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:without-due-date:{projectScope}",
            TaskHubReadTags(),
            async ct => await ActiveBase(userId, projectScope)
                .Where(t => !t.DueDate.HasValue && !_inactiveStatuses.Contains(t.Status))
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.CreatedAtUtc)
                .Select(ToDto)
                .ToListAsync(ct).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TasksHubTaskDto>> GetStaleTasksAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var staleThreshold = timeProvider.GetUtcNow().UtcDateTime.AddDays(-30);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:stale:{projectScope}",
            TaskHubReadTags(),
            async ct => await ActiveBase(userId, projectScope)
                .Where(t => t.UpdatedAtUtc < staleThreshold && !_inactiveStatuses.Contains(t.Status))
                .OrderBy(t => t.UpdatedAtUtc)
                .Select(ToDto)
                .ToListAsync(ct)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskStatusSummaryDto> GetStatusSummaryAsync(
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await cache.GetOrCreateAsync(
            userId,
            $"tasks-hub:status-summary:{projectScope}",
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<Project>()
            ],
            async ct =>
            {
                var counts = await ScopedBase(userId, projectScope)
                    .AsNoTracking()
                    .GroupBy(t => t.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync(ct).ConfigureAwait(false);
                return new TaskStatusSummaryDto(
                    counts.FirstOrDefault(c => c.Status == TaskItemStatus.Todo)?.Count ?? 0,
                    counts.FirstOrDefault(c => c.Status == TaskItemStatus.InProgress)?.Count ?? 0,
                    counts.FirstOrDefault(c => c.Status == TaskItemStatus.Waiting)?.Count ?? 0,
                    counts.FirstOrDefault(c => c.Status == TaskItemStatus.Done)?.Count ?? 0);
            },
            cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------
    // Attention score (pure computation)
    // ---------------------------------------------------------------------------

    public int ComputeAttentionScore(TasksHubTaskDto task)
    {
        ArgumentNullException.ThrowIfNull(task);

        // This method intentionally stays a pure synchronous score helper. Service
        // query methods use the persisted user time zone; callers that need an exact
        // boundary should score the already-classified query results.
        var today = timeProvider.GetUtcNow().UtcDateTime.Date;
        int score = 0;

        if (task.DueDate.HasValue)
        {
            var dueDate = task.DueDate.Value.Date;
            if (dueDate < today)
            {
                // Overdue: base 50 + up to 30 bonus for each overdue day.
                int overdueDays = (int)(today - dueDate).TotalDays;
                score += 50 + Math.Min(overdueDays * 2, 30);
            }
            else if (dueDate == today)
            {
                score += 20;
            }
            else if (dueDate == today.AddDays(1))
            {
                score += 10;
            }
        }

        score += task.Priority switch
        {
            TaskPriority.Critical => 20,
            TaskPriority.High     => 10,
            TaskPriority.Medium   => 5,
            _                     => 0,
        };

        return Math.Min(score, 100);
    }

    // ---------------------------------------------------------------------------
    // Search & filter
    // ---------------------------------------------------------------------------

    public async Task<PagedResult<TasksHubTaskDto>> SearchTasksAsync(
        string searchTerm,
        int page = 1,
        int pageSize = 20,
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return new PagedResult<TasksHubTaskDto>([], 0, page, pageSize);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        // Explicit lower-casing keeps the match case-insensitive regardless of the
        // database collation (and on the InMemory provider used in tests).
        var term = searchTerm.Trim().ToLower();

        return await cache.GetOrCreateAsync(
            userId,
            ApplicationCacheKey.Create("tasks-hub", "search", projectScope, term, page, pageSize),
            TaskHubReadTags(),
            ct => SearchTasksCoreAsync(userId, term, page, pageSize, projectScope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PagedResult<TasksHubTaskDto>> SearchTasksCoreAsync(
        string userId,
        string term,
        int page,
        int pageSize,
        TaskHubProjectScope projectScope,
        CancellationToken cancellationToken)
    {
        var query = ActiveBase(userId, projectScope)
            .Where(t => !_inactiveStatuses.Contains(t.Status)
                        && (t.Title.ToLower().Contains(term)
                            || (t.Description != null && t.Description.ToLower().Contains(term))));

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TasksHubTaskDto>(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<TasksHubTaskDto>> GetFilteredTasksAsync(
        TasksHubFilterDto filter,
        TaskHubProjectScope projectScope = TaskHubProjectScope.ActiveOnly,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = ApplicationCacheKey.Create(
            "tasks-hub",
            "filtered",
            projectScope,
            filter.ProjectId,
            filter.Status,
            filter.MinPriority,
            filter.DueBefore,
            filter.DueAfter,
            filter.SearchTerm,
            filter.Page,
            filter.PageSize,
            filter.Complexity);

        return await cache.GetOrCreateAsync(
            userId,
            cacheKey,
            TaskHubReadTags(),
            ct => GetFilteredTasksCoreAsync(userId, filter, projectScope, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PagedResult<TasksHubTaskDto>> GetFilteredTasksCoreAsync(
        string userId,
        TasksHubFilterDto filter,
        TaskHubProjectScope projectScope,
        CancellationToken cancellationToken)
    {
        var query = ActiveBase(userId, projectScope).AsQueryable();

        if (filter.ProjectId.HasValue)
            query = query.Where(t => t.ProjectId == filter.ProjectId.Value);

        if (filter.Status.HasValue)
            query = query.Where(t => t.Status == filter.Status.Value);
        else
            query = query.Where(t => !_inactiveStatuses.Contains(t.Status));

        if (filter.MinPriority.HasValue)
            query = query.Where(t => t.Priority >= filter.MinPriority.Value);

        if (filter.DueBefore.HasValue)
            query = query.Where(t => t.DueDate.HasValue && t.DueDate.Value <= filter.DueBefore.Value);

        if (filter.DueAfter.HasValue)
            query = query.Where(t => t.DueDate.HasValue && t.DueDate.Value >= filter.DueAfter.Value);

        if (filter.Complexity.HasValue)
            query = query.Where(t => t.Complexity == filter.Complexity.Value);

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            var term = filter.SearchTerm.Trim();
            query = query.Where(t => t.Title.Contains(term) || (t.Description != null && t.Description.Contains(term)));
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(ToDto)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<TasksHubTaskDto>(items, totalCount, filter.Page, filter.PageSize);
    }

    // ---------------------------------------------------------------------------
    // Bulk operations
    // ---------------------------------------------------------------------------

    public async Task<int> BulkOperationAsync(BulkTaskOperationDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Verify new project belongs to user if provided.
        if (dto.NewProjectId.HasValue)
        {
            var projectExists = await context.Projects
                .AnyAsync(p => p.Id == dto.NewProjectId.Value && p.UserId == userId && !p.IsArchived, cancellationToken)
                .ConfigureAwait(false);

            if (!projectExists)
                throw new KeyNotFoundException($"Project '{dto.NewProjectId.Value}' was not found.");
        }

        var tasks = await context.Tasks
            .Where(t => dto.TaskIds.Contains(t.Id) && t.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (dto.Archive)
        {
            foreach (var task in tasks)
                await taskService.ArchiveAsync(task.Id, cancellationToken).ConfigureAwait(false);

            return tasks.Count;
        }

        var completedTaskIds = dto.NewStatus == TaskItemStatus.Done
            ? tasks.Where(t => t.Status != TaskItemStatus.Done).Select(t => t.Id).ToList()
            : [];

        // Complete through TaskService before the bulk mutation so prerequisite,
        // recurrence, subtask, and current-focus invariants cannot be bypassed.
        foreach (var taskId in completedTaskIds)
            await taskService.CompleteAsync(taskId, cancellationToken).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        foreach (var task in tasks)
        {
            if (dto.NewStatus.HasValue)
            {
                if (dto.NewStatus.Value == TaskItemStatus.Done && task.Status != TaskItemStatus.Done)
                    task.CompletedDate = now;
                else if (dto.NewStatus.Value != TaskItemStatus.Done)
                    task.CompletedDate = null;

                task.Status = dto.NewStatus.Value;

                // A completed task can no longer be the user's current focus.
                if (dto.NewStatus.Value == TaskItemStatus.Done)
                    task.IsCurrentTask = false;
            }

            if (dto.NewPriority.HasValue)
                task.Priority = dto.NewPriority.Value;

            if (dto.NewDueDate.HasValue)
                task.DueDate = dto.NewDueDate;

            if (dto.NewProjectId.HasValue)
                task.ProjectId = dto.NewProjectId.Value;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (tasks.Count > 0)
        {
            List<string> tags =
            [
                ApplicationCacheKey.EntityTypeTag<TaskItem>(),
                ApplicationCacheKey.EntityTypeTag<LifecycleActivity>()
            ];
            tags.AddRange(tasks.Select(task => ApplicationCacheKey.EntityTag<TaskItem>(task.Id)));
            await cache.InvalidateTagsAsync(userId, tags, CancellationToken.None).ConfigureAwait(false);
        }

        return tasks.Count;
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static IReadOnlyCollection<string> TaskHubReadTags() =>
    [
        ApplicationCacheKey.EntityTypeTag<TaskItem>(),
        ApplicationCacheKey.EntityTypeTag<Project>(),
        ApplicationCacheKey.EntityTypeTag<TaskDependency>()
    ];

    private IQueryable<TaskItem> ActiveBase(string userId, TaskHubProjectScope projectScope) =>
        ScopedBase(userId, projectScope)
            .Where(t => t.ParentTaskId == null);

    private IQueryable<TaskItem> ScopedBase(string userId, TaskHubProjectScope projectScope) =>
        ApplyProjectScope(
            context.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId
                        && !t.IsArchived
                        && !t.Project.IsArchived),
            projectScope);

    private static IQueryable<TaskItem> ApplyProjectScope(
        IQueryable<TaskItem> query,
        TaskHubProjectScope projectScope) =>
        projectScope switch
        {
            TaskHubProjectScope.ActiveOnly =>
                query.Where(t => t.Project.Status == ProjectStatus.Active),
            TaskHubProjectScope.ActiveAndBlocked =>
                query.Where(t => t.Project.Status == ProjectStatus.Active || t.Project.Status == ProjectStatus.Blocked),
            TaskHubProjectScope.ActiveBlockedAndParked =>
                query.Where(t => t.Project.Status == ProjectStatus.Active
                              || t.Project.Status == ProjectStatus.Blocked
                              || t.Project.Status == ProjectStatus.Parked),
            _ => query.Where(t => t.Project.Status == ProjectStatus.Active)
        };
}
