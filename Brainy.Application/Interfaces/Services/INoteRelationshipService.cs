using Brainy.Application.DTOs.NoteRelationships;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing relationships between notes.</summary>
public interface INoteRelationshipService
{
    /// <summary>
    /// Returns all relationships (both directions) that involve <paramref name="noteId"/>,
    /// including the linked note's title for display.
    /// </summary>
    Task<IReadOnlyList<NoteRelationshipDto>> GetForNoteAsync(
        Guid noteId,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a new relationship between two notes owned by the current user.</summary>
    /// <exception cref="KeyNotFoundException">Either note was not found or not owned by the user.</exception>
    /// <exception cref="InvalidOperationException">The relationship already exists or notes are the same.</exception>
    Task<NoteRelationshipDto> CreateAsync(
        CreateNoteRelationshipDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a relationship. Only the owner of the source note may delete it.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
