using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.NoteRelationships;

/// <summary>
/// A single relationship involving a note. <see cref="IsOutgoing"/> is true when the
/// queried note is the source; false when it is the target.
/// </summary>
public record NoteRelationshipDto(
    Guid Id,
    /// <summary>The other note in the relationship.</summary>
    Guid LinkedNoteId,
    string LinkedNoteTitle,
    RelationshipType Type,
    bool IsOutgoing,
    string? Annotation,
    bool IsAiGenerated);
