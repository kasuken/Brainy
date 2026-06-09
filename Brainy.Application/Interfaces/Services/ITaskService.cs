using Brainy.Application.DTOs.Tasks;

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
}
