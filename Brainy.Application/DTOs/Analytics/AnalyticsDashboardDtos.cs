namespace Brainy.Application.DTOs.Analytics;

/// <summary>
/// Activation funnel counts across all users. Intentionally NOT scoped to a single user:
/// this is an aggregate, internal-admin view answering "does the product activate new
/// users," not a per-user report.
/// </summary>
public sealed record ActivationFunnelDto(
    int UsersWithAnyEvent,
    int UsersWithFirstCapture,
    int UsersWithFirstProcessedItem,
    int UsersWithFirstTask,
    int UsersWithFirstFocusSelection)
{
    public double FirstCaptureRate => Rate(UsersWithFirstCapture, UsersWithAnyEvent);
    public double FirstProcessedRate => Rate(UsersWithFirstProcessedItem, UsersWithAnyEvent);
    public double FirstTaskRate => Rate(UsersWithFirstTask, UsersWithAnyEvent);
    public double FirstFocusRate => Rate(UsersWithFirstFocusSelection, UsersWithAnyEvent);

    private static double Rate(int part, int whole) => whole == 0 ? 0 : (double)part / whole;
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

/// <summary>Weekly-review engagement aggregate across all users.</summary>
public sealed record WeeklyReviewSummaryDto(
    int WeeklyReviewViews,
    int ResurfacedItemActions);

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
