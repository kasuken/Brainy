using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A reusable blueprint for creating a <see cref="Project"/>: a name pattern, default
/// area/goal linkage, and a default task list with due dates expressed relative to the
/// day the template is instantiated. Templates count against no entitlement limit;
/// instantiating one is a plain <see cref="Project"/>/<see cref="TaskItem"/> create that
/// goes through the normal creation services, so the usual active-project limit still
/// applies at that point.
/// </summary>
public class ProjectTemplate : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Display name of the template itself (shown in the template picker).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Default name given to the project created from this template. May contain the
    /// tokens <c>{Date}</c> and <c>{Year}</c>, resolved against the user's calendar
    /// date at instantiation time.
    /// </summary>
    public string ProjectNamePattern { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? DesiredOutcome { get; set; }

    public ProjectPriority DefaultPriority { get; set; } = ProjectPriority.Medium;

    public Guid? DefaultAreaId { get; set; }

    public Area? DefaultArea { get; set; }

    public Guid? DefaultGoalId { get; set; }

    public Goal? DefaultGoal { get; set; }

    /// <summary>
    /// True for the small starter set seeded automatically for every user so the
    /// feature is useful on day one. Built-in templates behave like any other
    /// template — they may be edited or deleted like a user-authored one.
    /// </summary>
    public bool IsBuiltIn { get; set; }

    public ICollection<ProjectTemplateTask> Tasks { get; set; } = new List<ProjectTemplateTask>();
}
