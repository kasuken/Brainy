using Brainy.Application.DTOs.Offline;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Server-side counterpart of the Offline Lite (issue #302) client-side capture queue.
/// Applies a batch of queued captures posted from <c>offlineCapture.js</c>
/// (<c>POST /api/offline/captures/sync</c>), reusing <see cref="Brainy.Application.Interfaces.Services.IShareCaptureService"/> for
/// the actual Note creation so offline-synced captures follow exactly the same invariants
/// (and fire the same <c>capture.created</c> analytics event) as any other capture route.
/// </summary>
public interface IOfflineCaptureSyncService
{
    /// <summary>
    /// Maximum items accepted in a single batch. Bounds request size and per-request work;
    /// a device with more queued items than this simply syncs in more than one request.
    /// </summary>
    const int MaxBatchSize = 25;

    /// <summary>
    /// Applies every item in <paramref name="batch"/>, in order. Each item is independent:
    /// one item failing validation does not fail the others. Throws
    /// <see cref="ArgumentException"/> only for a batch-level problem (a null batch, or more
    /// than <see cref="MaxBatchSize"/> items).
    /// </summary>
    Task<OfflineCaptureSyncResultDto> SyncAsync(OfflineCaptureSyncBatchDto batch, CancellationToken cancellationToken = default);
}
