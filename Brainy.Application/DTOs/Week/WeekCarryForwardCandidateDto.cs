namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Represents an unfinished task selected last week that may be explicitly
/// carried into the current week.
/// </summary>
public record WeekCarryForwardCandidateDto(
    WeekTaskCardDto Task,
    DateTime PreviousWeekStartDate,
    bool CanCarryForward,
    bool AlreadySelectedThisWeek,
    string? CarryForwardReason);
