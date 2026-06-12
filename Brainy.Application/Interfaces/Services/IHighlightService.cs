using Brainy.Application.DTOs.Highlights;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing highlights on notes.</summary>
public interface IHighlightService
{
    /// <summary>Returns all highlights for the given note, scoped to the current user.</summary>
    Task<IReadOnlyList<HighlightDto>> GetByNoteAsync(Guid noteId, CancellationToken ct = default);

    /// <summary>Creates a new highlight on a note owned by the current user.</summary>
    /// <exception cref="KeyNotFoundException">The note was not found or not owned by the user.</exception>
    /// <exception cref="ArgumentException">The highlight text is empty.</exception>
    Task<HighlightDto> CreateAsync(CreateHighlightDto dto, CancellationToken ct = default);

    /// <summary>Updates the annotation and layer of an existing highlight.</summary>
    /// <exception cref="KeyNotFoundException">The highlight was not found or not owned by the user.</exception>
    Task UpdateAsync(Guid id, UpdateHighlightDto dto, CancellationToken ct = default);

    /// <summary>Deletes a highlight. Only the note owner may delete it.</summary>
    /// <exception cref="KeyNotFoundException">The highlight was not found or not owned by the user.</exception>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
