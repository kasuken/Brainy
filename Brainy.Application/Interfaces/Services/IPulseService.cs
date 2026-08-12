using Brainy.Application.DTOs.Pulse;
using Brainy.Domain.Enums;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Builds the Pulse activity report: a summary of what the user did across the app
/// (captures, processing, task/project completions, distillation, outputs, etc.)
/// over a selected period, plus a chronological activity log.
/// </summary>
public interface IPulseService
{
    /// <summary>
    /// Returns the Pulse report for <paramref name="period"/>. When <paramref name="period"/>
    /// is <see cref="PulsePeriod.Custom"/>, both <paramref name="customStartUtc"/> and
    /// <paramref name="customEndUtc"/> must be supplied (inclusive calendar days).
    /// </summary>
    Task<PulseReportDto> GetReportAsync(
        PulsePeriod period,
        DateTime? customStartUtc = null,
        DateTime? customEndUtc = null,
        CancellationToken cancellationToken = default);
}
