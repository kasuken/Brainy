namespace Brainy.Application.DTOs.Notes;

/// <summary>Payload for uploading a new note attachment (for example, pasted or attached from the browser).</summary>
public record UploadNoteImageDto(
    byte[] Data,
    string ContentType,
    string FileName,
    Guid? NoteId = null);
