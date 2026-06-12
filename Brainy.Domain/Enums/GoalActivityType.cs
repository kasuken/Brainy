namespace Brainy.Domain.Enums;

/// <summary>Classifies what kind of change a <see cref="Entities.GoalActivity"/> records.</summary>
public enum GoalActivityType
{
    Created,
    StatusChanged,
    TitleEdited,
    DescriptionEdited,
    TargetDateChanged,
    MilestoneAdded,
    MilestoneCompleted,
    Archived,
}
