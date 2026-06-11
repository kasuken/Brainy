using Brainy.Application.DTOs.Notes;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for storing and retrieving note images as binary in the database.</summary>
public interface INoteImageService
{
    /// <summary>Maximum allowed size, in bytes, for a single uploaded image.</summary>
    const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>
    /// Stores an uploaded image for the current user and returns its metadata.
    /// Validates the content type and size.
    /// </summary>
    Task<NoteImageDto> UploadAsync(UploadNoteImageDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the binary content of an image owned by the specified user, or null when it
    /// does not exist or belongs to another user. Used by the image-serving endpoint, which
    /// resolves the user from the HTTP request rather than a Blazor circuit.
    /// </summary>
    Task<NoteImageContentDto?> GetContentAsync(Guid id, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links previously uploaded images to a note once it has been saved. Only images owned
    /// by the current user are affected. Returns the number of images updated.
    /// </summary>
    Task<int> AssociateWithNoteAsync(Guid noteId, IEnumerable<Guid> imageIds, CancellationToken cancellationToken = default);
}
