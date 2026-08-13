using Brainy.Application.DTOs.Pulse;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Builds Pulse from the immutable lifecycle ledger.
/// </summary>
internal sealed class PulseService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IUserTimeZoneService userTimeZone) : IPulseService
{
    private static readonly DayOfWeek[] Weekdays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    private static readonly string[] TimeBlockLabels =
    [
        "12-3 AM",
        "4-7 AM",
        "8-11 AM",
        "12-3 PM",
        "4-7 PM",
        "8-11 PM",
    ];

    public async Task<PulseReportDto> GetReportAsync(
        PulsePeriod period,
        DateTime? customStartUtc = null,
        DateTime? customEndUtc = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var (start, end) = await ResolveRangeAsync(
            period, customStartUtc, customEndUtc, cancellationToken).ConfigureAwait(false);
        var timeZone = await userTimeZone.GetTimeZoneAsync(cancellationToken).ConfigureAwait(false);

        var lifecycle = await context.LifecycleActivities
            .AsNoTracking()
            .Where(a => a.UserId == userId && a.OccurredAtUtc >= start && a.OccurredAtUtc < end)
            .Select(a => new PulseActivityLogEntryDto(
                a.EntityId, a.ActivityType, a.OccurredAtUtc, a.Title, a.Context, a.Link))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var log = lifecycle
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();

        int Count(PulseActivityType type) => log.Count(e => e.ActivityType == type);

        var summary = new PulseSummaryDto(
            NotesCaptured:     Count(PulseActivityType.NoteCaptured),
            NotesProcessed:    Count(PulseActivityType.NoteProcessed),
            NotesArchived:     Count(PulseActivityType.NoteArchived),
            HighlightsAdded:   Count(PulseActivityType.HighlightAdded),
            SummariesCreated:  Count(PulseActivityType.SummaryCreated),
            TasksCreated:      Count(PulseActivityType.TaskCreated),
            TasksCompleted:    Count(PulseActivityType.TaskCompleted),
            TasksArchived:     Count(PulseActivityType.TaskArchived),
            ProjectsCreated:   Count(PulseActivityType.ProjectCreated),
            ProjectsCompleted: Count(PulseActivityType.ProjectCompleted),
            ProjectsArchived:  Count(PulseActivityType.ProjectArchived),
            OutputsCreated:    Count(PulseActivityType.OutputCreated),
            OutputsPublished:  Count(PulseActivityType.OutputPublished),
            IdeasCaptured:     Count(PulseActivityType.IdeaCaptured),
            IdeasCommitted:    Count(PulseActivityType.IdeaCommitted),
            GoalsAchieved:     Count(PulseActivityType.GoalAchieved),
            TotalActivities:   log.Count,
            ActiveDays:        log.Select(e => TimeZoneInfo.ConvertTimeFromUtc(
                                    DateTime.SpecifyKind(e.OccurredAtUtc, DateTimeKind.Utc), timeZone).Date)
                                  .Distinct().Count());

        var analytics = BuildAnalytics(log, start, end, timeZone, summary.ActiveDays);

        return new PulseReportDto(period, start, end, timeZone.Id, summary, analytics, log);
    }

    private static PulseAnalyticsDto BuildAnalytics(
        IReadOnlyList<PulseActivityLogEntryDto> log,
        DateTime startUtc,
        DateTime endUtc,
        TimeZoneInfo timeZone,
        int activeDays)
    {
        var localActivities = log
            .Select(entry => new LocalActivity(
               entry.ActivityType,
               TimeZoneInfo.ConvertTimeFromUtc(AsUtc(entry.OccurredAtUtc), timeZone)))
            .ToList();

        var firstDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(AsUtc(startUtc), timeZone));
        var lastDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(AsUtc(endUtc.AddTicks(-1)), timeZone));
        var daysInPeriod = lastDate.DayNumber - firstDate.DayNumber + 1;
        var activitiesByDate = localActivities
            .GroupBy(activity => DateOnly.FromDateTime(activity.OccurredAt))
            .ToDictionary(group => group.Key, group => group.ToList());

        var dailyActivity = Enumerable.Range(0, daysInPeriod)
            .Select(offset =>
            {
               var date = firstDate.AddDays(offset);
               var activities = activitiesByDate.GetValueOrDefault(date, []);
               return new PulseDailyActivityDto(
                   date,
                   activities.Count,
                   activities.Count(activity => IsForwardProgress(activity.ActivityType)));
            })
            .ToList();

        var productiveWeekdays = Weekdays
            .Select(day =>
            {
               var occurrences = dailyActivity.Count(activity => activity.Date.DayOfWeek == day);
               var total = dailyActivity
                   .Where(activity => activity.Date.DayOfWeek == day)
                   .Sum(activity => activity.ForwardProgressActivities);
               return new PulseActivityBucketDto(
                   day.ToString()[..3],
                   occurrences == 0 ? 0 : Math.Round((double)total / occurrences, 1));
            })
            .ToList();

        var productiveTimeBlocks = TimeBlockLabels
            .Select((label, index) => new PulseActivityBucketDto(
               label,
               localActivities.Count(activity =>
                   IsForwardProgress(activity.ActivityType) &&
                   activity.OccurredAt.Hour / 4 == index)))
            .ToList();

        var activityMix = new[]
        {
            new PulseActivityBucketDto(
               "Capture",
               localActivities.Count(activity => GetCategory(activity.ActivityType) == ActivityCategory.Capture)),
            new PulseActivityBucketDto(
               "Organize",
               localActivities.Count(activity => GetCategory(activity.ActivityType) == ActivityCategory.Organize)),
            new PulseActivityBucketDto(
               "Distill",
               localActivities.Count(activity => GetCategory(activity.ActivityType) == ActivityCategory.Distill)),
            new PulseActivityBucketDto(
               "Execute & express",
               localActivities.Count(activity => GetCategory(activity.ActivityType) == ActivityCategory.ExecuteAndExpress)),
            new PulseActivityBucketDto(
               "Maintenance",
               localActivities.Count(activity => GetCategory(activity.ActivityType) == ActivityCategory.Maintenance)),
        };

        var forwardProgressActivities = localActivities.Count(activity =>
            IsForwardProgress(activity.ActivityType));
        var mostProductiveWeekday = GetTopBucketLabel(productiveWeekdays);
        var mostProductiveTimeBlock = GetTopBucketLabel(productiveTimeBlocks);
        var busiestDay = dailyActivity
            .Where(activity => activity.TotalActivities > 0)
            .MaxBy(activity => activity.TotalActivities);

        return new PulseAnalyticsDto(
            dailyActivity,
            productiveWeekdays,
            productiveTimeBlocks,
            activityMix,
            forwardProgressActivities,
            log.Count == 0 ? 0 : Math.Round((double)forwardProgressActivities / log.Count * 100, 0),
            activeDays == 0 ? 0 : Math.Round((double)log.Count / activeDays, 1),
            daysInPeriod,
            mostProductiveWeekday,
            mostProductiveTimeBlock,
            busiestDay?.Date,
            busiestDay?.TotalActivities ?? 0);
    }

    private static string? GetTopBucketLabel(IReadOnlyList<PulseActivityBucketDto> buckets)
    {
        var maximum = buckets.Max(bucket => bucket.Value);
        return maximum == 0
            ? null
            : string.Join(" / ", buckets
                .Where(bucket => bucket.Value == maximum)
                .Select(bucket => bucket.Label));
    }

    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static bool IsForwardProgress(PulseActivityType type) => type is
        PulseActivityType.NoteProcessed or
        PulseActivityType.HighlightAdded or
        PulseActivityType.SummaryCreated or
        PulseActivityType.TaskCompleted or
        PulseActivityType.ProjectCompleted or
        PulseActivityType.OutputCreated or
        PulseActivityType.OutputPublished or
        PulseActivityType.IdeaCommitted or
        PulseActivityType.GoalAchieved;

    private static ActivityCategory GetCategory(PulseActivityType type) => type switch
    {
        PulseActivityType.NoteCaptured or
        PulseActivityType.TaskCreated or
        PulseActivityType.ProjectCreated or
        PulseActivityType.IdeaCaptured => ActivityCategory.Capture,

        PulseActivityType.NoteProcessed => ActivityCategory.Organize,

        PulseActivityType.HighlightAdded or
        PulseActivityType.SummaryCreated => ActivityCategory.Distill,

        PulseActivityType.TaskCompleted or
        PulseActivityType.ProjectCompleted or
        PulseActivityType.OutputCreated or
        PulseActivityType.OutputPublished or
        PulseActivityType.IdeaCommitted or
        PulseActivityType.GoalAchieved => ActivityCategory.ExecuteAndExpress,

        _ => ActivityCategory.Maintenance,
    };

    private async Task<(DateTime StartUtc, DateTime EndUtc)> ResolveRangeAsync(
        PulsePeriod period,
        DateTime? customStartUtc,
        DateTime? customEndUtc,
        CancellationToken cancellationToken)
    {
        if (period == PulsePeriod.Custom)
        {
            if (customStartUtc is null || customEndUtc is null)
                throw new ArgumentException("Custom Pulse periods require both a start and an end date.");

            var start = customStartUtc.Value.Date;
            var end = customEndUtc.Value.Date;
            if (end < start)
                throw new ArgumentException("The Pulse period end date must be on or after the start date.");

            return await userTimeZone.GetUtcRangeAsync(start, end, cancellationToken).ConfigureAwait(false);
        }

        var today = await userTimeZone.GetUserTodayAsync(cancellationToken).ConfigureAwait(false);
        var days = period switch
        {
            PulsePeriod.LastWeek => 7,
            PulsePeriod.LastTwoWeeks => 14,
            PulsePeriod.LastThreeWeeks => 21,
            PulsePeriod.LastMonth => 30,
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unsupported Pulse period."),
        };

        return await userTimeZone.GetUtcRangeAsync(
            today.AddDays(1 - days), today, cancellationToken).ConfigureAwait(false);
    }

    private sealed record LocalActivity(PulseActivityType ActivityType, DateTime OccurredAt);

    private enum ActivityCategory
    {
        Capture,
        Organize,
        Distill,
        ExecuteAndExpress,
        Maintenance,
    }
}
