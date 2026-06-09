namespace Brainy.Application.DTOs.Areas;

/// <summary>Read-only projection of a <see cref="Domain.Entities.Area"/>.</summary>
public record AreaDto(
    Guid Id,
    string Name,
    string? Description,
    string? Purpose,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
