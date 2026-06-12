namespace Brainy.Application.DTOs.Highlights;

/// <summary>Read model for a single highlight on a note.</summary>
public record HighlightDto(
    Guid Id,
    Guid NoteId,
    string Text,
    string? Annotation,
    int Layer,
    DateTime CreatedAtUtc);
