namespace Brainy.Domain.Enums;

/// <summary>Distinguishes the three deadline sources exposed by the ICS calendar feed.</summary>
public enum CalendarFeedEventKind
{
    Task = 0,
    ProjectDeadline = 1,
    GoalMilestone = 2
}
