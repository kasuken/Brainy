using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>Filter and pagination parameters for querying tasks on the hub.</summary>
public record TasksHubFilterDto(
    Guid? ProjectId = null,
    TaskItemStatus? Status = null,
    TaskPriority? MinPriority = null,
    DateTime? DueBefore = null,
    DateTime? DueAfter = null,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20,
    TaskComplexity? Complexity = null);
