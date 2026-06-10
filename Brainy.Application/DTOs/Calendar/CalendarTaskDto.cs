using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Calendar;

/// <summary>Lightweight projection of a <see cref="Domain.Entities.TaskItem"/> for calendar display.</summary>
public record CalendarTaskDto(
    Guid Id,
    string Title,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDate,
    Guid ProjectId,
    string ProjectName,
    Guid? AreaId,
    string? AreaName,
    bool IsOverdue);
