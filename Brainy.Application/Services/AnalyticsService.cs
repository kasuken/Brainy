using System.Text.Json;
using Brainy.Application.Analytics;
using Brainy.Application.DTOs.Analytics;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Records privacy-safe internal product-analytics events and answers the aggregate
/// activation/retrieval/retention questions from issue #295. Every write is consent-gated
/// per user via <see cref="UserDashboardPreference.AnalyticsEnabled"/>.
/// </summary>
/// <remarks>
/// The <c>GetXxxSummaryAsync</c> query methods intentionally aggregate across ALL users:
/// they back the internal admin analytics dashboard (config-gated, see
/// <c>Brainy.Web.Analytics.AnalyticsAccessOptions</c>), not a per-user report. Every write
/// path (<see cref="TrackAsync"/>, <see cref="TrackOnceAsync"/>) remains scoped to the
/// single <c>userId</c> the caller supplies.
/// </remarks>
internal sealed class AnalyticsService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IAnalyticsService
{
    private const int MaxPropertiesJsonLength = 2000;

    private static readonly HashSet<string> KnownEventNames =
        AnalyticsEvents.All.Select(e => e.EventName).ToHashSet(StringComparer.Ordinal);

    public async Task TrackAsync(
        string userId,
        string eventName,
        IReadOnlyDictionary<string, object?>? properties = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ValidateEventName(eventName);

        if (!await IsAnalyticsEnabledAsync(userId, cancellationToken).ConfigureAwait(false))
            return;

        await WriteEventAsync(userId, eventName, properties, cancellationToken).ConfigureAwait(false);
    }

    public async Task TrackOnceAsync(
        string userId,
        string eventName,
        IReadOnlyDictionary<string, object?>? properties = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ValidateEventName(eventName);

        if (!await IsAnalyticsEnabledAsync(userId, cancellationToken).ConfigureAwait(false))
            return;

        var alreadyFired = await context.ProductEvents.AsNoTracking()
            .AnyAsync(e => e.UserId == userId && e.EventName == eventName, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyFired)
            return;

        await WriteEventAsync(userId, eventName, properties, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> HasEventOccurredAsync(
        string userId,
        string eventName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ValidateEventName(eventName);

        return context.ProductEvents.AsNoTracking()
            .AnyAsync(e => e.UserId == userId && e.EventName == eventName, cancellationToken);
    }

    public async Task<ActivationFunnelDto> GetActivationFunnelAsync(CancellationToken cancellationToken = default)
    {
        var totalUsers = await CountDistinctUsersAsync(cancellationToken).ConfigureAwait(false);
        var firstCapture = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstCaptureCreated, cancellationToken).ConfigureAwait(false);
        var firstProcessed = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstInboxItemProcessed, cancellationToken).ConfigureAwait(false);
        var firstTask = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstTaskCreated, cancellationToken).ConfigureAwait(false);
        var firstFocus = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstCurrentFocusSelected, cancellationToken).ConfigureAwait(false);

        return new ActivationFunnelDto(totalUsers, firstCapture, firstProcessed, firstTask, firstFocus);
    }

    public async Task<SearchQualityDto> GetSearchQualityAsync(CancellationToken cancellationToken = default)
    {
        var submitted = await CountEventsAsync(AnalyticsEvents.SearchSubmitted, cancellationToken).ConfigureAwait(false);
        var zeroResult = await CountEventsAsync(AnalyticsEvents.SearchZeroResult, cancellationToken).ConfigureAwait(false);
        var opened = await CountEventsAsync(AnalyticsEvents.SearchResultOpened, cancellationToken).ConfigureAwait(false);
        var followOn = await CountEventsAsync(AnalyticsEvents.SearchFollowOnAction, cancellationToken).ConfigureAwait(false);

        return new SearchQualityDto(submitted, zeroResult, opened, followOn);
    }

    public async Task<WeeklyReviewSummaryDto> GetWeeklyReviewSummaryAsync(CancellationToken cancellationToken = default)
    {
        var views = await CountEventsAsync(AnalyticsEvents.WeeklyReviewViewed, cancellationToken).ConfigureAwait(false);
        var resurfaced = await CountEventsAsync(AnalyticsEvents.ResurfacedItemActioned, cancellationToken).ConfigureAwait(false);

        return new WeeklyReviewSummaryDto(views, resurfaced);
    }

    public async Task<AiUsageSummaryDto> GetAiUsageSummaryAsync(CancellationToken cancellationToken = default)
    {
        var submitted = await CountEventsAsync(AnalyticsEvents.AiRequestSubmitted, cancellationToken).ConfigureAwait(false);
        var failed = await CountEventsAsync(AnalyticsEvents.AiRequestFailed, cancellationToken).ConfigureAwait(false);

        var reviewedProperties = await context.ProductEvents.AsNoTracking()
            .Where(e => e.EventName == AnalyticsEvents.AiSuggestionReviewed)
            .Select(e => e.PropertiesJson)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var reviewed = reviewedProperties.Count;
        var edited = reviewedProperties.Count(WasEditedFlagSet);

        return new AiUsageSummaryDto(submitted, failed, reviewed, edited);
    }

    public async Task<RetentionSummaryDto> GetRetentionSummaryAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Brainy's event volume does not warrant an OLAP pipeline yet: pulling
        // (UserId, OccurredAtUtc) pairs once and cohorting in memory is simple and
        // correct at current scale. Revisit if ProductEvent grows past a few million rows.
        var events = await context.ProductEvents.AsNoTracking()
            .Select(e => new { e.UserId, e.OccurredAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var day7CohortSize = 0;
        var day7Retained = 0;
        var day30CohortSize = 0;
        var day30Retained = 0;

        foreach (var userEvents in events.GroupBy(e => e.UserId))
        {
            var firstEventAt = userEvents.Min(e => e.OccurredAtUtc);

            if (firstEventAt <= now.AddDays(-7))
            {
                day7CohortSize++;
                if (userEvents.Any(e => e.OccurredAtUtc >= firstEventAt.AddDays(7)))
                    day7Retained++;
            }

            if (firstEventAt <= now.AddDays(-30))
            {
                day30CohortSize++;
                if (userEvents.Any(e => e.OccurredAtUtc >= firstEventAt.AddDays(30)))
                    day30Retained++;
            }
        }

        return new RetentionSummaryDto(day7CohortSize, day7Retained, day30CohortSize, day30Retained);
    }

    private async Task WriteEventAsync(
        string userId,
        string eventName,
        IReadOnlyDictionary<string, object?>? properties,
        CancellationToken cancellationToken)
    {
        var productEvent = new ProductEvent
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            EventName = eventName,
            OccurredAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            PropertiesJson = SerializeProperties(properties),
        };

        context.ProductEvents.Add(productEvent);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsCurrentUserOptedInAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        return await IsAnalyticsEnabledAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetCurrentUserOptedInAsync(bool optedIn, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        var preference = await context.DashboardPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (preference is null)
        {
            preference = new UserDashboardPreference { UserId = userId, AnalyticsEnabled = optedIn };
            context.DashboardPreferences.Add(preference);
        }
        else
        {
            preference.AnalyticsEnabled = optedIn;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> IsAnalyticsEnabledAsync(string userId, CancellationToken cancellationToken)
    {
        var enabled = await context.DashboardPreferences.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (bool?)p.AnalyticsEnabled)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        // No preference row yet means the user has never changed the default: opted in.
        return enabled ?? true;
    }

    private Task<int> CountEventsAsync(string eventName, CancellationToken cancellationToken) =>
        context.ProductEvents.AsNoTracking().CountAsync(e => e.EventName == eventName, cancellationToken);

    private Task<int> CountDistinctUsersWithEventAsync(string eventName, CancellationToken cancellationToken) =>
        context.ProductEvents.AsNoTracking()
            .Where(e => e.EventName == eventName)
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

    private Task<int> CountDistinctUsersAsync(CancellationToken cancellationToken) =>
        context.ProductEvents.AsNoTracking()
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync(cancellationToken);

    private static void ValidateEventName(string eventName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        if (!KnownEventNames.Contains(eventName))
            throw new ArgumentException(
                $"'{eventName}' is not a declared analytics event. Add it to AnalyticsEvents first.",
                nameof(eventName));
    }

    private static string? SerializeProperties(IReadOnlyDictionary<string, object?>? properties)
    {
        if (properties is null || properties.Count == 0)
            return null;

        var json = JsonSerializer.Serialize(properties);
        if (json.Length > MaxPropertiesJsonLength)
            throw new ArgumentException(
                "Analytics event properties must be small, non-content metadata only.", nameof(properties));

        return json;
    }

    private static bool WasEditedFlagSet(string? propertiesJson)
    {
        if (string.IsNullOrWhiteSpace(propertiesJson))
            return false;

        try
        {
            using var document = JsonDocument.Parse(propertiesJson);
            return document.RootElement.TryGetProperty("edited", out var edited) &&
                   edited.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
