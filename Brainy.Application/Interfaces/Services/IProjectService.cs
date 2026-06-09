using Brainy.Application.DTOs.Projects;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing projects.</summary>
public interface IProjectService
{
    Task<IReadOnlyList<ProjectDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<ProjectDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectDto> CreateAsync(CreateProjectDto dto, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
