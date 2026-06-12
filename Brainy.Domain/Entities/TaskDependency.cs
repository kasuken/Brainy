using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Represents a "must finish before" relationship between two tasks.
/// <see cref="Task"/> cannot start until <see cref="DependsOnTask"/> is complete.
/// </summary>
public class TaskDependency : BaseEntity
{
    /// <summary>The task that has a prerequisite.</summary>
    public Guid TaskId { get; set; }

    public TaskItem Task { get; set; } = null!;

    /// <summary>The prerequisite task that must be completed first.</summary>
    public Guid DependsOnTaskId { get; set; }

    public TaskItem DependsOnTask { get; set; } = null!;
}
