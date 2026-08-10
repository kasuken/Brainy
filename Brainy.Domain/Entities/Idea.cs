using Brainy.Domain.Common;
using Brainy.Domain.Enums;

namespace Brainy.Domain.Entities;

/// <summary>
/// A captured idea that may be researched, validated, and eventually converted into a project.
/// Ideas are scoped to a user and optionally linked to an area.
/// </summary>
public class Idea : BaseEntity, IUserOwnedEntity
{
    /// <summary>Identity key of the owning user.</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Short, descriptive title of the idea.</summary>
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Optional link to the area this idea relates to.</summary>
    public Guid? AreaId { get; set; }

    public Area? Area { get; set; }

    public IdeaPriority Priority { get; set; } = IdeaPriority.Medium;

    public IdeaStatus Status { get; set; } = IdeaStatus.Captured;

    /// <summary>When true, the idea is hidden from active views.</summary>
    public bool IsArchived { get; set; }

    /// <summary>Populated when the idea is archived. Null if active.</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    /// <summary>Free-form research notes accumulated during evaluation.</summary>
    public string? Research { get; set; }

    /// <summary>Notes on competing solutions, products, or approaches.</summary>
    public string? Competitors { get; set; }

    /// <summary>General notes that do not fit in Research or Competitors.</summary>
    public string? Notes { get; set; }

    // ── Commitment criteria ─────────────────────────────────────────────────
    // All five must be filled in before the idea can move to IdeaStatus.Committed.

    /// <summary>The specific user and the problem they have — required to commit.</summary>
    public string? TargetUserAndProblem { get; set; }

    /// <summary>Why the owner is suited to build or write this — required to commit.</summary>
    public string? SuitabilityReason { get; set; }

    /// <summary>One piece of real evidence supporting the idea — required to commit.</summary>
    public string? Evidence { get; set; }

    /// <summary>A small validation experiment to run — required to commit.</summary>
    public string? ValidationExperiment { get; set; }

    /// <summary>The existing commitment this idea will consciously replace — required to commit.</summary>
    public string? ReplacedCommitment { get; set; }

    /// <summary>Id of the project created when this idea was committed. Null until committed.</summary>
    public Guid? CommittedProjectId { get; set; }

    /// <summary>When the idea was committed and its project was created. Null until committed.</summary>
    public DateTime? CommittedAtUtc { get; set; }
}
