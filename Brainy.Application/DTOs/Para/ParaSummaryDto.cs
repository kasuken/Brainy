namespace Brainy.Application.DTOs.Para;

/// <summary>
/// Aggregated counts across all PARA categories for the current user.
/// Active counts exclude archived items; archived counts are per-entity-type.
/// </summary>
public record ParaSummaryDto(
    int ActiveProjectCount,
    int ArchivedProjectCount,
    int ActiveAreaCount,
    int ArchivedAreaCount,
    int ActiveResourceCount,
    int ArchivedResourceCount)
{
    /// <summary>Total archived items across all PARA categories.</summary>
    public int TotalArchiveCount => ArchivedProjectCount + ArchivedAreaCount + ArchivedResourceCount;
}
