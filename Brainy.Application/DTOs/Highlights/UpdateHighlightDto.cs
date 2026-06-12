namespace Brainy.Application.DTOs.Highlights;

/// <summary>Payload for updating an existing highlight's annotation and layer.</summary>
public record UpdateHighlightDto(string? Annotation, int Layer);
