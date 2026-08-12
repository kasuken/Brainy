namespace Brainy.Domain.Enums;

/// <summary>Classifies a single entry in the Pulse activity log.</summary>
public enum PulseActivityType
{
    NoteCaptured,
    NoteProcessed,
    NoteArchived,
    HighlightAdded,
    SummaryCreated,
    TaskCreated,
    TaskCompleted,
    TaskArchived,
    ProjectCreated,
    ProjectCompleted,
    ProjectArchived,
    OutputCreated,
    OutputPublished,
    IdeaCaptured,
    IdeaCommitted,
    GoalAchieved,
}
