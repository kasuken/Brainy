namespace Brainy.Application.DTOs.DataExport;

/// <summary>
/// A ready-to-download CSV export of the aggregate activation/reuse/retrieval metrics from
/// the internal analytics dashboard (issue #324), for the #293 Starter Mode / pricing
/// discovery work. Contains only cross-user aggregate numbers &mdash; the same figures already
/// rendered on <c>AnalyticsDashboardPage</c> &mdash; never per-user rows or content.
/// </summary>
public sealed record AnalyticsMetricsExportDto(
    string FileName,
    string ContentType,
    byte[] Content);
