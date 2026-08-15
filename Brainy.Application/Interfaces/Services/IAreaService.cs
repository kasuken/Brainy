using Brainy.Application.DTOs.Areas;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing areas (PARA: Area).</summary>
public interface IAreaService
{
    // ── Queries ──────────────────────────────────────────────────────────────
    Task<IReadOnlyList<AreaDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AreaDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default);
    Task<AreaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    /// <summary>Returns full detail including related-entity counts.</summary>
    Task<AreaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    // ── Mutations ─────────────────────────────────────────────────────────────
    Task<AreaDto> CreateAsync(CreateAreaDto dto, CancellationToken cancellationToken = default);
    Task<AreaDto> UpdateAsync(UpdateAreaDto dto, CancellationToken cancellationToken = default);
    /// <summary>Soft-archives the area. Sets IsArchived = true and ArchivedAtUtc = UtcNow.</summary>
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null);
    /// <summary>Restores an archived area. Clears IsArchived and ArchivedAtUtc.</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // ── Project linking (Issue #65) ───────────────────────────────────────────
    /// <summary>Associates a project with this area by setting Project.AreaId.</summary>
    Task LinkProjectAsync(Guid areaId, Guid projectId, CancellationToken cancellationToken = default);
    /// <summary>Removes a project's area association by clearing Project.AreaId.</summary>
    Task UnlinkProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    // ── Note linking (Issue #66) ──────────────────────────────────────────────
    /// <summary>Associates a note with this area by setting Note.AreaId.</summary>
    Task LinkNoteAsync(Guid areaId, Guid noteId, CancellationToken cancellationToken = default);
    /// <summary>Removes a note's area association by clearing Note.AreaId.</summary>
    Task UnlinkNoteAsync(Guid noteId, CancellationToken cancellationToken = default);
}
