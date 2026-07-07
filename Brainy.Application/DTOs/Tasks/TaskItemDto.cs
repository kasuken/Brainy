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
    bool IsCurrentTask,
    Guid ProjectId,
    Guid? ParentTaskId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int SubtaskCount,
    int DoneSubtaskCount,
    IReadOnlyList<TaskItemDto>? Subtasks = null,
    TaskComplexity? Complexity = null,
    int SortOrder = 0,
    bool IsRecurring = false,
    RecurrenceType? RecurrenceType = null,
    int? RecurrenceInterval = null,
    DateTime? RecurrenceEndDate = null,
    DateTime? NextOccurrenceDate = null,
    /// <summary>Concurrency token captured at load time; pass back on update to detect conflicts.</summary>
    byte[]? RowVersion = null);
