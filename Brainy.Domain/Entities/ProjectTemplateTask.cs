using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// One entry in a <see cref="ProjectTemplate"/>'s default task list. Scoped through its
/// parent template rather than owning a <see cref="IUserOwnedEntity.UserId"/> directly,
/// the same shape as <see cref="GoalMilestone"/> under <see cref="Goal"/>.
/// </summary>
public class ProjectTemplateTask : BaseEntity
{
    public Guid ProjectTemplateId { get; set; }

    public ProjectTemplate ProjectTemplate { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    public TaskComplexity? Complexity { get; set; }

    /// <summary>
    /// Days from the instantiation date (the user's calendar "today") that this task's
    /// due date should be offset by. Null means the created task has no due date.
    /// May be negative to express "should already be underway" relative to project start.
    /// </summary>
    public int? DueDateOffsetDays { get; set; }

    /// <summary>Position in the template's task list; lower values appear first.</summary>
    public int SortOrder { get; set; }
}
