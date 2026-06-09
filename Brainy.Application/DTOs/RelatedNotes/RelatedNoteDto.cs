using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.RelatedNotes;

/// <summary>A note suggested as related, with a similarity score in [0, 1].</summary>
public record RelatedNoteDto(
    Guid Id,
    string Title,
    ParaCategory ParaCategory,
    NoteStatus Status,
    /// <summary>Jaccard similarity score in [0, 1]. Higher is more similar.</summary>
    double SimilarityScore,
    DateTime UpdatedAtUtc);
