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
    /// <summary>
    /// Count of all activity-log entries, including reopen/restore and output-archive
    /// transitions that are shown in the log but do not have a dedicated summary tile.
    /// </summary>
    int TotalActivities,
    /// <summary>Distinct calendar days in the user's configured time zone with activity.</summary>
    int ActiveDays);
