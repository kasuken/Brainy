using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>Payload for creating a new task or subtask.</summary>
public record CreateTaskDto(
    Guid ProjectId,
    string Title,
    string? Description = null,
    TaskPriority Priority = TaskPriority.Medium,
    DateTime? DueDate = null,
    Guid? ParentTaskId = null,
    TaskComplexity? Complexity = null,
    bool IsRecurring = false,
    RecurrenceType? RecurrenceType = null,
    int? RecurrenceInterval = null,
    DateTime? RecurrenceEndDate = null,
    DateTime? NextOccurrenceDate = null,
    IReadOnlyList<Guid>? DependsOnTaskIds = null);
