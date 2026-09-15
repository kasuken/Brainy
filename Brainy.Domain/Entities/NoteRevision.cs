using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// An immutable snapshot of a <see cref="Note"/>'s title and content, captured whenever
/// the note's user-visible text changes. Revisions form an append-only history: editing
/// a note never overwrites earlier wording, and restoring an old revision creates a new
/// one rather than reverting destructively (see <see cref="RestoredFromRevisionId"/>).
/// </summary>
public class NoteRevision : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    public Guid NoteId { get; set; }

    public Note Note { get; set; } = null!;

    /// <summary>Note title as it existed at the time of this revision.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Note content as it existed at the time of this revision.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Why this revision was captured.</summary>
    public NoteRevisionReason Reason { get; set; }

    /// <summary>The AI model used, when <see cref="Reason"/> is <see cref="NoteRevisionReason.AiGeneration"/> (provenance).</summary>
    public string? Model { get; set; }

    /// <summary>The versioned prompt used, when <see cref="Reason"/> is <see cref="NoteRevisionReason.AiGeneration"/> (provenance).</summary>
    public string? PromptVersion { get; set; }

    /// <summary>
    /// When this revision was itself created by restoring an earlier one (<see cref="Reason"/>
    /// is <see cref="NoteRevisionReason.Restore"/>), the id of the revision that was restored.
    /// Null otherwise. Set null if that earlier revision is later purged by retention.
    /// </summary>
    public Guid? RestoredFromRevisionId { get; set; }
}
