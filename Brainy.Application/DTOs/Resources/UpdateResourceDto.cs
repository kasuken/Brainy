namespace Brainy.Application.DTOs.Resources;

/// <summary>Payload for updating an existing resource.</summary>
public record UpdateResourceDto(
    Guid Id,
    string Name,
    string? Description,
    string? Topic,
    Guid? AreaId,
    IReadOnlyList<string>? Tags,
    string? Emoji = null);
