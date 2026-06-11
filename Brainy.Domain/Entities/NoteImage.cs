using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// A binary image embedded in a note (for example, pasted from the clipboard).
/// The raw bytes are stored in the database and referenced from note Markdown via
/// the image-serving endpoint.
/// </summary>
public class NoteImage : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// The note this image belongs to. Null while the image was uploaded for a note
    /// that has not been saved yet; it is linked once the note is persisted.
    /// </summary>
    public Guid? NoteId { get; set; }

    public Note? Note { get; set; }

    /// <summary>Original or generated file name, used as the download/alt name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME type of the image (for example, <c>image/png</c>).</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Size of <see cref="Data"/> in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Raw image bytes.</summary>
    public byte[] Data { get; set; } = [];
}
