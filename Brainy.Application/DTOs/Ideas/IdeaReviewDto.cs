namespace Brainy.Application.DTOs.Ideas;

/// <summary>
/// Aggregated review data surfacing ideas that need attention:
/// stale ideas, recently active ones, and high-priority ideas without recent activity.
/// </summary>
public record IdeaReviewDto(
    IReadOnlyList<IdeaDto> StaleIdeas,
    IReadOnlyList<IdeaDto> RecentlyUpdatedIdeas,
    IReadOnlyList<IdeaDto> HighPriorityWithoutRecentActivity);
