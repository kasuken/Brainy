using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>
/// Lightweight projection of a subtask surfaced inside its parent task's card on the
/// Today screen, so the user can review and change its status without leaving Today.
/// </summary>
public record TodaySubtaskItemDto(
    Guid Id,
    string Title,
    TaskItemStatus Status,
    DateTime? DueDate);
