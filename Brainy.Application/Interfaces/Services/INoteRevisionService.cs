using Brainy.Application.DTOs.Notes;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Reads, diffs and restores a note's revision history. Revisions themselves are
/// captured by <see cref="INoteService"/> (create/update/process) and by the external
/// import path, whichever mutates a note's title or content; this service is the
/// read/diff/restore side plus retention enforcement.
/// </summary>
public interface INoteRevisionService
{
    /// <summary>
    /// Hard cap on revisions retained per note, applied even when the user has not
    /// configured a <c>NoteRevision</c> <see cref="Domain.Entities.ArchiveRetentionRule"/>.
    /// Guarantees storage stays bounded rather than growing without limit.
    /// </summary>
    const int MaxRevisionsPerNote = 200;

    /// <summary>The note's revisions, newest first.</summary>
    Task<IReadOnlyList<NoteRevisionDto>> GetTimelineAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>A single revision of a note, or null if not found/not owned by the current user.</summary>
    Task<NoteRevisionDto?> GetByIdAsync(Guid noteId, Guid revisionId, CancellationToken cancellationToken = default);

    /// <summary>Line-based diff of title and content between two revisions of the same note.</summary>
    Task<NoteRevisionDiffDto> DiffAsync(
        Guid noteId, Guid fromRevisionId, Guid toRevisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores <paramref name="revisionId"/>'s title/content onto the note as a NEW revision
    /// (reason <see cref="Domain.Enums.NoteRevisionReason.Restore"/>) — never a destructive
    /// overwrite of history. When <paramref name="rowVersion"/> is supplied, the restore fails
    /// with <see cref="ConcurrencyConflictException"/> if the note changed since it was loaded.
    /// </summary>
    Task<NoteDto> RestoreAsync(
        Guid noteId, Guid revisionId, byte[]? rowVersion, CancellationToken cancellationToken = default);
}
