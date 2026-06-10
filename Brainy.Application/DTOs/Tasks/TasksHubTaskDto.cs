using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>Read-only projection used on the Tasks Hub dashboard.</summary>
public record TasksHubTaskDto(
    Guid Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDate,
    Guid ProjectId,
    string ProjectName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
