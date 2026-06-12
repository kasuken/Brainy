using Brainy.Application.DTOs.Summaries;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing summary layers on notes.</summary>
public interface ISummaryService
{
    /// <summary>Returns all summaries for the given note, scoped to the current user.</summary>
    Task<IReadOnlyList<SummaryDto>> GetByNoteAsync(Guid noteId, CancellationToken ct = default);

    /// <summary>Creates a new summary on a note owned by the current user.</summary>
    /// <exception cref="KeyNotFoundException">The note was not found or not owned by the user.</exception>
    /// <exception cref="ArgumentException">The summary content is empty.</exception>
    Task<SummaryDto> CreateAsync(CreateSummaryDto dto, CancellationToken ct = default);

    /// <summary>Deletes a summary. Only the note owner may delete it.</summary>
    /// <exception cref="KeyNotFoundException">The summary was not found or not owned by the user.</exception>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
