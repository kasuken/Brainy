using Brainy.Application.DTOs.Resources;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing resources (PARA: Resource).</summary>
public interface IResourceService
{
    Task<IReadOnlyList<ResourceDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResourceDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ResourceDto>> SearchAsync(string? searchText, string? topic, CancellationToken cancellationToken = default);
    Task<ResourceDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ResourceDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ResourceDto> CreateAsync(CreateResourceDto dto, CancellationToken cancellationToken = default);
    Task<ResourceDto> UpdateAsync(UpdateResourceDto dto, CancellationToken cancellationToken = default);
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null);
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, byte[]? rowVersion, CancellationToken cancellationToken = default);

    // ── Note linking ──────────────────────────────────────────────────────────
    /// <summary>Associates a note with this resource by setting Note.ResourceId.</summary>
    Task LinkNoteAsync(Guid resourceId, Guid noteId, CancellationToken cancellationToken = default);
    /// <summary>Removes a note's resource association by clearing Note.ResourceId.</summary>
    Task UnlinkNoteAsync(Guid noteId, CancellationToken cancellationToken = default);
}
