using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Tasks;

/// <summary>Specifies a bulk operation to apply to a set of tasks.</summary>
public record BulkTaskOperationDto(
    IReadOnlyList<Guid> TaskIds,
    TaskItemStatus? NewStatus = null,
    TaskPriority? NewPriority = null,
    DateTime? NewDueDate = null,
    Guid? NewProjectId = null,
    bool Archive = false);
