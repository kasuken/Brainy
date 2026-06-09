namespace Brainy.Application.DTOs.Areas;

/// <summary>Full area detail including related-entity counts, used by the Area Detail page.</summary>
public record AreaDetailDto(
    Guid Id,
    string Name,
    string? Description,
    string? Purpose,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int ActiveProjectCount,
    int OpenTaskCount,
    int RecentNoteCount);
