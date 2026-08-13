namespace Brainy.Application.DTOs.Pulse;

/// <summary>
/// Time-based and behavioral analytics derived from a Pulse activity report.
/// </summary>
/// <param name="DailyActivity">Activity and forward-progress totals for every local calendar day.</param>
/// <param name="ProductiveWeekdays">Average forward-progress activity by weekday.</param>
/// <param name="ProductiveTimeBlocks">Forward-progress activity grouped into equal four-hour local-time blocks.</param>
/// <param name="ActivityMix">All activity grouped by CODE-oriented workflow category.</param>
/// <param name="ForwardProgressActivities">Activities that moved work beyond capture or maintenance.</param>
/// <param name="ForwardProgressPercentage">Percentage of all activity that represented forward progress.</param>
/// <param name="AverageActivitiesPerActiveDay">Average activity count on days when activity occurred.</param>
/// <param name="DaysInPeriod">Number of local calendar days covered by the report.</param>
/// <param name="MostProductiveWeekday">Weekday with the highest average forward progress, when available.</param>
/// <param name="MostProductiveTimeBlock">Four-hour block with the most forward progress, when available.</param>
/// <param name="BusiestDate">Local calendar date with the most total activity, when available.</param>
/// <param name="BusiestDateActivities">Total activity recorded on <paramref name="BusiestDate"/>.</param>
public sealed record PulseAnalyticsDto(
    IReadOnlyList<PulseDailyActivityDto> DailyActivity,
    IReadOnlyList<PulseActivityBucketDto> ProductiveWeekdays,
    IReadOnlyList<PulseActivityBucketDto> ProductiveTimeBlocks,
    IReadOnlyList<PulseActivityBucketDto> ActivityMix,
    int ForwardProgressActivities,
    double ForwardProgressPercentage,
    double AverageActivitiesPerActiveDay,
    int DaysInPeriod,
    string? MostProductiveWeekday,
    string? MostProductiveTimeBlock,
    DateOnly? BusiestDate,
    int BusiestDateActivities);

/// <summary>
/// Activity totals for one local calendar day.
/// </summary>
/// <param name="Date">The user's local calendar date.</param>
/// <param name="TotalActivities">All activities recorded on the date.</param>
/// <param name="ForwardProgressActivities">Activities that moved work beyond capture or maintenance.</param>
public sealed record PulseDailyActivityDto(
    DateOnly Date,
    int TotalActivities,
    int ForwardProgressActivities);

/// <summary>
/// A named analytics bucket and its numeric value.
/// </summary>
/// <param name="Label">Human-readable bucket label.</param>
/// <param name="Value">The count or average represented by the bucket.</param>
public sealed record PulseActivityBucketDto(string Label, double Value);
