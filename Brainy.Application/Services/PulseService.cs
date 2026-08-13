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

        return new PulseReportDto(period, start, end, summary, log);
    }

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
}
