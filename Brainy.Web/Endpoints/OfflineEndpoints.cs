using Brainy.Application.DTOs.Offline;
using Brainy.Application.Interfaces.Services;

namespace Brainy.Web.Endpoints;

/// <summary>
/// Plain JSON HTTP endpoints for Brainy's Offline Lite feature (issue #302): a read-only
/// Today/current-focus/favorites snapshot for the static offline fallback page
/// (<c>wwwroot/offline.html</c>), and a sync endpoint for captures queued client-side
/// (IndexedDB, see <c>offlineCapture.js</c>) while the browser had no connectivity. Both are
/// deliberately plain, cookie-authenticated minimal APIs rather than Razor components — they
/// must work without a live Blazor Server circuit, since that is exactly what a fully offline
/// browser does not have.
/// </summary>
public static class OfflineEndpoints
{
    public static IEndpointRouteBuilder MapOfflineEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/offline/today-snapshot", async (
            IOfflineSnapshotService snapshotService,
            CancellationToken cancellationToken) =>
        {
            var snapshot = await snapshotService.GetSnapshotAsync(cancellationToken);
            return Results.Ok(snapshot);
        })
        .RequireAuthorization();

        endpoints.MapPost("/api/offline/captures/sync", async (
            OfflineCaptureSyncBatchDto? batch,
            IOfflineCaptureSyncService syncService,
            CancellationToken cancellationToken) =>
        {
            if (batch?.Items is null)
                return Results.BadRequest("Request body must include an \"items\" array.");

            try
            {
                var result = await syncService.SyncAsync(batch, cancellationToken);
                return Results.Ok(result);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .RequireAuthorization()
        // Called by plain fetch from offlineCapture.js (a JSON body, no Blazor/Razor form),
        // both from a live page and from the service worker's background-sync handler.
        .DisableAntiforgery();

        return endpoints;
    }
}
