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
}
