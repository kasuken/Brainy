using Brainy.Application.DTOs.Projects;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing projects.</summary>
public interface IProjectService
{
    /// <summary>Returns only <see cref="Domain.Enums.ProjectStatus.Active"/> projects — used by Today and active work views.</summary>
    Task<IReadOnlyList<ProjectDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all non-archived projects — used by Project List and project pickers.</summary>
    Task<IReadOnlyList<ProjectDto>> GetAllNonArchivedAsync(CancellationToken cancellationToken = default);

    Task<ProjectDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectDto> CreateAsync(CreateProjectDto dto, CancellationToken cancellationToken = default);
    Task<ProjectDto> UpdateAsync(UpdateProjectDto dto, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
