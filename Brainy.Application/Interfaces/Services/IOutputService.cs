using Brainy.Application.DTOs.Outputs;

namespace Brainy.Application.Interfaces.Services;

/// <summary>Application service for managing outputs — AI-generated or authored content derived from notes and projects.</summary>
public interface IOutputService
{
    // ── Queries ──────────────────────────────────────────────────────────────

    /// <summary>Returns all non-archived outputs for the current user, ordered by most recently updated.</summary>
    Task<IReadOnlyList<OutputDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns all archived outputs for the current user, ordered by archived date descending.</summary>
    Task<IReadOnlyList<OutputDto>> GetAllArchivedAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns active outputs linked to a specific project.</summary>
    Task<IReadOnlyList<OutputDto>> GetByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    /// <summary>Returns active outputs linked to a specific goal.</summary>
    Task<IReadOnlyList<OutputDto>> GetByGoalAsync(Guid goalId, CancellationToken cancellationToken = default);

    /// <summary>Returns active outputs linked to a specific area.</summary>
    Task<IReadOnlyList<OutputDto>> GetByAreaAsync(Guid areaId, CancellationToken cancellationToken = default);

    /// <summary>Returns outputs that cite a specific note as source material.</summary>
    Task<IReadOnlyList<OutputDto>> GetBySourceNoteAsync(Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Returns a lightweight projection of a single output, or <c>null</c> if not found.</summary>
    Task<OutputDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns full detail (including content and source notes) for a single output, or <c>null</c> if not found.</summary>
    Task<OutputDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Searches active outputs by title, description, and content.</summary>
    Task<IReadOnlyList<OutputDto>> SearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Returns aggregated metrics about the current user's outputs.</summary>
    Task<OutputMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default);

    // ── Mutations ─────────────────────────────────────────────────────────────

    /// <summary>Creates a new output for the current user with status Draft.</summary>
    Task<OutputDto> CreateAsync(CreateOutputDto dto, CancellationToken cancellationToken = default);

    /// <summary>Updates the mutable fields of an existing output.</summary>
    Task<OutputDto> UpdateAsync(UpdateOutputDto dto, CancellationToken cancellationToken = default);

    /// <summary>Soft-archives the output. Sets IsArchived = true, ArchivedDate = UtcNow, Status = Archived.</summary>
    Task ArchiveAsync(Guid id, CancellationToken cancellationToken = default, string? archivedReason = null);

    /// <summary>Restores an archived output. Clears IsArchived and ArchivedDate; resets Status to Draft.</summary>
    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Marks the output as Published and records the PublishedDate.</summary>
    Task PublishAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes an output.</summary>
    Task DeleteAsync(Guid id, byte[]? rowVersion, CancellationToken cancellationToken = default);

    /// <summary>Adds a note to the output's source notes collection.</summary>
    Task AddSourceNoteAsync(Guid outputId, Guid noteId, CancellationToken cancellationToken = default);

    /// <summary>Removes a note from the output's source notes collection.</summary>
    Task RemoveSourceNoteAsync(Guid outputId, Guid noteId, CancellationToken cancellationToken = default);
}
