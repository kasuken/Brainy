using Brainy.Application.DTOs.Areas;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing areas (PARA: Area).</summary>
public interface IAreaService
{
    Task<IReadOnlyList<AreaDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<AreaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<AreaDto> CreateAsync(CreateAreaDto dto, CancellationToken cancellationToken = default);
    Task<AreaDto> UpdateAsync(UpdateAreaDto dto, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
