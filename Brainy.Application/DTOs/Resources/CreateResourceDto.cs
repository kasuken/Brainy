namespace Brainy.Application.DTOs.Resources;

/// <summary>Payload for creating a new resource.</summary>
public record CreateResourceDto(
    string Name,
    string? Description = null,
    string? Topic = null,
    Guid? AreaId = null,
    IReadOnlyList<string>? Tags = null,
    string? Emoji = null);
