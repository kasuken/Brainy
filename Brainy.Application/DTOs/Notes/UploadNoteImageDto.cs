namespace Brainy.Application.DTOs.Notes;

/// <summary>Payload for uploading a new note image (for example, pasted from the clipboard).</summary>
public record UploadNoteImageDto(
    byte[] Data,
    string ContentType,
    string FileName,
    Guid? NoteId = null);
