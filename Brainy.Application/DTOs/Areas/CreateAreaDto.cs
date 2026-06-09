namespace Brainy.Application.DTOs.Areas;

/// <summary>Payload for creating a new area.</summary>
public record CreateAreaDto(
    string Name,
    string? Description = null);
