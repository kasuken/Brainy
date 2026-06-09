using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A directed relationship between two notes (e.g. related, references, duplicate).
/// </summary>
public class NoteRelationship : BaseEntity
{
    public Guid SourceNoteId { get; set; }

    public Note SourceNote { get; set; } = null!;

    public Guid TargetNoteId { get; set; }

    public Note TargetNote { get; set; } = null!;

    public RelationshipType Type { get; set; }

    /// <summary>Optional note explaining the relationship.</summary>
    public string? Annotation { get; set; }

    /// <summary>True when the link was suggested by AI rather than the user.</summary>
    public bool IsAiGenerated { get; set; }
}
