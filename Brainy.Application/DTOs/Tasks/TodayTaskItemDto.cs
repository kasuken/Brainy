using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>
/// Lightweight projection used on the Today screen. Carries project name so
/// tasks from different projects can be rendered without an extra lookup.
/// </summary>
public record TodayTaskItemDto(
    Guid Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDate,
    Guid ProjectId,
    string ProjectName,
    DateTime CreatedAtUtc,
    int OverdueSubtaskCount = 0,
    int DueTodaySubtaskCount = 0);
