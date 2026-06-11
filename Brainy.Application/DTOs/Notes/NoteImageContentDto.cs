namespace Brainy.Application.DTOs.Notes;

/// <summary>The binary content of a stored note image, used when serving it over HTTP.</summary>
public record NoteImageContentDto(
    byte[] Data,
    string ContentType,
    string FileName);
