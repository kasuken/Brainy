using System.Security.Claims;
using Brainy.Application.Interfaces.Services;
using Brainy.Web.Configuration;

namespace Brainy.Web.Endpoints;

/// <summary>
/// Serves the aggregate activation/reuse/retrieval metrics CSV export for the internal
/// analytics dashboard (issue #324), for the #293 discovery work. Gated by the same
/// email allowlist as <c>AnalyticsDashboardPage</c> — see <see cref="AnalyticsAccessOptions"/>
/// — since this is the same internal-admin surface, not a per-user download.
/// </summary>
public static class AnalyticsExportEndpoints
{
    public static IEndpointRouteBuilder MapAnalyticsExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/analytics/export.csv", async (
            HttpContext http,
            IAnalyticsService analyticsService,
            AnalyticsAccessOptions accessOptions,
            CancellationToken cancellationToken) =>
        {
            var email = http.User.FindFirstValue(ClaimTypes.Email);
            var authorized = !string.IsNullOrWhiteSpace(email) &&
                accessOptions.AdminEmails.Contains(email, StringComparer.OrdinalIgnoreCase);

            // 404 rather than 403/401: an unauthorized caller should not learn this
            // endpoint even exists, matching AnalyticsDashboardPage's own redirect.
            if (!authorized)
                return Results.NotFound();

            var file = await analyticsService.ExportMetricsCsvAsync(cancellationToken).ConfigureAwait(false);
            return Results.File(file.Content, file.ContentType, file.FileName);
        })
        .RequireAuthorization();

        return endpoints;
    }
}
