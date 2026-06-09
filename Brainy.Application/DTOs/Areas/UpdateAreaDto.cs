namespace Brainy.Application.DTOs.Areas;

/// <summary>Payload for updating an existing area.</summary>
public record UpdateAreaDto(
    Guid Id,
    string Name,
    string? Description,
    string? Purpose);
