using Brainy.Application.DTOs.Tasks;
using Brainy.Domain.Enums;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing tasks and subtasks within a project.</summary>
public interface ITaskService
{
    /// <summary>Returns a single task by ID. Returns null if not found or not owned by the current user.</summary>
    Task<TaskItemDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all non-archived top-level tasks for a project, ordered by priority then due date.</summary>
    Task<IReadOnlyList<TaskItemDto>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Creates a new task (or subtask when <see cref="CreateTaskDto.ParentTaskId"/> is set).</summary>
    Task<TaskItemDto> CreateAsync(CreateTaskDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates title, description, status, priority, and due date of an existing task.</summary>
    Task<TaskItemDto> UpdateAsync(UpdateTaskDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a task as <see cref="Domain.Enums.TaskItemStatus.Done"/> and records <c>CompletedDate</c>.
    /// Idempotent — calling on an already-done task is a no-op.
    /// </summary>
    Task<TaskItemDto> CompleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a task as <see cref="Domain.Enums.TaskItemStatus.InProgress"/>.
    /// </summary>
    Task<TaskItemDto> SetInProgressAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reopens a completed task: sets status back to <see cref="Domain.Enums.TaskItemStatus.Todo"/>
    /// and clears <c>CompletedDate</c>.
    /// </summary>
    Task<TaskItemDto> ReopenAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-archives a task. Archived tasks are excluded from all active work views.
    /// Also archives any direct subtasks.
    /// </summary>
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a task and all its subtasks.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns the task currently flagged as the Current Task for the user, or null if none is set.</summary>
    Task<TaskItemDto?> GetCurrentTaskAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Designates the specified task as the user's Current Task.
    /// Clears the flag on all other tasks for that user first, ensuring only one Current Task exists at a time.
    /// </summary>
    Task<TaskItemDto> SetCurrentTaskAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>Clears the Current Task flag from all tasks belonging to the current user.</summary>
    Task ClearCurrentTaskAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the display order of cards within a project column.
    /// Assigns <see cref="Domain.Entities.TaskItem.SortOrder"/> based on each task's index in
    /// <paramref name="orderedTaskIds"/>. Unknown IDs are silently skipped.
    /// </summary>
    Task ReorderAsync(Guid projectId, TaskItemStatus status, IReadOnlyList<Guid> orderedTaskIds, CancellationToken ct = default);

    // ── Recurrence ────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns the next occurrence of a recurring task template. The new task is a
    /// one-off copy with <c>Status = Todo</c> and <c>DueDate = template.NextOccurrenceDate</c>.
    /// The template's <see cref="Domain.Entities.TaskItem.NextOccurrenceDate"/> is advanced by one interval.
    /// </summary>
    Task<TaskItemDto> CreateRecurringOccurrenceAsync(Guid taskId, CancellationToken ct = default);

    // ── Dependencies ─────────────────────────────────────────────────────────

    /// <summary>Records that <paramref name="taskId"/> depends on <paramref name="dependsOnTaskId"/>. Idempotent.</summary>
    Task AddDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken ct = default);

    /// <summary>Removes the dependency between <paramref name="taskId"/> and <paramref name="dependsOnTaskId"/>. No-op if the relationship does not exist.</summary>
    Task RemoveDependencyAsync(Guid taskId, Guid dependsOnTaskId, CancellationToken ct = default);
}
