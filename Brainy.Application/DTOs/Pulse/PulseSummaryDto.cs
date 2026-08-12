namespace Brainy.Application.DTOs.Pulse;

/// <summary>
/// Aggregate activity counts for a Pulse reporting period. Every count reflects items
/// whose relevant lifecycle timestamp (captured, processed, completed, etc.) falls
/// within the report's date range — not the item's current overall state.
/// </summary>
public record PulseSummaryDto(
    int NotesCaptured,
    int NotesProcessed,
    int NotesArchived,
    int HighlightsAdded,
    int SummariesCreated,
    int TasksCreated,
    int TasksCompleted,
    int TasksArchived,
    int ProjectsCreated,
    int ProjectsCompleted,
    int ProjectsArchived,
    int OutputsCreated,
    int OutputsPublished,
    int IdeasCaptured,
    int IdeasCommitted,
    int GoalsAchieved,
    /// <summary>Sum of every count above.</summary>
    int TotalActivities,
    /// <summary>Distinct calendar days (UTC) with at least one activity.</summary>
    int ActiveDays);
