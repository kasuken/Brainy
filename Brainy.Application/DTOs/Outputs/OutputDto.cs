using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Outputs;

/// <summary>Read-only projection of an <see cref="Domain.Entities.Output"/> for list views.</summary>
public record OutputDto(
    Guid Id,
    string Title,
    string? Description,
    OutputType Type,
    OutputStatus Status,
    bool IsAiGenerated,
    bool IsArchived,
    Guid? ProjectId,
    string? ProjectTitle,
    Guid? AreaId,
    string? AreaName,
    Guid? GoalId,
    string? GoalTitle,
    DateTime? PublishedDate,
    DateTime? ArchivedDate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    /// <summary>Concurrency token captured at load time; pass back on update or delete.</summary>
    byte[]? RowVersion = null);
