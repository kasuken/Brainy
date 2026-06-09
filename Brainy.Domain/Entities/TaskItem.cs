using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A unit of work belonging to a project. Subtasks are modeled as tasks with a
/// <see cref="ParentTaskId"/>. Mapped to the "Task" table; named TaskItem to avoid
/// clashing with <see cref="System.Threading.Tasks.Task"/>.
/// </summary>
public class TaskItem : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskItemStatus Status { get; set; }

    public TaskPriority Priority { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>Populated when Status transitions to Done.</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>Archived tasks are excluded from active work screens.</summary>
    public bool IsArchived { get; set; }

    /// <summary>
    /// When true, this task is the user's designated Current Task.
    /// Only one task per user may have this flag set; the service layer enforces this invariant.
    /// </summary>
    public bool IsCurrentTask { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    /// <summary>Set when this task is a subtask of another task.</summary>
    public Guid? ParentTaskId { get; set; }

    public TaskItem? ParentTask { get; set; }

    public ICollection<TaskItem> Subtasks { get; set; } = new List<TaskItem>();
}
