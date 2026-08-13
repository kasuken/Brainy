namespace Brainy.Application.DTOs.Highlights;

/// <summary>Payload for creating a new highlight on a note.</summary>
public record CreateHighlightDto(
    Guid NoteId,
    string Text,
    string? Annotation,
    int Layer = 1,
    int? StartOffset = null,
    int? EndOffset = null);
