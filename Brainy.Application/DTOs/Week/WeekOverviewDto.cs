namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Complete planning snapshot for the authenticated user's current working week.
/// </summary>
public record WeekOverviewDto(
    DateTime Today,
    DateTime WeekStartDate,
    DateTime WeekEndDate,
    int WeekNumber,
    int SelectedTaskCount,
    int CompletedSelectedTaskCount,
    int RemainingSelectedTaskCount,
    int RepresentedProjectCount,
    IReadOnlyList<WeekProjectOverviewDto> Projects,
    IReadOnlyList<WeekProjectPlanDto> SelectedTaskGroups,
    IReadOnlyList<WeekTaskCardDto> OverdueAttention,
    IReadOnlyList<WeekTaskCardDto> DueThisWeekAttention,
    IReadOnlyList<WeekTaskCardDto> NeedsReplanning,
    IReadOnlyList<WeekCarryForwardCandidateDto> CarryForwardCandidates);
