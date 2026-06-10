using Brainy.Application.DTOs.Notes;
using Brainy.Domain.Enums;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing notes.</summary>
public interface INoteService
{
    Task<IReadOnlyList<NoteDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<NoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NoteDto> CreateAsync(CreateNoteDto dto, CancellationToken cancellationToken = default);
    Task<NoteDto> UpdateAsync(UpdateNoteDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns notes with <see cref="NoteStatus.Inbox"/> status owned by the current user,
    /// ordered by capture date (oldest first — process what came in first).
    /// </summary>
    Task<IReadOnlyList<NoteDto>> GetInboxAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a note out of the Inbox by assigning a PARA category, an optional destination
    /// (project / area / resource) and a lifecycle status.
    /// </summary>
    Task<NoteDto> ProcessNoteAsync(ProcessNoteDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves multiple inbox notes to the same PARA category and status in a single
    /// database operation. Only notes owned by the current user are affected.
    /// </summary>
    Task<int> BulkProcessInboxAsync(
        IEnumerable<Guid> ids,
        ParaCategory category,
        NoteStatus status,
        Guid? projectId = null,
        Guid? areaId = null,
        Guid? resourceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves multiple notes to a new PARA category in a single database operation.
    /// Only notes owned by the current user are affected.
    /// </summary>
    Task<int> BulkMoveCategoryAsync(IEnumerable<Guid> ids, ParaCategory category, CancellationToken cancellationToken = default);

    /// <summary>Returns all notes not currently linked to any project — used by the "link existing" picker.</summary>
    Task<IReadOnlyList<NoteDto>> GetNotLinkedToProjectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Links an existing note to a project. Sets <c>ProjectId</c> and updates
    /// <c>ParaCategory</c> to <see cref="ParaCategory.Project"/> when it was not already
    /// a resource note. Moves Inbox notes to <see cref="NoteStatus.Active"/> automatically.
    /// </summary>
    Task<NoteDto> LinkToProjectAsync(Guid noteId, Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the project association from a note (sets <c>ProjectId</c> to null).
    /// The note and its content are preserved; only the project link is cleared.
    /// </summary>
    Task<NoteDto> UnlinkFromProjectAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles the <c>IsFavorite</c> flag on a note.
    /// Returns the updated <see cref="NoteDto"/> with the new flag value.
    /// </summary>
    Task<NoteDto> ToggleFavoriteAsync(Guid id, CancellationToken cancellationToken = default);
}
