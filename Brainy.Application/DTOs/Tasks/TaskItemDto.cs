using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>Read-only projection of a <see cref="Domain.Entities.TaskItem"/>.</summary>
public record TaskItemDto(
    Guid Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDate,
    DateTime? CompletedDate,
    bool IsArchived,
    Guid ProjectId,
    Guid? ParentTaskId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int SubtaskCount,
    int DoneSubtaskCount);
