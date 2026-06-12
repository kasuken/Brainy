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

    /// <summary>Position within the kanban column; lower values appear first. Set by ReorderAsync.</summary>
    public int SortOrder { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public Guid ProjectId { get; set; }

    public Project Project { get; set; } = null!;

    /// <summary>
    /// Optional t-shirt size estimate of the effort or complexity of this task.
    /// Null means the user has not estimated yet.
    /// </summary>
    public TaskComplexity? Complexity { get; set; }

    /// <summary>Set when this task is a subtask of another task.</summary>
    public Guid? ParentTaskId { get; set; }

    public TaskItem? ParentTask { get; set; }

    public ICollection<TaskItem> Subtasks { get; set; } = new List<TaskItem>();

    // ── Recurrence ────────────────────────────────────────────────────────────

    /// <summary>When true, this task acts as a template for recurring occurrences.</summary>
    public bool IsRecurring { get; set; }

    public RecurrenceType? RecurrenceType { get; set; }

    /// <summary>How many units (days / weeks / months / years) between occurrences.</summary>
    public int? RecurrenceInterval { get; set; }

    /// <summary>Date after which no further occurrences should be created.</summary>
    public DateTime? RecurrenceEndDate { get; set; }

    /// <summary>Date of the next occurrence to be spawned via CreateRecurringOccurrenceAsync.</summary>
    public DateTime? NextOccurrenceDate { get; set; }

    // ── Dependencies ──────────────────────────────────────────────────────────

    /// <summary>Tasks that this task depends on (prerequisites).</summary>
    public ICollection<TaskDependency> Dependencies { get; set; } = [];

    /// <summary>Tasks that depend on this task.</summary>
    public ICollection<TaskDependency> Dependents { get; set; } = [];
}
