using Brainy.Application.DTOs.Projects;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing projects.</summary>
public interface IProjectService
{
    /// <summary>Returns only <see cref="Domain.Enums.ProjectStatus.Active"/> projects — used by Today and active work views.</summary>
    Task<IReadOnlyList<ProjectDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all non-archived projects — used by Project List and project pickers.</summary>
    Task<IReadOnlyList<ProjectDto>> GetAllNonArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all archived projects — used by the Archives view.</summary>
    Task<IReadOnlyList<ProjectDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all non-archived projects enriched with task statistics — used by the Project List page.</summary>
    Task<IReadOnlyList<ProjectSummaryDto>> GetProjectSummariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-archived, non-completed projects whose due date is today.
    /// Used by the Today screen for deadline monitoring.
    /// </summary>
    Task<IReadOnlyList<ProjectSummaryDto>> GetDueTodayProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-archived, non-completed projects due within the next 1–6 days (excluding today).
    /// Used by the Today screen for deadline monitoring.
    /// </summary>
    Task<IReadOnlyList<ProjectSummaryDto>> GetDueThisWeekProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns non-archived, non-completed projects whose due date is in the past.
    /// Ordered by due date ascending (most overdue first).
    /// </summary>
    Task<IReadOnlyList<ProjectSummaryDto>> GetOverdueProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the full project workspace — tasks, notes, and resource notes included.</summary>
    Task<ProjectDetailDto?> GetProjectDetailAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a lightweight progress snapshot for a single project.
    /// Designed for polling — only queries the Task table, no navigation properties loaded.
    /// Returns null if the project does not belong to the current user.
    /// </summary>
    Task<ProjectProgressDto?> GetProjectProgressAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ProjectDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectDto> CreateAsync(CreateProjectDto dto, CancellationToken cancellationToken = default);
    Task<ProjectDto> UpdateAsync(UpdateProjectDto dto, CancellationToken cancellationToken = default);
    /// <summary>
    /// Marks a project as <see cref="Domain.Enums.ProjectStatus.Completed"/>, sets
    /// <c>CompletedDate</c>, and handles remaining open tasks according to
    /// <paramref name="taskAction"/>. Notes and history are preserved.
    /// </summary>
    Task<ProjectDto> CompleteAsync(Guid id, TaskCompletionAction taskAction, CancellationToken cancellationToken = default);

    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores an archived project back to <see cref="Domain.Enums.ProjectStatus.NotStarted"/>.
    /// </summary>
    Task<ProjectDto> RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a project. Throws <see cref="InvalidOperationException"/>
    /// if the project still has tasks, notes, or outputs.
    /// </summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
