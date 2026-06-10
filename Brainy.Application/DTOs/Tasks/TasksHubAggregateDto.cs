namespace Brainy.Application.DTOs.Tasks;

/// <summary>Aggregate of all task lists and summary data for the Tasks Hub dashboard.</summary>
public record TasksHubAggregateDto(
    IReadOnlyList<TasksHubTaskDto> ActiveTasks,
    IReadOnlyList<TasksHubTaskDto> HighPriorityTasks,
    IReadOnlyList<TasksHubTaskDto> OnHoldTasks,
    IReadOnlyList<TasksHubTaskDto> OverdueTasks,
    IReadOnlyList<TasksHubTaskDto> NeedingAttentionTasks,
    IReadOnlyList<TasksHubTaskDto> WithoutDueDateTasks,
    IReadOnlyList<TasksHubTaskDto> StaleTasks,
    TaskStatusSummaryDto StatusSummary);
