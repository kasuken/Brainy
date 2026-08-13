using Brainy.Domain.Enums;

namespace Brainy.Application.DTOs.Pulse;

/// <summary>
/// A full Pulse report for a date range: aggregate counts plus the detailed activity log,
/// assembled by <see cref="Interfaces.Services.IPulseService.GetReportAsync"/>.
/// </summary>
public record PulseReportDto(
    PulsePeriod Period,
    DateTime PeriodStartUtc,
    /// <summary>Exclusive upper bound of the reporting window.</summary>
    DateTime PeriodEndUtc,
    /// <summary>Time-zone id used for all calendar and time-of-day analytics.</summary>
    string TimeZoneId,
    PulseSummaryDto Summary,
    /// <summary>Time-based trends and behavioral insights for the reporting window.</summary>
    PulseAnalyticsDto Analytics,
    /// <summary>Ordered most-recent-first.</summary>
    IReadOnlyList<PulseActivityLogEntryDto> Log);
