using Brainy.Application.DTOs.RelatedNotes;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Suggests notes that are textually similar to a given note using keyword overlap
/// (Jaccard similarity on tokenised title + content).
/// </summary>
public interface IRelatedNotesService
{
    /// <summary>
    /// Returns up to <paramref name="topN"/> notes similar to the stored note
    /// identified by <paramref name="noteId"/>, ordered by similarity descending.
    /// </summary>
    Task<IReadOnlyList<RelatedNoteDto>> GetRelatedAsync(
        Guid noteId,
        int topN = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes similarity against arbitrary in-progress text so the sidebar
    /// can refresh while the user is editing without a save round-trip.
    /// </summary>
    Task<IReadOnlyList<RelatedNoteDto>> GetRelatedByContentAsync(
        Guid excludeNoteId,
        string title,
        string content,
        int topN = 5,
        CancellationToken cancellationToken = default);
}
