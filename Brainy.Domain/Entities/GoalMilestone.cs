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
}
