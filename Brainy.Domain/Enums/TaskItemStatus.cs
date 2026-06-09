namespace Brainy.Domain.Enums;

/// <summary>
/// Lifecycle status of a task or subtask.
/// </summary>
public enum TaskItemStatus
{
    Todo = 0,
    InProgress = 1,
    Waiting = 2,
    Done = 3,
    Archived = 4
}
