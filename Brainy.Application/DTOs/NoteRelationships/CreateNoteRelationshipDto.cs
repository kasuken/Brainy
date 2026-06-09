using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.NoteRelationships;

/// <summary>Payload for creating a new relationship between two notes.</summary>
public record CreateNoteRelationshipDto(
    Guid SourceNoteId,
    Guid TargetNoteId,
    RelationshipType Type,
    string? Annotation = null);
