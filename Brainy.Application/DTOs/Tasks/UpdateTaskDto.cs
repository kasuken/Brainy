using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>Payload for updating an existing task.</summary>
public record UpdateTaskDto(
    Guid Id,
    string Title,
    string? Description = null,
    TaskItemStatus Status = TaskItemStatus.Todo,
    TaskPriority Priority = TaskPriority.Medium,
    DateTime? DueDate = null,
    TaskComplexity? Complexity = null,
    bool IsRecurring = false,
    RecurrenceType? RecurrenceType = null,
    int? RecurrenceInterval = null,
    DateTime? RecurrenceEndDate = null,
    DateTime? NextOccurrenceDate = null,
    /// <summary>
    /// Concurrency token from the loaded task. When provided, the update fails with a
    /// <see cref="Common.ConcurrencyConflictException"/> if the task changed since it was loaded.
    /// </summary>
    byte[]? RowVersion = null);
