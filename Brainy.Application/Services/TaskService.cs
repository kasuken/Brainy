using Brainy.Application.Common;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Handles CRUD operations for <see cref="TaskItem"/> entities, scoped to the current user.
/// Archived tasks are excluded from active queries; all reads use <c>AsNoTracking</c>.
/// </summary>
internal sealed class TaskService(IApplicationDbContext context, ICurrentUserService currentUser) : ITaskService
{
    public async Task<TaskItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .AsNoTracking()
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (task is null) return null;

        var subtasks = task.Subtasks
            .Where(s => !s.IsArchived)
            .Select(s => ToDto(s))
            .OrderByDescending(s => s.Priority)
            .ThenBy(s => s.DueDate)
            .ThenBy(s => s.Title)
            .ToList();

        return new TaskItemDto(
            task.Id, task.Title, task.Description, task.Status, task.Priority,
            task.DueDate, task.CompletedDate, task.IsArchived, task.IsCurrentTask, task.ProjectId, task.ParentTaskId,
            task.CreatedAtUtc, task.UpdatedAtUtc,
            SubtaskCount: subtasks.Count,
            DoneSubtaskCount: subtasks.Count(s => s.Status == TaskItemStatus.Done),
            Subtasks: subtasks,
            Complexity: task.Complexity,
            SortOrder: task.SortOrder,
            IsRecurring: task.IsRecurring,
            RecurrenceType: task.RecurrenceType,
            RecurrenceInterval: task.RecurrenceInterval,
            RecurrenceEndDate: task.RecurrenceEndDate,
            NextOccurrenceDate: task.NextOccurrenceDate,
            RowVersion: task.RowVersion);
    }

    public async Task<IReadOnlyList<TaskItemDto>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var tasks = await context.Tasks
            .AsNoTracking()
            .Where(t => t.ProjectId == projectId && t.UserId == userId && !t.IsArchived && t.ParentTaskId == null)
            .Select(t => new TaskItemDto(
                t.Id, t.Title, t.Description, t.Status, t.Priority,
                t.DueDate, t.CompletedDate, t.IsArchived, t.IsCurrentTask, t.ProjectId, t.ParentTaskId,
                t.CreatedAtUtc, t.UpdatedAtUtc,
                t.Subtasks.Count(s => !s.IsArchived),
                t.Subtasks.Count(s => !s.IsArchived && s.Status == TaskItemStatus.Done),
                t.Subtasks
                    .Where(s => !s.IsArchived)
                    .Select(s => new TaskItemDto(
                        s.Id, s.Title, s.Description, s.Status, s.Priority,
                        s.DueDate, s.CompletedDate, s.IsArchived, s.IsCurrentTask, s.ProjectId, s.ParentTaskId,
                        s.CreatedAtUtc, s.UpdatedAtUtc, 0, 0, null, s.Complexity, s.SortOrder)
                    {
                        RowVersion = s.RowVersion
                    })
                    .ToList(),
                t.Complexity, t.SortOrder,
                t.IsRecurring, t.RecurrenceType, t.RecurrenceInterval, t.RecurrenceEndDate, t.NextOccurrenceDate)
            {
                RowVersion = t.RowVersion
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // SortOrder is the primary key (preserves explicit user ordering); fall back to
        // Priority → DueDate → Title for tasks that have never been manually sorted (SortOrder == 0).
        return tasks
            .Select(t => t with { Subtasks = OrderTasks(t.Subtasks) })
            .OrderBy(t => t.SortOrder)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.DueDate)
            .ThenBy(t => t.Title)
            .ToList();
    }

    private static IReadOnlyList<TaskItemDto>? OrderTasks(IReadOnlyList<TaskItemDto>? tasks) =>
        tasks is null or { Count: 0 }
            ? tasks
            : tasks
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.DueDate)
                .ThenBy(t => t.Title)
                .ToList();

    public async Task<TaskItemDto> CreateAsync(CreateTaskDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Task title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Verify the project belongs to the user
        var projectExists = await context.Projects
            .AnyAsync(p => p.Id == dto.ProjectId && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (!projectExists)
            throw new KeyNotFoundException($"Project '{dto.ProjectId}' was not found.");

        // Verify parent task belongs to the same project and user (if supplied)
        if (dto.ParentTaskId.HasValue)
        {
            var parentExists = await context.Tasks
                .AnyAsync(t => t.Id == dto.ParentTaskId.Value && t.ProjectId == dto.ProjectId && t.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (!parentExists)
                throw new KeyNotFoundException($"Parent task '{dto.ParentTaskId}' was not found in project '{dto.ProjectId}'.");
        }

        var task = new TaskItem
        {
            Id           = Guid.NewGuid(),
            UserId       = userId,
            ProjectId    = dto.ProjectId,
            ParentTaskId = dto.ParentTaskId,
            Title        = dto.Title.Trim(),
            Description  = dto.Description?.Trim(),
            Priority     = dto.Priority,
            Complexity   = dto.Complexity,
            DueDate      = dto.DueDate,
            Status       = TaskItemStatus.Todo,
            IsRecurring          = dto.IsRecurring,
            RecurrenceType       = dto.RecurrenceType,
            RecurrenceInterval   = dto.RecurrenceInterval,
            RecurrenceEndDate    = dto.RecurrenceEndDate,
            NextOccurrenceDate   = dto.NextOccurrenceDate,
        };

        context.Tasks.Add(task);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A new (incomplete) subtask may require the parent to be reopened.
        if (task.ParentTaskId.HasValue)
        {
            await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);
        }

        return ToDto(task);
    }

    public async Task<TaskItemDto> UpdateAsync(UpdateTaskDto dto, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (string.IsNullOrWhiteSpace(dto.Title))
            throw new ArgumentException("Task title is required.", nameof(dto));

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == dto.Id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{dto.Id}' was not found.");

        // Optimistic concurrency: compare against the token captured when the caller
        // loaded the task so edits made elsewhere since then are detected.
        if (dto.RowVersion is not null)
            context.Entry(task).Property(t => t.RowVersion).OriginalValue = dto.RowVersion;

        task.Title       = dto.Title.Trim();
        task.Description = dto.Description?.Trim();
        task.Priority    = dto.Priority;
        task.Complexity  = dto.Complexity;
        task.DueDate     = dto.DueDate;
        task.IsRecurring          = dto.IsRecurring;
        task.RecurrenceType       = dto.RecurrenceType;
        task.RecurrenceInterval   = dto.RecurrenceInterval;
        task.RecurrenceEndDate    = dto.RecurrenceEndDate;
        task.NextOccurrenceDate   = dto.NextOccurrenceDate;

        // Handle status transition — keep CompletedDate in sync
        var statusChanged = dto.Status != task.Status;
        if (dto.Status == TaskItemStatus.Done && task.Status != TaskItemStatus.Done)
        {
            task.CompletedDate = DateTime.UtcNow;
        }
        else if (dto.Status != TaskItemStatus.Done)
        {
            task.CompletedDate = null;
        }

        task.Status = dto.Status;

        // A completed task can no longer be the user's current focus.
        if (dto.Status == TaskItemStatus.Done && task.IsCurrentTask)
        {
            task.IsCurrentTask = false;
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("task", ex);
        }

        if (statusChanged && task.ParentTaskId.HasValue)
        {
            await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);
        }

        return ToDto(task);
    }

    public async Task<TaskItemDto> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        var now = DateTime.UtcNow;
        var changed = false;

        if (task.Status != TaskItemStatus.Done)
        {
            task.Status        = TaskItemStatus.Done;
            task.CompletedDate = now;
            changed = true;
        }

        // A completed task can no longer be the user's current focus.
        if (task.IsCurrentTask)
        {
            task.IsCurrentTask = false;
            changed = true;
        }

        // Completing a task completes all of its active subtasks too.
        foreach (var sub in task.Subtasks.Where(s => !s.IsArchived && s.Status != TaskItemStatus.Done))
        {
            sub.Status        = TaskItemStatus.Done;
            sub.CompletedDate = now;
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (task.ParentTaskId.HasValue)
            {
                await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);
            }
        }

        return ToDto(task);
    }

    public async Task<TaskItemDto> SetInProgressAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        if (task.Status != TaskItemStatus.InProgress)
        {
            task.Status        = TaskItemStatus.InProgress;
            task.CompletedDate = null;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (task.ParentTaskId.HasValue)
            {
                await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);
            }
        }

        return ToDto(task);
    }

    public async Task<TaskItemDto> ReopenAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        if (task.Status == TaskItemStatus.Done)
        {
            task.Status        = TaskItemStatus.Todo;
            task.CompletedDate = null;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            if (task.ParentTaskId.HasValue)
            {
                await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);
            }
        }

        return ToDto(task);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        var now = DateTime.UtcNow;
        task.IsArchived    = true;
        task.ArchivedAtUtc = now;
        task.IsCurrentTask = false;

        // Cascade archive to subtasks
        foreach (var sub in task.Subtasks.Where(s => !s.IsArchived))
        {
            sub.IsArchived    = true;
            sub.ArchivedAtUtc = now;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Removing a subtask from the active set may complete (or reopen) the parent.
        if (task.ParentTaskId.HasValue)
        {
            await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        // Dependency links use Restrict delete behaviour on both sides, so any link
        // referencing this task or its subtasks must be removed before the delete.
        var taskIds = task.Subtasks.Select(s => s.Id).Append(task.Id).ToList();
        var dependencyLinks = await context.TaskDependencies
            .Where(d => taskIds.Contains(d.TaskId) || taskIds.Contains(d.DependsOnTaskId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        context.TaskDependencies.RemoveRange(dependencyLinks);

        // Remove subtasks first (EF Restrict delete behaviour on self-reference)
        context.Tasks.RemoveRange(task.Subtasks);
        context.Tasks.Remove(task);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TaskItemDto?> GetCurrentTaskAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var task = await context.Tasks
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IsCurrentTask, cancellationToken)
            .ConfigureAwait(false);

        return task is null ? null : ToDto(task);
    }

    public async Task<TaskItemDto> SetCurrentTaskAsync(Guid taskId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Clear the flag on all tasks for this user first to enforce the one-per-user invariant
        await context.Tasks
            .Where(t => t.UserId == userId && t.IsCurrentTask)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsCurrentTask, false), cancellationToken)
            .ConfigureAwait(false);

        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        if (task.Status != TaskItemStatus.InProgress)
        {
            task.Status = TaskItemStatus.InProgress;
            task.CompletedDate = null;
        }

        task.IsCurrentTask = true;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(task);
    }

    public async Task ClearCurrentTaskAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await context.Tasks
            .Where(t => t.UserId == userId && t.IsCurrentTask)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.IsCurrentTask, false), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReorderAsync(Guid projectId, TaskItemStatus status, IReadOnlyList<Guid> orderedTaskIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(orderedTaskIds);

        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var tasks = await context.Tasks
            .Where(t => t.ProjectId == projectId && t.UserId == userId
                        && t.Status == status && !t.IsArchived && t.ParentTaskId == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        for (var i = 0; i < orderedTaskIds.Count; i++)
        {
            var task = tasks.Find(t => t.Id == orderedTaskIds[i]);
            if (task is not null)
                task.SortOrder = i;
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<TaskItemDto> CreateRecurringOccurrenceAsync(Guid taskId, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var template = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        if (!template.IsRecurring)
            throw new InvalidOperationException($"Task '{taskId}' is not a recurring task.");

        var occurrenceDueDate = template.NextOccurrenceDate;

        var occurrence = new TaskItem
        {
            Id          = Guid.NewGuid(),
            UserId      = userId,
            ProjectId   = template.ProjectId,
            Title       = template.Title,
            Description = template.Description,
            Priority    = template.Priority,
            Complexity  = template.Complexity,
            DueDate     = occurrenceDueDate,
            Status      = TaskItemStatus.Todo,
            // Occurrences are one-off tasks, not themselves templates
        };

        context.Tasks.Add(occurrence);

        // Advance the template's next occurrence date
        if (occurrenceDueDate.HasValue
            && template.RecurrenceType.HasValue
            && template.RecurrenceInterval is > 0)
        {
            var interval = template.RecurrenceInterval.Value;
            template.NextOccurrenceDate = template.RecurrenceType.Value switch
            {
                Domain.Enums.RecurrenceType.Daily   => occurrenceDueDate.Value.AddDays(interval),
                Domain.Enums.RecurrenceType.Weekly  => occurrenceDueDate.Value.AddDays(7 * interval),
                Domain.Enums.RecurrenceType.Monthly => occurrenceDueDate.Value.AddMonths(interval),
                Domain.Enums.RecurrenceType.Yearly  => occurrenceDueDate.Value.AddYears(interval),
                _                                   => occurrenceDueDate.Value,
            };
        }

        await context.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToDto(occurrence);
    }

    public async Task AddDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var taskExists = await context.Tasks
            .AnyAsync(t => t.Id == taskId && t.UserId == userId, ct)
            .ConfigureAwait(false);
        if (!taskExists)
            throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        var dependsOnExists = await context.Tasks
            .AnyAsync(t => t.Id == dependsOnTaskId && t.UserId == userId, ct)
            .ConfigureAwait(false);
        if (!dependsOnExists)
            throw new KeyNotFoundException($"Task '{dependsOnTaskId}' was not found.");

        var alreadyExists = await context.TaskDependencies
            .AnyAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId, ct)
            .ConfigureAwait(false);
        if (alreadyExists) return;

        context.TaskDependencies.Add(new TaskDependency
        {
            Id              = Guid.NewGuid(),
            TaskId          = taskId,
            DependsOnTaskId = dependsOnTaskId,
        });

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var dep = await context.TaskDependencies
            .FirstOrDefaultAsync(d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId, ct)
            .ConfigureAwait(false);
        if (dep is null) return;

        // Guard: the task must belong to the current user
        var taskBelongsToUser = await context.Tasks
            .AnyAsync(t => t.Id == taskId && t.UserId == userId, ct)
            .ConfigureAwait(false);
        if (!taskBelongsToUser)
            throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        context.TaskDependencies.Remove(dep);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Recomputes a parent task's completion state from its non-archived subtasks:
    /// the parent is auto-completed when every subtask is done, and auto-reopened
    /// (to In Progress) when a previously complete parent gains an unfinished subtask.
    /// Parents without subtasks are left untouched.
    /// </summary>
    private async Task SyncParentProgressAsync(Guid parentTaskId, string userId, CancellationToken cancellationToken)
    {
        var parent = await context.Tasks
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == parentTaskId && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (parent is null)
        {
            return;
        }

        var activeSubtasks = parent.Subtasks.Where(s => !s.IsArchived).ToList();
        if (activeSubtasks.Count == 0)
        {
            return;
        }

        var allDone = activeSubtasks.All(s => s.Status == TaskItemStatus.Done);
        var changed = false;

        if (allDone && parent.Status != TaskItemStatus.Done)
        {
            parent.Status        = TaskItemStatus.Done;
            parent.CompletedDate = DateTime.UtcNow;
            parent.IsCurrentTask = false;
            changed = true;
        }
        else if (!allDone && parent.Status == TaskItemStatus.Done)
        {
            parent.Status        = TaskItemStatus.InProgress;
            parent.CompletedDate = null;
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static TaskItemDto ToDto(TaskItem t) => new(
        t.Id, t.Title, t.Description, t.Status, t.Priority,
        t.DueDate, t.CompletedDate, t.IsArchived, t.IsCurrentTask, t.ProjectId, t.ParentTaskId,
        t.CreatedAtUtc, t.UpdatedAtUtc,
        SubtaskCount: 0, DoneSubtaskCount: 0, Complexity: t.Complexity, SortOrder: t.SortOrder,
        IsRecurring: t.IsRecurring, RecurrenceType: t.RecurrenceType,
        RecurrenceInterval: t.RecurrenceInterval, RecurrenceEndDate: t.RecurrenceEndDate,
        NextOccurrenceDate: t.NextOccurrenceDate, RowVersion: t.RowVersion);
}
