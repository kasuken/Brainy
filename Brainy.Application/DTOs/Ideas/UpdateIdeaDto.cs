using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Ideas;

/// <summary>Payload for updating an existing <see cref="Domain.Entities.Idea"/>.</summary>
public record UpdateIdeaDto(
    Guid Id,
    string Title,
    string? Description,
    Guid? AreaId,
    IdeaPriority Priority,
    IdeaStatus Status,
    string? Research,
    string? Competitors,
    string? Notes);
