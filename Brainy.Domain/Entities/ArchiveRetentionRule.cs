using Brainy.Domain.Common;

namespace Brainy.Domain.Entities;

/// <summary>
/// Per-user, per-entity-type retention policy for archived items.
/// When RetentionDays is set, archived items older than that threshold are eligible for permanent deletion.
/// </summary>
public class ArchiveRetentionRule : BaseEntity, IUserOwnedEntity
{
    public string UserId { get; set; } = string.Empty;

    /// <summary>Entity type name: "Project", "Area", "Resource", or "Note".</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Number of days after archiving before an item is eligible for purge. Null = keep forever.</summary>
    public int? RetentionDays { get; set; }
}
