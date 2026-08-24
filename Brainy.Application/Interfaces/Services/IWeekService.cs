using Brainy.Application.DTOs.Week;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Application service for deliberate Monday-Sunday weekly planning.
/// </summary>
public interface IWeekService
{
    /// <summary>
    /// Loads the authenticated user's current-week planning overview.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the query.</param>
    /// <returns>The current-week overview.</returns>
    Task<WeekOverviewDto> GetCurrentWeekOverviewAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a bounded set of selectable top-level tasks for one project.
    /// </summary>
    /// <param name="projectId">The project to inspect.</param>
    /// <param name="searchTerm">Optional server-side search term.</param>
    /// <param name="maxResults">Maximum number of tasks to return.</param>
    /// <param name="cancellationToken">Token used to cancel the query.</param>
    /// <returns>The project-specific picker payload.</returns>
    Task<WeekTaskPickerDto> GetSelectableTasksAsync(
        Guid projectId,
        string? searchTerm = null,
        int maxResults = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the specified task to the authenticated user's current week without
    /// mutating task status, due date, priority, or current focus.
    /// </summary>
    /// <param name="taskId">The task to add.</param>
    /// <param name="cancellationToken">Token used to cancel the mutation.</param>
    Task AddTaskToCurrentWeekAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the specified task from the authenticated user's current week.
    /// </summary>
    /// <param name="taskId">The task to remove.</param>
    /// <param name="cancellationToken">Token used to cancel the mutation.</param>
    Task RemoveTaskFromCurrentWeekAsync(Guid taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns unfinished selections from the immediately previous week.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the query.</param>
    /// <returns>The previous-week carry-forward candidates.</returns>
    Task<IReadOnlyList<WeekCarryForwardCandidateDto>> GetCarryForwardCandidatesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly carries the selected previous-week tasks into the current week.
    /// </summary>
    /// <param name="taskIds">The selected task ids.</param>
    /// <param name="cancellationToken">Token used to cancel the mutation.</param>
    Task CarryForwardTasksAsync(IReadOnlyList<Guid> taskIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a project's status using the Week page's constrained workflow.
    /// </summary>
    /// <param name="dto">The status change payload.</param>
    /// <param name="cancellationToken">Token used to cancel the mutation.</param>
    /// <returns>The refreshed project overview.</returns>
    Task<WeekProjectOverviewDto> UpdateProjectStatusAsync(
        WeekProjectStatusUpdateDto dto,
        CancellationToken cancellationToken = default);
}
