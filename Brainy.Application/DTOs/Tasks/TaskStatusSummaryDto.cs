namespace Brainy.Application.DTOs.Tasks;

/// <summary>Snapshot of task counts grouped by status.</summary>
public record TaskStatusSummaryDto(int TodoCount, int InProgressCount, int WaitingCount, int DoneCount);
