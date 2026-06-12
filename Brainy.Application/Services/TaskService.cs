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
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return task is null ? null : ToDto(task);
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
                        s.CreatedAtUtc, s.UpdatedAtUtc, 0, 0, null))
                    .ToList()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Priority is persisted as a string, so ordering by it in the database would be
        // lexicographic rather than by severity. Order in memory using the enum value instead.
        return tasks
            .Select(t => t with { Subtasks = OrderTasks(t.Subtasks) })
            .OrderByDescending(t => t.Priority)
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
            DueDate      = dto.DueDate,
            Status       = TaskItemStatus.Todo,
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

        task.Title       = dto.Title.Trim();
        task.Description = dto.Description?.Trim();
        task.Priority    = dto.Priority;
        task.DueDate     = dto.DueDate;

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

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

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
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Task '{id}' was not found.");

        if (task.Status != TaskItemStatus.Done)
        {
            task.Status        = TaskItemStatus.Done;
            task.CompletedDate = DateTime.UtcNow;
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
        SubtaskCount: 0, DoneSubtaskCount: 0);
}
