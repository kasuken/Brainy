namespace Brainy.Domain.Entities;

/// <summary>
/// A discrete checkpoint on the path to achieving a Goal.
/// Ownership is inherited through the parent Goal rather than carried directly.
/// </summary>
public class GoalMilestone : BaseEntity
{
    public Guid GoalId { get; set; }

    public Goal? Goal { get; set; }

    public string Title { get; set; } = string.Empty;

    public bool IsCompleted { get; set; }

    /// <summary>Populated when IsCompleted is set to true.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>
    /// The user-calendar date this checkpoint is due, if any. Like <see cref="Project.DueDate"/>
    /// and <see cref="TaskItem.DueDate"/>, this is the date the user intends, not a UTC instant —
    /// it is never converted through a time zone. Optional: milestones are often just an
    /// ordered checklist with no date, and only dated ones appear in the calendar feed.
    /// </summary>
    public DateTime? DueDate { get; set; }
}
