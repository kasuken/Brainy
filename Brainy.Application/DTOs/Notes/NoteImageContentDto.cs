namespace Brainy.Application.DTOs.Notes;

/// <summary>The binary content of a stored note attachment, used when serving it over HTTP.</summary>
public record NoteImageContentDto(
    byte[] Data,
    string ContentType,
    string FileName);
