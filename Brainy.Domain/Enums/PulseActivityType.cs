namespace Brainy.Domain.Enums;

/// <summary>Classifies a single entry in the Pulse activity log.</summary>
public enum PulseActivityType
{
    NoteCaptured,
    NoteProcessed,
    NoteArchived,
    NoteRestored,
    HighlightAdded,
    SummaryCreated,
    TaskCreated,
    TaskCompleted,
    TaskArchived,
    TaskReopened,
    TaskRestored,
    ProjectCreated,
    ProjectCompleted,
    ProjectArchived,
    ProjectRestored,
    OutputCreated,
    OutputPublished,
    OutputArchived,
    OutputRestored,
    IdeaCaptured,
    IdeaCommitted,
    GoalAchieved,
}
