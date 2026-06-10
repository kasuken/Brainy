namespace Brainy.Application.DTOs.Ideas;

/// <summary>Aggregated metrics about the current user's ideas, broken down by status and area.</summary>
public record IdeaMetricsDto(
    int TotalIdeas,
    int ActiveIdeas,
    int ArchivedIdeas,
    int ConvertedIdeas,
    int RejectedIdeas,
    IReadOnlyList<IdeasByAreaDto> ByArea);

/// <summary>Idea count for a single area (or ideas with no area when <see cref="AreaId"/> is null).</summary>
public record IdeasByAreaDto(Guid? AreaId, string AreaName, int Count);
