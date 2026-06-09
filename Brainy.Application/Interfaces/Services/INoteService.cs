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
    /// Moves multiple notes to a new PARA category in a single database operation.
    /// Only notes owned by the current user are affected.
    /// </summary>
    Task<int> BulkMoveCategoryAsync(IEnumerable<Guid> ids, ParaCategory category, CancellationToken cancellationToken = default);
}
