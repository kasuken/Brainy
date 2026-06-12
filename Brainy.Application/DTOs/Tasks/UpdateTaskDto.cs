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
    TaskComplexity? Complexity = null);
