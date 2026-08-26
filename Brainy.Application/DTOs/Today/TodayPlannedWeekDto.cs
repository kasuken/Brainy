using Brainy.Application.DTOs.Tasks;

namespace Brainy.Application.DTOs.Today;

/// <summary>
/// Compact current-week planning projection used to connect deliberate Week
/// commitments with the Today execution surface.
/// </summary>
/// <param name="SelectedTaskCount">All tasks selected for the current week.</param>
/// <param name="CompletedTaskCount">Selected tasks completed during the week.</param>
/// <param name="ActionableTaskCount">Unfinished selections that are still actionable.</param>
/// <param name="NeedsReplanningCount">Unfinished selections that are no longer actionable.</param>
/// <param name="Tasks">
/// Actionable selected tasks. A task may also appear in other Today sections when
/// its status, priority, or due date makes it applicable there.
/// </param>
public record TodayPlannedWeekDto(
    int SelectedTaskCount,
    int CompletedTaskCount,
    int ActionableTaskCount,
    int NeedsReplanningCount,
    IReadOnlyList<TodayTaskItemDto> Tasks);
