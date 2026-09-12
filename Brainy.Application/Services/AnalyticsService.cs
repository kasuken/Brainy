using System.Globalization;
using System.Text;
using System.Text.Json;
using Brainy.Application.Analytics;
using Brainy.Application.DTOs.Analytics;
using Brainy.Application.DTOs.DataExport;
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
    IUserDirectoryService userDirectory,
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
        var registeredUsers = await userDirectory.GetRegisteredUserCountAsync(cancellationToken).ConfigureAwait(false);
        var consentExcludedUsers = await CountOptedOutUsersAsync(cancellationToken).ConfigureAwait(false);
        var totalUsers = await CountDistinctUsersAsync(cancellationToken).ConfigureAwait(false);
        var firstCapture = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstCaptureCreated, cancellationToken).ConfigureAwait(false);
        var firstProcessed = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstInboxItemProcessed, cancellationToken).ConfigureAwait(false);
        var firstTask = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstTaskCreated, cancellationToken).ConfigureAwait(false);
        var firstFocus = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstCurrentFocusSelected, cancellationToken).ConfigureAwait(false);
        var firstOutput = await CountDistinctUsersWithEventAsync(AnalyticsEvents.FirstOutputCreated, cancellationToken).ConfigureAwait(false);

        return new ActivationFunnelDto(
            registeredUsers,
            consentExcludedUsers,
            totalUsers,
            firstCapture,
            firstProcessed,
            firstTask,
            firstFocus,
            firstOutput);
    }

    public async Task<KnowledgeReuseDto> GetKnowledgeReuseSummaryAsync(CancellationToken cancellationToken = default)
    {
        var totalCaptures = await CountEventsAsync(AnalyticsEvents.CaptureCreated, cancellationToken).ConfigureAwait(false);
        var reusedAsProject = await CountEventsAsync(AnalyticsEvents.CaptureReusedAsProject, cancellationToken).ConfigureAwait(false);
        var reusedAsTask = await CountEventsAsync(AnalyticsEvents.CaptureReusedAsTask, cancellationToken).ConfigureAwait(false);
        var reusedAsOutput = await CountEventsAsync(AnalyticsEvents.CaptureReusedAsOutput, cancellationToken).ConfigureAwait(false);

        return new KnowledgeReuseDto(totalCaptures, reusedAsProject, reusedAsTask, reusedAsOutput);
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
        var totalCaptures = await CountEventsAsync(AnalyticsEvents.CaptureCreated, cancellationToken).ConfigureAwait(false);
        var processed = await CountEventsAsync(AnalyticsEvents.InboxItemProcessed, cancellationToken).ConfigureAwait(false);

        return new WeeklyReviewSummaryDto(views, resurfaced, totalCaptures, processed);
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

    public async Task<AnalyticsMetricsExportDto> ExportMetricsCsvAsync(CancellationToken cancellationToken = default)
    {
        var funnel = await GetActivationFunnelAsync(cancellationToken).ConfigureAwait(false);
        var reuse = await GetKnowledgeReuseSummaryAsync(cancellationToken).ConfigureAwait(false);
        var search = await GetSearchQualityAsync(cancellationToken).ConfigureAwait(false);
        var weeklyReview = await GetWeeklyReviewSummaryAsync(cancellationToken).ConfigureAwait(false);
        var retention = await GetRetentionSummaryAsync(cancellationToken).ConfigureAwait(false);

        var csv = new StringBuilder();
        csv.Append("section,metric,value\n");

        AppendRow(csv, "activation_funnel", "registered_users", funnel.RegisteredUserCount);
        AppendRow(csv, "activation_funnel", "consent_excluded_users", funnel.ConsentExcludedUserCount);
        AppendRow(csv, "activation_funnel", "eligible_users", funnel.EligibleUserCount);
        AppendRow(csv, "activation_funnel", "users_with_any_event", funnel.UsersWithAnyEvent);
        AppendRow(csv, "activation_funnel", "first_capture_users", funnel.UsersWithFirstCapture);
        AppendRow(csv, "activation_funnel", "first_capture_rate_of_eligible", funnel.FirstCaptureRate);
        AppendRow(csv, "activation_funnel", "first_classify_users", funnel.UsersWithFirstProcessedItem);
        AppendRow(csv, "activation_funnel", "first_classify_rate_of_eligible", funnel.FirstProcessedRate);
        AppendRow(csv, "activation_funnel", "first_classify_rate_of_prior_step", funnel.CaptureToProcessedRate);
        AppendRow(csv, "activation_funnel", "first_task_users", funnel.UsersWithFirstTask);
        AppendRow(csv, "activation_funnel", "first_task_rate_of_eligible", funnel.FirstTaskRate);
        AppendRow(csv, "activation_funnel", "first_task_rate_of_prior_step", funnel.ProcessedToTaskRate);
        AppendRow(csv, "activation_funnel", "first_focus_users", funnel.UsersWithFirstFocusSelection);
        AppendRow(csv, "activation_funnel", "first_focus_rate_of_eligible", funnel.FirstFocusRate);
        AppendRow(csv, "activation_funnel", "first_focus_rate_of_prior_step", funnel.TaskToFocusRate);
        AppendRow(csv, "activation_funnel", "first_output_users", funnel.UsersWithFirstOutput);
        AppendRow(csv, "activation_funnel", "first_output_rate_of_eligible", funnel.FirstOutputRate);
        AppendRow(csv, "activation_funnel", "first_output_rate_of_prior_step", funnel.FocusToOutputRate);

        AppendRow(csv, "knowledge_reuse", "total_captures", reuse.TotalCaptures);
        AppendRow(csv, "knowledge_reuse", "reused_as_project", reuse.ReusedAsProjectCount);
        AppendRow(csv, "knowledge_reuse", "reused_as_task", reuse.ReusedAsTaskCount);
        AppendRow(csv, "knowledge_reuse", "reused_as_output", reuse.ReusedAsOutputCount);
        AppendRow(csv, "knowledge_reuse", "total_reuse_actions", reuse.TotalReuseActions);
        AppendRow(csv, "knowledge_reuse", "reuse_rate", reuse.ReuseRate);

        AppendRow(csv, "retrieval_effectiveness", "searches_submitted", search.SearchesSubmitted);
        AppendRow(csv, "retrieval_effectiveness", "zero_result_searches", search.ZeroResultSearches);
        AppendRow(csv, "retrieval_effectiveness", "zero_result_rate", search.ZeroResultRate);
        AppendRow(csv, "retrieval_effectiveness", "results_opened", search.ResultsOpened);
        AppendRow(csv, "retrieval_effectiveness", "result_open_rate", search.ResultOpenRate);
        AppendRow(csv, "retrieval_effectiveness", "follow_on_actions", search.FollowOnActions);
        AppendRow(csv, "retrieval_effectiveness", "follow_on_rate", search.FollowOnRate);

        AppendRow(csv, "weekly_review_and_inbox_adherence", "weekly_review_views", weeklyReview.WeeklyReviewViews);
        AppendRow(csv, "weekly_review_and_inbox_adherence", "resurfaced_item_actions", weeklyReview.ResurfacedItemActions);
        AppendRow(csv, "weekly_review_and_inbox_adherence", "total_captures", weeklyReview.TotalCaptures);
        AppendRow(csv, "weekly_review_and_inbox_adherence", "inbox_items_processed", weeklyReview.InboxItemsProcessed);
        AppendRow(csv, "weekly_review_and_inbox_adherence", "inbox_processing_adherence_rate", weeklyReview.InboxProcessingAdherenceRate);

        AppendRow(csv, "retention", "day7_cohort_size", retention.Day7CohortSize);
        AppendRow(csv, "retention", "day7_retained", retention.Day7Retained);
        AppendRow(csv, "retention", "day7_retention_rate", retention.Day7RetentionRate);
        AppendRow(csv, "retention", "day30_cohort_size", retention.Day30CohortSize);
        AppendRow(csv, "retention", "day30_retained", retention.Day30Retained);
        AppendRow(csv, "retention", "day30_retention_rate", retention.Day30RetentionRate);

        var generatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var fileName = $"brainy-activation-metrics-{generatedAtUtc:yyyy-MM-dd}.csv";
        var content = Encoding.UTF8.GetBytes(csv.ToString());

        return new AnalyticsMetricsExportDto(fileName, "text/csv", content);
    }

    /// <summary>
    /// Appends one aggregate metric row. Every value here is a cross-user count or rate —
    /// never a per-user identifier or content — per issue #324's aggregate-only guardrail.
    /// </summary>
    private static void AppendRow(StringBuilder csv, string section, string metric, double value) =>
        csv.Append(section).Append(',').Append(metric).Append(',')
            .Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');

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

    /// <summary>
    /// Users known to have opted out of analytics: those with an explicit
    /// <see cref="UserDashboardPreference"/> row recording <c>AnalyticsEnabled == false</c>.
    /// A user who never touched the toggle has no row and defaults to opted in (see
    /// <see cref="IsAnalyticsEnabledAsync"/>), so this count never overstates the gap.
    /// </summary>
    private Task<int> CountOptedOutUsersAsync(CancellationToken cancellationToken) =>
        context.DashboardPreferences.AsNoTracking()
            .CountAsync(p => !p.AnalyticsEnabled, cancellationToken);

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
