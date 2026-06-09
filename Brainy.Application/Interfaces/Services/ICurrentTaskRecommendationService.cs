using Brainy.Application.DTOs.Tasks;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Recommends the single best task to work on next when the user has no Current Task set.
/// </summary>
public interface ICurrentTaskRecommendationService
{
    /// <summary>
    /// Returns the best task to work on next when the user has no Current Task set.
    /// Returns <see langword="null"/> if no eligible tasks exist.
    /// </summary>
    Task<TodayTaskItemDto?> GetRecommendationAsync(CancellationToken cancellationToken = default);
}
