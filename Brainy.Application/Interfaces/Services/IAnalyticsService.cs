using Brainy.Application.DTOs.Analytics;

namespace Brainy.Application.Interfaces.Services;

/// <summary>
/// Records and queries privacy-safe, consent-gated internal product-analytics events.
/// Event names must come from <c>Brainy.Application.Analytics.AnalyticsEvents</c>; never
/// pass caller-supplied free text as <paramref name="eventName"></paramref> equivalents.
/// </summary>
public interface IAnalyticsService
{
    /// <summary>
    /// Records one occurrence of <paramref name="eventName"/> for <paramref name="userId"/>.
    /// A no-op when the user has opted out of analytics tracking.
    /// </summary>
    Task TrackAsync(
        string userId,
        string eventName,
        IReadOnlyDictionary<string, object?>? properties = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="eventName"/> for <paramref name="userId"/> only if it has
    /// never fired for that user before. Use for "first capture", "first task", etc.
    /// A no-op when the user has opted out of analytics tracking.
    /// </summary>
    Task TrackOnceAsync(
        string userId,
        string eventName,
        IReadOnlyDictionary<string, object?>? properties = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether <paramref name="eventName"/> has ever been recorded for the user.</summary>
    Task<bool> HasEventOccurredAsync(
        string userId,
        string eventName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns whether the current authenticated user is opted in to analytics tracking.
    /// Defaults to <c>true</c> (opted in) until the user changes it, matching the disclosed
    /// default in Account &amp; data.
    /// </summary>
    Task<bool> IsCurrentUserOptedInAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets the current authenticated user's analytics-tracking consent.</summary>
    Task SetCurrentUserOptedInAsync(bool optedIn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Aggregate activation funnel across all users. Intentionally not user-scoped: this
    /// backs the internal admin analytics dashboard, not a per-user view.
    /// </summary>
    Task<ActivationFunnelDto> GetActivationFunnelAsync(CancellationToken cancellationToken = default);

    /// <summary>Aggregate search retrieval-quality metrics across all users.</summary>
    Task<SearchQualityDto> GetSearchQualityAsync(CancellationToken cancellationToken = default);

    /// <summary>Aggregate weekly-review engagement metrics across all users.</summary>
    Task<WeeklyReviewSummaryDto> GetWeeklyReviewSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Aggregate AI usage metrics across all users.</summary>
    Task<AiUsageSummaryDto> GetAiUsageSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Approximate D7/D30 retention across all users.</summary>
    Task<RetentionSummaryDto> GetRetentionSummaryAsync(CancellationToken cancellationToken = default);
}
