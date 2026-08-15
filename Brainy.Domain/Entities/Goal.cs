using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A long-horizon outcome that a user is working toward, optionally scoped to an Area.
/// Goals can be broken into Milestones and supported by one or more Projects.
/// </summary>
public class Goal : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public GoalStatus Status { get; set; } = GoalStatus.Planned;

    /// <summary>When the user aims to achieve this goal.</summary>
    public DateTime? TargetDate { get; set; }

    /// <summary>Populated when Status transitions to Achieved.</summary>
    public DateTime? AchievedDate { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public string? ArchivedReason { get; set; }

    public Guid? AreaId { get; set; }

    public Area? Area { get; set; }

    public ICollection<GoalMilestone> Milestones { get; set; } = new List<GoalMilestone>();

    /// <summary>Projects that contribute toward achieving this goal.</summary>
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}
