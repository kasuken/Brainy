using Brainy.Application.DTOs.Capture;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Extension seam originally reserved for issue #302 ("Offline Lite"). #302's real offline
/// persistence ended up entirely client-side instead (IndexedDB queue + a plain
/// <c>POST /api/offline/captures/sync</c> endpoint, see <c>offlineCapture.js</c> and
/// <see cref="IOfflineCaptureSyncService"/>) — a fully offline browser has no live Blazor
/// Server circuit to call *any* C# code from, so this seam could never be the primary
/// mechanism. <c>/capture/share</c> now calls the client-side queue directly via
/// <c>IJSRuntime</c> for the "circuit still alive but browser reports offline" case too,
/// which covers that narrower case just as well without a second, server-side pending-capture
/// table. This interface stays registered (see <see cref="NullOfflineCaptureQueue"/>) as a
/// no-op in case a future server-side use for it emerges; every implementation must keep
/// reporting "not queued" so nothing can silently claim a false success through it.
/// </summary>
public interface IOfflineCaptureQueue
{
    /// <summary>
    /// Attempts to persist <paramref name="dto"/> for later sync while the user is offline.
    /// Returns <c>false</c> when no queue is available (the current, pre-#302 behaviour) —
    /// callers must not represent the capture as saved in that case.
    /// </summary>
    Task<bool> TryQueueAsync(ShareCaptureDto dto, CancellationToken cancellationToken = default);
}
