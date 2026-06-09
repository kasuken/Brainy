using Brainy.Application.DTOs.Projects;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Computes a priority score for each active project and returns them ranked highest-first.
/// Used on the Today screen to surface the most important projects.
/// </summary>
public interface IProjectPrioritizationService
{
    /// <summary>
    /// Returns active (non-archived) projects for the current user, ordered by calculated priority score.
    /// </summary>
    Task<IReadOnlyList<ProjectSummaryDto>> GetPrioritizedProjectsAsync(int maxCount = 5, CancellationToken cancellationToken = default);
}
