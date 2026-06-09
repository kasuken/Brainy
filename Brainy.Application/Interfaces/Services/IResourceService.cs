using Brainy.Application.DTOs.Resources;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing resources (PARA: Resource).</summary>
public interface IResourceService
{
    Task<IReadOnlyList<ResourceDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<ResourceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ResourceDto> CreateAsync(CreateResourceDto dto, CancellationToken cancellationToken = default);
    Task<ResourceDto> UpdateAsync(UpdateResourceDto dto, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
