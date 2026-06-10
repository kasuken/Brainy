namespace Brainy.Domain.Enums;

/// <summary>
/// Lifecycle state of an <see cref="Entities.Idea"/>, tracking its journey from initial
/// capture through research, validation, and eventual disposition.
/// </summary>
public enum IdeaStatus
{
    /// <summary>Newly captured; not yet evaluated.</summary>
    Captured = 0,

    /// <summary>Actively being researched.</summary>
    Researching = 1,

    /// <summary>Being validated with experiments, prototypes, or user feedback.</summary>
    Validating = 2,

    /// <summary>Accepted and scheduled for future implementation.</summary>
    Planned = 3,

    /// <summary>Evaluated and deliberately not pursued.</summary>
    Rejected = 4,

    /// <summary>Promoted into a concrete project.</summary>
    ConvertedToProject = 5,

    /// <summary>Soft-archived; retained for historical reference.</summary>
    Archived = 6
}
