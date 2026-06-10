using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Calendar;

/// <summary>Optional filters applied when querying calendar tasks.</summary>
public record CalendarFilterDto(
    Guid? ProjectId = null,
    Guid? AreaId = null,
    TaskPriority? Priority = null,
    TaskItemStatus? Status = null,
    string? SearchTerm = null);
