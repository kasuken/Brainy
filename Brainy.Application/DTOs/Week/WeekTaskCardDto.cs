using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Rich task projection used by the Week page across the picker, plan, and
/// attention surfaces.
/// </summary>
public record WeekTaskCardDto(
    Guid Id,
    Guid ProjectId,
    string ProjectName,
    string ProjectEmoji,
    ProjectStatus ProjectStatus,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDate,
    DateTime? CompletedDate,
    TaskComplexity? Complexity,
    bool IsCurrentFocus,
    bool IsSelectedForCurrentWeek,
    bool CanAddToWeek,
    string? SelectionBlockReason,
    int OverdueSubtaskCount = 0,
    int DueThisWeekSubtaskCount = 0,
    WeekNextActionDto? NextAction = null,
    bool HasUnresolvedDependency = false,
    string? AttentionReason = null,
    string? ReplanningReason = null);
