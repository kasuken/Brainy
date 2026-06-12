using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// An immutable audit event on a Goal. Activities accumulate over the goal's lifetime
/// and are shown in the timeline view. Ownership is inherited through the parent Goal.
/// </summary>
public class GoalActivity : BaseEntity
{
    public Guid GoalId { get; set; }

    public Goal Goal { get; set; } = null!;

    public GoalActivityType ActivityType { get; set; }

    /// <summary>Human-readable summary of what changed.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Serialised previous value, when applicable (e.g. old status name).</summary>
    public string? OldValue { get; set; }

    /// <summary>Serialised new value, when applicable (e.g. new status name).</summary>
    public string? NewValue { get; set; }
}
