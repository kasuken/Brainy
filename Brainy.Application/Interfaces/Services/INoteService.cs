using Brainy.Application.DTOs.Notes;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing notes.</summary>
public interface INoteService
{
    Task<IReadOnlyList<NoteDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<NoteDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<NoteDto> CreateAsync(CreateNoteDto dto, CancellationToken cancellationToken = default);
    Task<NoteDto> UpdateAsync(UpdateNoteDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
