using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Week;

/// <summary>
/// Lightweight next-step context shown beside a planned or selectable parent task.
/// </summary>
/// <param name="Id">The subtask identifier.</param>
/// <param name="Title">The subtask title.</param>
/// <param name="Status">The current subtask status.</param>
/// <param name="DueDate">The optional subtask due date.</param>
public record WeekNextActionDto(
    Guid Id,
    string Title,
    TaskItemStatus Status,
    DateTime? DueDate);
