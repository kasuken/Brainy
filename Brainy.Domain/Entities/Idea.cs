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
}
