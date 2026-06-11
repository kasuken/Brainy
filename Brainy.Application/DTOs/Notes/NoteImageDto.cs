namespace Brainy.Application.DTOs.Notes;

/// <summary>Metadata describing a stored note image (without the binary payload).</summary>
public record NoteImageDto(
    Guid Id,
    Guid? NoteId,
    string FileName,
    string ContentType,
    long SizeBytes);
