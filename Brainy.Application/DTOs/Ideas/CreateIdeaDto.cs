using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Ideas;

/// <summary>Payload for creating a new <see cref="Domain.Entities.Idea"/>.</summary>
public record CreateIdeaDto(
    string Title,
    string? Description,
    Guid? AreaId,
    IdeaPriority Priority = IdeaPriority.Medium);
