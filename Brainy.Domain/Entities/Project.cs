namespace Brainy.Domain.Entities;

/// <summary>
/// A short-term outcome with a deadline (PARA: Project). Archived projects and their
/// tasks are excluded from active work queries by default.
/// </summary>
public class Project : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>When true, the project and its tasks are treated as archived context.</summary>
    public bool IsArchived { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    /// <summary>Higher values surface the project's work more prominently on Today.</summary>
    public bool IsPriority { get; set; }

    public Guid? AreaId { get; set; }

    public Area? Area { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public ICollection<Output> Outputs { get; set; } = new List<Output>();
}
