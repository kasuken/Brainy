namespace Brainy.Application.DTOs.Resources;

/// <summary>Payload for creating a new resource.</summary>
public record CreateResourceDto(
    string Name,
    string? Description = null,
    Guid? AreaId = null);
