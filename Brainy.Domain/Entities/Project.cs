using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A short-term outcome with a deadline (PARA: Project). Archived projects and their
/// tasks are excluded from active work queries by default.
/// </summary>
public class Project : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Visual identifier chosen by the user (for example: 🎯, 🚀).</summary>
    public string Emoji { get; set; } = ProjectEmojiDefaults.DefaultEmoji;

    public string? Description { get; set; }

    /// <summary>The intended end state — what "done" looks like for this project.</summary>
    public string? DesiredOutcome { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.NotStarted;

    public ProjectPriority Priority { get; set; } = ProjectPriority.Medium;

    public DateTime? StartDate { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>Populated when Status transitions to Completed.</summary>
    public DateTime? CompletedDate { get; set; }

    /// <summary>When true, the project and its tasks are treated as archived context.</summary>
    public bool IsArchived { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public Guid? AreaId { get; set; }

    public Area? Area { get; set; }

    public Guid? GoalId { get; set; }

    public Goal? Goal { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public ICollection<Output> Outputs { get; set; } = new List<Output>();
}
