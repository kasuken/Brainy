namespace Brainy.Application.DTOs.Analytics;

/// <summary>
/// Activation funnel counts across all users: registered &rarr; first capture &rarr; first
/// classify (Inbox processed) &rarr; first task &rarr; first current focus &rarr; first
/// output (issue #324). Intentionally NOT scoped to a single user: this is an aggregate,
/// internal-admin view answering "does the product activate new users," not a per-user
/// report.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ConsentExcludedUserCount"/> is the known denominator gap: users who opted out
/// of analytics are structurally invisible to every event-based count in this DTO (and every
/// other <c>*SummaryDto</c> in this file), because <see cref="Brainy.Application.Services.AnalyticsService.TrackAsync"/>
/// never writes an event for them. Per-step rates are computed against
/// <see cref="EligibleUserCount"/> (registered minus known-excluded) rather than against
/// <see cref="RegisteredUserCount"/>, but callers must render <see cref="ConsentExcludedUserCount"/>
/// alongside every rate so nobody mistakes "of eligible users" for "of everyone who
/// registered" &mdash; see issue #324's guardrail against silently undercounting.
/// </para>
/// </remarks>
public sealed record ActivationFunnelDto(
    int RegisteredUserCount,
    int ConsentExcludedUserCount,
    int UsersWithAnyEvent,
    int UsersWithFirstCapture,
    int UsersWithFirstProcessedItem,
    int UsersWithFirstTask,
    int UsersWithFirstFocusSelection,
    int UsersWithFirstOutput)
{
    /// <summary>
    /// Registered users who have not (as far as this system knows) opted out of analytics
    /// &mdash; the largest population any event-based metric here could possibly observe.
    /// </summary>
    public int EligibleUserCount => Math.Max(RegisteredUserCount - ConsentExcludedUserCount, 0);

    // "Of registered (eligible)" conversion for each funnel step.
    public double FirstCaptureRate => Rate(UsersWithFirstCapture, EligibleUserCount);
    public double FirstProcessedRate => Rate(UsersWithFirstProcessedItem, EligibleUserCount);
    public double FirstTaskRate => Rate(UsersWithFirstTask, EligibleUserCount);
    public double FirstFocusRate => Rate(UsersWithFirstFocusSelection, EligibleUserCount);
    public double FirstOutputRate => Rate(UsersWithFirstOutput, EligibleUserCount);

    // Step-over-step drop-off, in funnel order, for the per-step conversion/drop-off view.
    public double CaptureToProcessedRate => Rate(UsersWithFirstProcessedItem, UsersWithFirstCapture);
    public double ProcessedToTaskRate => Rate(UsersWithFirstTask, UsersWithFirstProcessedItem);
    public double TaskToFocusRate => Rate(UsersWithFirstFocusSelection, UsersWithFirstTask);
    public double FocusToOutputRate => Rate(UsersWithFirstOutput, UsersWithFirstFocusSelection);

    private static double Rate(int part, int whole) => whole <= 0 ? 0 : (double)part / whole;
}

/// <summary>Search retrieval-quality aggregate across all users.</summary>
public sealed record SearchQualityDto(
    int SearchesSubmitted,
    int ZeroResultSearches,
    int ResultsOpened,
    int FollowOnActions)
{
    public double ZeroResultRate => SearchesSubmitted == 0 ? 0 : (double)ZeroResultSearches / SearchesSubmitted;
    public double ResultOpenRate => SearchesSubmitted == 0 ? 0 : (double)ResultsOpened / SearchesSubmitted;
    public double FollowOnRate => ResultsOpened == 0 ? 0 : (double)FollowOnActions / ResultsOpened;
}

/// <summary>
/// Knowledge-reuse aggregate across all users: what share of captured items go on to be
/// referenced in a project, task, or output (issue #324's "knowledge reuse rate"). Computed
/// entirely from existing events &mdash; no event carries which specific note was reused, only
/// that a reuse action of a given kind happened, so <see cref="ReuseRate"/> is a
/// captures-vs-reuse-actions ratio (a single capture reused twice counts twice), not a
/// distinct-item rate.
/// </summary>
public sealed record KnowledgeReuseDto(
    int TotalCaptures,
    int ReusedAsProjectCount,
    int ReusedAsTaskCount,
    int ReusedAsOutputCount)
{
    public int TotalReuseActions => ReusedAsProjectCount + ReusedAsTaskCount + ReusedAsOutputCount;

    public double ReuseRate => TotalCaptures == 0 ? 0 : (double)TotalReuseActions / TotalCaptures;
}

/// <summary>
/// Weekly-review engagement and Inbox-processing adherence aggregate across all users.
/// <see cref="InboxProcessingAdherenceRate"/> approximates how much of what gets captured is
/// actually triaged out of the Inbox, from the existing capture/processed event counts.
/// </summary>
public sealed record WeeklyReviewSummaryDto(
    int WeeklyReviewViews,
    int ResurfacedItemActions,
    int TotalCaptures,
    int InboxItemsProcessed)
{
    public double InboxProcessingAdherenceRate =>
        TotalCaptures == 0 ? 0 : (double)InboxItemsProcessed / TotalCaptures;
}

/// <summary>AI usage aggregate across all users. Never contains prompt or generated text.</summary>
public sealed record AiUsageSummaryDto(
    int RequestsSubmitted,
    int RequestsFailed,
    int SuggestionsReviewed,
    int SuggestionsEdited)
{
    public double FailureRate => RequestsSubmitted == 0 ? 0 : (double)RequestsFailed / RequestsSubmitted;
    public double EditRate => SuggestionsReviewed == 0 ? 0 : (double)SuggestionsEdited / SuggestionsReviewed;
}

/// <summary>
/// Approximate D7/D30 retention: of users whose first-ever tracked event happened at least
/// that many days ago, the share with at least one more event on/after that boundary.
/// </summary>
public sealed record RetentionSummaryDto(
    int Day7CohortSize,
    int Day7Retained,
    int Day30CohortSize,
    int Day30Retained)
{
    public double Day7RetentionRate => Day7CohortSize == 0 ? 0 : (double)Day7Retained / Day7CohortSize;
    public double Day30RetentionRate => Day30CohortSize == 0 ? 0 : (double)Day30Retained / Day30CohortSize;
}
