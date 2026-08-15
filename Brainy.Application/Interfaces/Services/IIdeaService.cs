using Brainy.Application.DTOs.Ideas;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing ideas — from initial capture through conversion to a project.</summary>
public interface IIdeaService
{
    // ── Queries ──────────────────────────────────────────────────────────────

    /// <summary>Returns all non-archived ideas for the current user, ordered by most recently updated.</summary>
    Task<IReadOnlyList<IdeaDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all archived ideas for the current user, ordered by archive date descending.</summary>
    Task<IReadOnlyList<IdeaDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns active ideas linked to a specific area.</summary>
    Task<IReadOnlyList<IdeaDto>> GetByAreaAsync(Guid areaId, CancellationToken cancellationToken = default);

    /// <summary>Returns a lightweight projection of a single idea, or <c>null</c> if not found.</summary>
    Task<IdeaDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns full detail (including Research, Competitors, Notes) for a single idea.</summary>
    Task<IdeaDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns aggregated review data surfacing ideas that need attention.</summary>
    Task<IdeaReviewDto> GetReviewDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns aggregated metrics about the current user's ideas.</summary>
    Task<IdeaMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);

    /// <summary>Searches ideas by title, description, research, competitors, and notes.</summary>
    Task<IReadOnlyList<IdeaDto>> SearchAsync(string query, CancellationToken cancellationToken = default);

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>Creates a new idea for the current user.</summary>
    Task<IdeaDto> CreateAsync(CreateIdeaDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates the mutable fields of an existing idea.</summary>
    Task<IdeaDto> UpdateAsync(UpdateIdeaDto dto, CancellationToken cancellationToken = default);

    /// <summary>Soft-archives the idea. Sets IsArchived = true, ArchivedAtUtc = UtcNow. Status is left unchanged.</summary>
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null);

    /// <summary>Restores an archived idea. Clears IsArchived and ArchivedAtUtc. Status is left unchanged.</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes an idea.</summary>
    Task DeleteAsync(Guid id, byte[]? rowVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits the idea to a new project. Requires all five commitment criteria to already be
    /// recorded on the idea: a specific user and problem, a suitability reason, one piece of real
    /// evidence, a small validation experiment, and a conscious replaced-commitment decision.
    /// Throws <see cref="InvalidOperationException"/> if any criterion is missing or the idea is
    /// already committed. Creates a project from the idea's title, description, and area; sets
    /// the idea's status to Committed and clears its bulky content (Description, Research,
    /// Competitors, Notes), leaving only a link to the project and the decision record
    /// (the five criteria fields) in the Ideas database.
    /// </summary>
    Task<IdeaDto> CommitToProjectAsync(Guid id, CancellationToken cancellationToken = default);
}
