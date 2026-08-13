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
            .Include(t => t.Dependencies)
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
            RowVersion: task.RowVersion,
            DependsOnTaskIds: task.Dependencies.Select(d => d.DependsOnTaskId).ToList());
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
                        RowVersion = s.RowVersion,
                        DependsOnTaskIds = s.Dependencies.Select(d => d.DependsOnTaskId).ToList()
                    })
                    .ToList(),
                t.Complexity, t.SortOrder,
                t.IsRecurring, t.RecurrenceType, t.RecurrenceInterval, t.RecurrenceEndDate, t.NextOccurrenceDate,
                null,
                t.Dependencies.Select(d => d.DependsOnTaskId).ToList())
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

    public async Task<IReadOnlyList<ArchivedTaskDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.Tasks
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.IsArchived)
            .OrderByDescending(t => t.ArchivedAtUtc)
            .Select(t => new ArchivedTaskDto(
                t.Id,
                t.Title,
                t.Description,
                t.ProjectId,
                t.Project.Name,
                t.ArchivedAtUtc ?? t.UpdatedAtUtc,
                t.UpdatedAtUtc,
                !t.Project.IsArchived && (t.ParentTaskId == null || !t.ParentTask!.IsArchived)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
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

        ValidateRecurrence(dto.IsRecurring, dto.RecurrenceType, dto.RecurrenceInterval,
            dto.NextOccurrenceDate, dto.RecurrenceEndDate);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Verify the project belongs to the user
        var projectExists = await context.Projects
            .AnyAsync(p => p.Id == dto.ProjectId && p.UserId == userId && !p.IsArchived, cancellationToken)
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
            RecurrenceType       = dto.IsRecurring ? dto.RecurrenceType : null,
            RecurrenceInterval   = dto.IsRecurring ? dto.RecurrenceInterval : null,
            RecurrenceEndDate    = dto.IsRecurring ? dto.RecurrenceEndDate?.Date : null,
            NextOccurrenceDate   = dto.IsRecurring ? dto.NextOccurrenceDate?.Date : null,
        };

        var dependencyIds = NormalizeDependencyIds(dto.DependsOnTaskIds, task.Id);
        await ValidateDependenciesAsync(task.Id, dto.ProjectId, dependencyIds, userId, cancellationToken)
            .ConfigureAwait(false);
        task.Dependencies = dependencyIds.Select(dependencyId => new TaskDependency
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            DependsOnTaskId = dependencyId
        }).ToList();

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

        ValidateRecurrence(dto.IsRecurring, dto.RecurrenceType, dto.RecurrenceInterval,
            dto.NextOccurrenceDate, dto.RecurrenceEndDate);

        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var requiresSerializedDependencyState = dto.DependsOnTaskIds is not null ||
                                                dto.Status is TaskItemStatus.InProgress or TaskItemStatus.Done;
        if (!requiresSerializedDependencyState)
        {
            return await UpdateCoreAsync(dto, userId, cancellationToken).ConfigureAwait(false);
        }

        return await context.ExecuteSerializedTaskDependencyMutationAsync(
                userId,
                transactionCancellationToken => UpdateCoreAsync(dto, userId, transactionCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskItemDto> UpdateCoreAsync(
        UpdateTaskDto dto,
        string userId,
        CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .Include(t => t.Dependencies)
            .FirstOrDefaultAsync(t => t.Id == dto.Id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{dto.Id}' was not found.");

        // Optimistic concurrency: compare against the token captured when the caller
        // loaded the task so edits made elsewhere since then are detected.
        if (dto.RowVersion is not null)
            context.Entry(task).Property(t => t.RowVersion).OriginalValue = dto.RowVersion;

        var dependencyIds = dto.DependsOnTaskIds is null
            ? task.Dependencies.Select(d => d.DependsOnTaskId).ToList()
            : NormalizeDependencyIds(dto.DependsOnTaskIds, task.Id);
        if (dto.DependsOnTaskIds is not null)
        {
            await ValidateDependenciesAsync(task.Id, task.ProjectId, dependencyIds, userId, cancellationToken)
                .ConfigureAwait(false);
        }

        var statusChanged = dto.Status != task.Status;
        if (dto.Status is TaskItemStatus.InProgress or TaskItemStatus.Done)
            await EnsurePrerequisitesCompletedAsync(dependencyIds, userId, cancellationToken).ConfigureAwait(false);

        task.Title       = dto.Title.Trim();
        task.Description = dto.Description?.Trim();
        task.Priority    = dto.Priority;
        task.Complexity  = dto.Complexity;
        task.DueDate     = dto.DueDate;
        task.IsRecurring          = dto.IsRecurring;
        task.RecurrenceType       = dto.IsRecurring ? dto.RecurrenceType : null;
        task.RecurrenceInterval   = dto.IsRecurring ? dto.RecurrenceInterval : null;
        task.RecurrenceEndDate    = dto.IsRecurring ? dto.RecurrenceEndDate?.Date : null;
        task.NextOccurrenceDate   = dto.IsRecurring ? dto.NextOccurrenceDate?.Date : null;

        if (dto.DependsOnTaskIds is not null)
        {
            var existingDependencyIds = task.Dependencies.Select(d => d.DependsOnTaskId).ToHashSet();
            if (!existingDependencyIds.SetEquals(dependencyIds))
            {
                context.TaskDependencies.RemoveRange(task.Dependencies);
                context.TaskDependencies.AddRange(dependencyIds.Select(dependencyId => new TaskDependency
                {
                    Id = Guid.NewGuid(),
                    TaskId = task.Id,
                    DependsOnTaskId = dependencyId
                }));
            }
        }

        // Handle status transition — keep CompletedDate in sync
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

        if (dto.Status == TaskItemStatus.Done && statusChanged && task.IsRecurring)
        {
            await PrepareRecurringOccurrenceAsync(task, userId, cancellationToken).ConfigureAwait(false);
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

        return ToDto(task) with { DependsOnTaskIds = dependencyIds };
    }

    public async Task<TaskItemDto> CompleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        return await context.ExecuteSerializedTaskDependencyMutationAsync(
                userId,
                transactionCancellationToken => CompleteCoreAsync(id, userId, transactionCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskItemDto> CompleteCoreAsync(
        Guid id,
        string userId,
        CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        var now = DateTime.UtcNow;
        var changed = false;

        if (task.Status != TaskItemStatus.Done)
        {
            await EnsurePrerequisitesCompletedAsync(task.Id, userId, cancellationToken).ConfigureAwait(false);
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

        if (task.Status == TaskItemStatus.Done && task.IsRecurring)
        {
            await PrepareRecurringOccurrenceAsync(task, userId, cancellationToken).ConfigureAwait(false);
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

        return await context.ExecuteSerializedTaskDependencyMutationAsync(
                userId,
                transactionCancellationToken => SetInProgressCoreAsync(id, userId, transactionCancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TaskItemDto> SetInProgressCoreAsync(
        Guid id,
        string userId,
        CancellationToken cancellationToken)
    {
        var task = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        if (task.Status != TaskItemStatus.InProgress)
        {
            await EnsurePrerequisitesCompletedAsync(task.Id, userId, cancellationToken).ConfigureAwait(false);
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

        if (task.IsArchived)
            return;

        var now = DateTime.UtcNow;
        var archiveOperationId = Guid.NewGuid();
        task.IsArchived    = true;
        task.ArchivedAtUtc = now;
        task.ArchiveOperationId = archiveOperationId;
        task.IsCurrentTask = false;

        // Cascade archive to subtasks
        foreach (var sub in task.Subtasks.Where(s => !s.IsArchived))
        {
            sub.IsArchived    = true;
            sub.ArchivedAtUtc = now;
            sub.ArchiveOperationId = archiveOperationId;
            sub.IsCurrentTask = false;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Removing a subtask from the active set may complete (or reopen) the parent.
        if (task.ParentTaskId.HasValue)
        {
            await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TaskItemDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var task = await context.Tasks
            .Include(t => t.Project)
            .Include(t => t.ParentTask)
            .Include(t => t.Subtasks)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        if (!task.IsArchived)
            return ToDto(task);

        if (task.Project.IsArchived)
            throw new InvalidOperationException("Restore the archived project to restore tasks archived with it.");
        if (task.ParentTask?.IsArchived == true)
            throw new InvalidOperationException("Restore the parent task before restoring this subtask.");

        var operationId = task.ArchiveOperationId;
        RestoreTask(task);

        if (operationId.HasValue)
        {
            foreach (var subtask in task.Subtasks.Where(s => s.IsArchived && s.ArchiveOperationId == operationId))
                RestoreTask(subtask);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (task.ParentTaskId.HasValue)
            await SyncParentProgressAsync(task.ParentTaskId.Value, userId, cancellationToken).ConfigureAwait(false);

        return ToDto(task);
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

        // Load and validate the target before changing the existing current task. All
        // flag changes are persisted in one SaveChanges transaction.
        var candidates = await context.Tasks
            .Include(t => t.Project)
            .Where(t => t.UserId == userId && (t.Id == taskId || t.IsCurrentTask))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var task = candidates.FirstOrDefault(t => t.Id == taskId)
            ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        if (task.IsArchived || task.Project.IsArchived || task.Status is TaskItemStatus.Done or TaskItemStatus.Archived)
            throw new InvalidOperationException("Only an active, incomplete task can be set as the current task.");

        foreach (var current in candidates.Where(t => t.IsCurrentTask && t.Id != taskId))
            current.IsCurrentTask = false;

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

    public async Task<TaskItemDto?> CreateRecurringOccurrenceAsync(Guid taskId, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var template = await context.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId && t.UserId == userId, ct)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        if (!template.IsRecurring)
            throw new InvalidOperationException($"Task '{taskId}' is not a recurring task.");

        if (template.Status != TaskItemStatus.Done)
            throw new InvalidOperationException("A recurring task creates its next occurrence when it is completed.");

        var occurrence = await PrepareRecurringOccurrenceAsync(template, userId, ct).ConfigureAwait(false);

        await context.SaveChangesAsync(ct).ConfigureAwait(false);
        return occurrence is null ? null : ToDto(occurrence);
    }

    public async Task AddDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        _ = await context.ExecuteSerializedTaskDependencyMutationAsync(
                userId,
                transactionCancellationToken => AddDependencyCoreAsync(
                    taskId,
                    dependsOnTaskId,
                    userId,
                    transactionCancellationToken),
                ct)
            .ConfigureAwait(false);
    }

    private async Task<bool> AddDependencyCoreAsync(
        Guid taskId,
        Guid dependsOnTaskId,
        string userId,
        CancellationToken cancellationToken)
    {
        var taskState = await context.Tasks
            .Where(t => t.Id == taskId && t.UserId == userId && !t.IsArchived)
            .Select(t => new { t.ProjectId, t.Status })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (taskState is null)
            throw new KeyNotFoundException($"Task '{taskId}' was not found.");

        var dependencyIds = NormalizeDependencyIds([dependsOnTaskId], taskId);
        await ValidateDependenciesAsync(taskId, taskState.ProjectId, dependencyIds, userId, cancellationToken)
            .ConfigureAwait(false);

        var alreadyExists = await context.TaskDependencies
            .AnyAsync(
                d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId,
                cancellationToken)
            .ConfigureAwait(false);
        if (alreadyExists) return false;

        if (taskState.Status is TaskItemStatus.InProgress or TaskItemStatus.Done)
            await EnsurePrerequisitesCompletedAsync(dependencyIds, userId, cancellationToken).ConfigureAwait(false);

        context.TaskDependencies.Add(new TaskDependency
        {
            Id              = Guid.NewGuid(),
            TaskId          = taskId,
            DependsOnTaskId = dependsOnTaskId,
        });

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken ct = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(ct).ConfigureAwait(false);

        var dep = await context.TaskDependencies
            .FirstOrDefaultAsync(
                d => d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId && d.Task.UserId == userId,
                ct)
            .ConfigureAwait(false);
        if (dep is null) return;

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

    private async Task<TaskItem?> PrepareRecurringOccurrenceAsync(
        TaskItem template,
        string userId,
        CancellationToken cancellationToken)
    {
        ValidateRecurrence(template.IsRecurring, template.RecurrenceType, template.RecurrenceInterval,
            template.NextOccurrenceDate, template.RecurrenceEndDate, allowExhausted: true);

        var existing = await context.Tasks
            .FirstOrDefaultAsync(t => t.RecurrenceSourceTaskId == template.Id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
            return existing;

        var occurrenceDueDate = template.NextOccurrenceDate!.Value.Date;
        if (template.RecurrenceEndDate.HasValue && occurrenceDueDate > template.RecurrenceEndDate.Value.Date)
            return null;

        var followingDate = AdvanceRecurrence(
            occurrenceDueDate,
            template.RecurrenceType!.Value,
            template.RecurrenceInterval!.Value);
        var continues = !template.RecurrenceEndDate.HasValue || followingDate <= template.RecurrenceEndDate.Value.Date;

        var occurrence = new TaskItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProjectId = template.ProjectId,
            ParentTaskId = template.ParentTaskId,
            Title = template.Title,
            Description = template.Description,
            Priority = template.Priority,
            Complexity = template.Complexity,
            DueDate = occurrenceDueDate,
            Status = TaskItemStatus.Todo,
            RecurrenceSourceTaskId = template.Id,
            IsRecurring = continues,
            RecurrenceType = continues ? template.RecurrenceType : null,
            RecurrenceInterval = continues ? template.RecurrenceInterval : null,
            RecurrenceEndDate = continues ? template.RecurrenceEndDate : null,
            NextOccurrenceDate = continues ? followingDate : null,
        };

        template.NextOccurrenceDate = followingDate;
        context.Tasks.Add(occurrence);
        return occurrence;
    }

    private static DateTime AdvanceRecurrence(DateTime date, RecurrenceType recurrenceType, int interval) =>
        recurrenceType switch
        {
            RecurrenceType.Daily => date.AddDays(interval),
            RecurrenceType.Weekly => date.AddDays(7 * interval),
            RecurrenceType.Monthly => date.AddMonths(interval),
            RecurrenceType.Yearly => date.AddYears(interval),
            _ => throw new ArgumentOutOfRangeException(nameof(recurrenceType)),
        };

    private static List<Guid> NormalizeDependencyIds(IReadOnlyList<Guid>? dependencyIds, Guid taskId)
    {
        var normalized = dependencyIds?.Distinct().ToList() ?? [];
        if (normalized.Contains(taskId))
            throw new InvalidOperationException("A task cannot depend on itself.");

        return normalized;
    }

    private async Task ValidateDependenciesAsync(
        Guid taskId,
        Guid projectId,
        IReadOnlyList<Guid> dependencyIds,
        string userId,
        CancellationToken cancellationToken)
    {
        if (dependencyIds.Count == 0)
            return;

        var dependencies = await context.Tasks
            .AsNoTracking()
            .Where(task => dependencyIds.Contains(task.Id) && task.UserId == userId)
            .Select(task => new { task.Id, task.ProjectId, task.IsArchived })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (dependencies.Count != dependencyIds.Count)
            throw new KeyNotFoundException("One or more prerequisite tasks were not found.");
        if (dependencies.Any(task => task.IsArchived))
            throw new InvalidOperationException("An archived task cannot be used as a prerequisite.");
        if (dependencies.Any(task => task.ProjectId != projectId))
            throw new InvalidOperationException("Task prerequisites must belong to the same project.");

        var edges = await context.TaskDependencies
            .AsNoTracking()
            .Where(edge => edge.Task.UserId == userId && edge.TaskId != taskId)
            .Select(edge => new { edge.TaskId, edge.DependsOnTaskId })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = new Stack<Guid>(dependencyIds);
        var visited = new HashSet<Guid>();
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (current == taskId)
                throw new InvalidOperationException("This dependency would create a cycle.");

            foreach (var next in edges.Where(edge => edge.TaskId == current).Select(edge => edge.DependsOnTaskId))
                pending.Push(next);
        }
    }

    private async Task EnsurePrerequisitesCompletedAsync(
        Guid taskId,
        string userId,
        CancellationToken cancellationToken)
    {
        var dependencyIds = await context.TaskDependencies
            .AsNoTracking()
            .Where(edge => edge.TaskId == taskId && edge.Task.UserId == userId)
            .Select(edge => edge.DependsOnTaskId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await EnsurePrerequisitesCompletedAsync(dependencyIds, userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsurePrerequisitesCompletedAsync(
        IReadOnlyList<Guid> dependencyIds,
        string userId,
        CancellationToken cancellationToken)
    {
        if (dependencyIds.Count == 0)
            return;

        var completedCount = await context.Tasks
            .AsNoTracking()
            .CountAsync(task => dependencyIds.Contains(task.Id) && task.UserId == userId &&
                                !task.IsArchived && task.Status == TaskItemStatus.Done, cancellationToken)
            .ConfigureAwait(false);

        if (completedCount != dependencyIds.Count)
            throw new InvalidOperationException("Complete all prerequisite tasks before starting or completing this task.");
    }

    private static void ValidateRecurrence(
        bool isRecurring,
        RecurrenceType? recurrenceType,
        int? recurrenceInterval,
        DateTime? nextOccurrenceDate,
        DateTime? recurrenceEndDate,
        bool allowExhausted = false)
    {
        if (!isRecurring) return;
        if (!recurrenceType.HasValue)
            throw new ArgumentException("A recurring task requires a recurrence type.");
        if (recurrenceInterval is null or <= 0)
            throw new ArgumentException("A recurring task requires a positive recurrence interval.");
        if (!nextOccurrenceDate.HasValue)
            throw new ArgumentException("A recurring task requires a next occurrence date.");
        if (!allowExhausted && recurrenceEndDate.HasValue && recurrenceEndDate.Value.Date < nextOccurrenceDate.Value.Date)
            throw new ArgumentException("The recurrence end date cannot be before the next occurrence date.");
    }

    private static void RestoreTask(TaskItem task)
    {
        task.IsArchived = false;
        task.ArchivedAtUtc = null;
        task.ArchiveOperationId = null;
    }

    private static TaskItemDto ToDto(TaskItem t) => new(
        t.Id, t.Title, t.Description, t.Status, t.Priority,
        t.DueDate, t.CompletedDate, t.IsArchived, t.IsCurrentTask, t.ProjectId, t.ParentTaskId,
        t.CreatedAtUtc, t.UpdatedAtUtc,
        SubtaskCount: 0, DoneSubtaskCount: 0, Complexity: t.Complexity, SortOrder: t.SortOrder,
        IsRecurring: t.IsRecurring, RecurrenceType: t.RecurrenceType,
        RecurrenceInterval: t.RecurrenceInterval, RecurrenceEndDate: t.RecurrenceEndDate,
        NextOccurrenceDate: t.NextOccurrenceDate, RowVersion: t.RowVersion,
        DependsOnTaskIds: t.Dependencies.Select(d => d.DependsOnTaskId).ToList());
}
