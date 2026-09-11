using Brainy.Application.DTOs.Offline;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Builds the small, read-only Today/current-focus/favorites snapshot served by
/// <c>GET /api/offline/today-snapshot</c> for the Offline Lite (issue #302) offline fallback
/// page. Every field is scoped to the current authenticated user, same as the live Today
/// widgets it summarizes — this is a static export of that data, not a live view.
/// </summary>
public interface IOfflineSnapshotService
{
    /// <summary>Maximum number of favorite/recent notes included in the snapshot.</summary>
    const int MaxNotes = 8;

    Task<OfflineSnapshotDto> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
