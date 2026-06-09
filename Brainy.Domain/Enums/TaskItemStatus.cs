namespace Brainy.Domain.Enums;

/// <summary>
/// Execution status of a task or subtask.
/// </summary>
public enum TaskItemStatus
{
    Todo = 0,
    InProgress = 1,
    Blocked = 2,
    Done = 3
}
