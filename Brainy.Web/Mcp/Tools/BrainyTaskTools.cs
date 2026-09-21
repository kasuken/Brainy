using System.ComponentModel;
using Brainy.Application.DTOs.Tasks;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using ModelContextProtocol.Server;

namespace Brainy.Web.Mcp.Tools;

/// <summary>
/// MCP tools for creating and progressing the signed-in user's tasks. Thin adapters over
/// <see cref="ITaskService"/>, scoped per request to the token's owner. Update is fetch-then-merge.
/// No hard-delete tool is exposed — archiving is reversible; deletion is not.
/// </summary>
[McpServerToolType]
internal sealed class BrainyTaskTools
{
    [McpServerTool(Name = "list_tasks")]
    [Description("List the non-archived tasks of a project, ordered by priority then due date.")]
    public static async Task<IReadOnlyList<McpTask>> ListTasksAsync(
        ITaskService taskService,
        [Description("The id of the project whose tasks to list.")] Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var tasks = await taskService.GetByProjectAsync(projectId, cancellationToken).ConfigureAwait(false);
        return tasks.Select(McpTask.From).ToList();
    }

    [McpServerTool(Name = "get_task")]
    [Description("Get a single task by its id, or null if it does not exist or is not the user's.")]
    public static async Task<McpTask?> GetTaskAsync(
        ITaskService taskService,
        [Description("The task's id.")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await taskService.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false);
        return task is null ? null : McpTask.From(task);
    }

    [McpServerTool(Name = "get_current_task")]
    [Description("Get the task the user currently has flagged as their single Current Task, or null.")]
    public static async Task<McpTask?> GetCurrentTaskAsync(
        ITaskService taskService,
        CancellationToken cancellationToken = default)
    {
        var task = await taskService.GetCurrentTaskAsync(cancellationToken).ConfigureAwait(false);
        return task is null ? null : McpTask.From(task);
    }

    [McpServerTool(Name = "create_task")]
    [Description("Create a task in a project (or a subtask when parentTaskId is set).")]
    public static async Task<McpTask> CreateTaskAsync(
        ITaskService taskService,
        [Description("The id of the project to add the task to.")] Guid projectId,
        [Description("The task title.")] string title,
        [Description("Optional description.")] string? description = null,
        [Description("Priority: Low, Medium, High, or Critical. Defaults to Medium.")] string? priority = null,
        [Description("Optional due date (UTC).")] DateTime? dueDate = null,
        [Description("Optional parent task id, to create this as a subtask.")] Guid? parentTaskId = null,
        CancellationToken cancellationToken = default)
    {
        var dto = new CreateTaskDto(
            ProjectId: projectId,
            Title: title,
            Description: description,
            Priority: McpToolHelpers.ParseEnum<TaskPriority>(priority, nameof(priority)) ?? TaskPriority.Medium,
            DueDate: dueDate,
            ParentTaskId: parentTaskId);

        var created = await taskService.CreateAsync(dto, cancellationToken).ConfigureAwait(false);
        return McpTask.From(created);
    }

    [McpServerTool(Name = "update_task")]
    [Description("Update fields of an existing task. Only supplied fields change; omit the rest. " +
                 "Valid status values: Todo, InProgress, Waiting, Done.")]
    public static async Task<McpTask> UpdateTaskAsync(
        ITaskService taskService,
        [Description("The id of the task to update.")] Guid taskId,
        [Description("New title, if changing it.")] string? title = null,
        [Description("New description, if changing it.")] string? description = null,
        [Description("New status: Todo, InProgress, Waiting, or Done.")] string? status = null,
        [Description("New priority: Low, Medium, High, or Critical.")] string? priority = null,
        [Description("New due date (UTC).")] DateTime? dueDate = null,
        CancellationToken cancellationToken = default)
    {
        var existing = await taskService.GetByIdAsync(taskId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No task with id {taskId} was found for the current user.");

        var dto = new UpdateTaskDto(
            Id: existing.Id,
            Title: title ?? existing.Title,
            Description: description ?? existing.Description,
            Status: McpToolHelpers.ParseEnum<TaskItemStatus>(status, nameof(status)) ?? existing.Status,
            Priority: McpToolHelpers.ParseEnum<TaskPriority>(priority, nameof(priority)) ?? existing.Priority,
            DueDate: dueDate ?? existing.DueDate,
            Complexity: existing.Complexity,
            IsRecurring: existing.IsRecurring,
            RecurrenceType: existing.RecurrenceType,
            RecurrenceInterval: existing.RecurrenceInterval,
            RecurrenceEndDate: existing.RecurrenceEndDate,
            NextOccurrenceDate: existing.NextOccurrenceDate,
            RowVersion: existing.RowVersion,
            DependsOnTaskIds: existing.DependsOnTaskIds);

        var updated = await taskService.UpdateAsync(dto, cancellationToken).ConfigureAwait(false);
        return McpTask.From(updated);
    }

    [McpServerTool(Name = "complete_task")]
    [Description("Mark a task as done. Idempotent — completing an already-done task is a no-op.")]
    public static async Task<McpTask> CompleteTaskAsync(
        ITaskService taskService,
        [Description("The id of the task to complete.")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await taskService.CompleteAsync(taskId, cancellationToken).ConfigureAwait(false);
        return McpTask.From(task);
    }

    [McpServerTool(Name = "start_task")]
    [Description("Mark a task as in progress.")]
    public static async Task<McpTask> StartTaskAsync(
        ITaskService taskService,
        [Description("The id of the task to start.")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await taskService.SetInProgressAsync(taskId, cancellationToken).ConfigureAwait(false);
        return McpTask.From(task);
    }

    [McpServerTool(Name = "reopen_task")]
    [Description("Reopen a completed task, setting it back to Todo and clearing its completion date.")]
    public static async Task<McpTask> ReopenTaskAsync(
        ITaskService taskService,
        [Description("The id of the task to reopen.")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await taskService.ReopenAsync(taskId, cancellationToken).ConfigureAwait(false);
        return McpTask.From(task);
    }

    [McpServerTool(Name = "set_current_task")]
    [Description("Designate a task as the user's single Current Task, clearing the flag from any other.")]
    public static async Task<McpTask> SetCurrentTaskAsync(
        ITaskService taskService,
        [Description("The id of the task to set as current.")] Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = await taskService.SetCurrentTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        return McpTask.From(task);
    }

    [McpServerTool(Name = "archive_task")]
    [Description("Archive a task (reversible). It and its subtasks leave active work views but are kept.")]
    public static async Task<string> ArchiveTaskAsync(
        ITaskService taskService,
        [Description("The id of the task to archive.")] Guid taskId,
        [Description("Optional reason recorded with the archive.")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await taskService.ArchiveAsync(taskId, cancellationToken, reason).ConfigureAwait(false);
        return $"Task {taskId} archived.";
    }
}

/// <summary>Compact task projection for MCP callers.</summary>
public record McpTask(
    Guid Id,
    string Title,
    [property: Description("Todo, InProgress, Waiting, Done, or Archived.")]
    string Status,
    [property: Description("Low, Medium, High, or Critical.")]
    string Priority,
    DateTime? DueDate,
    bool IsCurrentTask,
    Guid ProjectId,
    Guid? ParentTaskId,
    string? Description)
{
    public static McpTask From(TaskItemDto t) => new(
        t.Id,
        t.Title,
        t.Status.ToString(),
        t.Priority.ToString(),
        t.DueDate,
        t.IsCurrentTask,
        t.ProjectId,
        t.ParentTaskId,
        t.Description);
}
