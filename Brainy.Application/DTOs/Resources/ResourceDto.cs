namespace Brainy.Application.DTOs.Resources;

/// <summary>Read-only projection of a <see cref="Domain.Entities.Resource"/>.</summary>
public record ResourceDto(
    Guid Id,
    string Name,
    string? Description,
    bool IsArchived,
    Guid? AreaId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
