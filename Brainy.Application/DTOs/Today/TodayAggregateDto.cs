using Brainy.Application.DTOs.Projects;
using Brainy.Application.DTOs.Tasks;

namespace Brainy.Application.DTOs.Today;

/// <summary>
/// A single aggregated snapshot of everything the Today screen needs,
/// assembled by <see cref="Interfaces.Services.ITodayService.GetTodayAggregateAsync"/>.
/// </summary>
public record TodayAggregateDto(
    TodayTaskItemDto? CurrentTask,
    IReadOnlyList<TodayTaskItemDto> HighPriorityWork,
    IReadOnlyList<TodayTaskItemDto> Overdue,
    IReadOnlyList<TodayTaskItemDto> DueToday,
    IReadOnlyList<TodayTaskItemDto> DueThisWeek,
    IReadOnlyList<TodayTaskItemDto> NextTasks,
    int InboxCount,
    bool InboxWarning,
    int InboxWarningThreshold,
    IReadOnlyList<ProjectSummaryDto> PrioritizedProjects);
