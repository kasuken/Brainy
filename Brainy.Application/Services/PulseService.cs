using Brainy.Application.Common;
using Brainy.Application.DTOs.Pulse;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Builds the Pulse activity report by deriving activity from the lifecycle timestamps
/// already tracked on each entity (created, processed, completed, archived, etc.) rather
/// than a separate audit log — every count and log entry reflects a timestamp landing
/// within the selected date range.
/// </summary>
internal sealed class PulseService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    TimeProvider timeProvider) : IPulseService
{
    public async Task<PulseReportDto> GetReportAsync(
        PulsePeriod period,
        DateTime? customStartUtc = null,
        DateTime? customEndUtc = null,
        CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var (start, end) = ResolveRange(period, customStartUtc, customEndUtc, timeProvider);

        // EF Core's DbContext is not thread-safe, so every query below runs sequentially
        // even though the intent is to gather all activity in a single pass.
        var notesCaptured = await context.Notes.AsNoTracking()
            .Where(n => n.UserId == userId && n.CreatedAtUtc >= start && n.CreatedAtUtc < end)
            .Select(n => new PulseActivityLogEntryDto(n.Id, PulseActivityType.NoteCaptured, n.CreatedAtUtc, n.Title, "Captured", $"/notes/{n.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var notesProcessed = await context.Notes.AsNoTracking()
            .Where(n => n.UserId == userId && n.ProcessedAtUtc != null && n.ProcessedAtUtc >= start && n.ProcessedAtUtc < end)
            .Select(n => new PulseActivityLogEntryDto(n.Id, PulseActivityType.NoteProcessed, n.ProcessedAtUtc!.Value, n.Title, "Processed from Inbox", $"/notes/{n.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var notesArchived = await context.Notes.AsNoTracking()
            .Where(n => n.UserId == userId && n.IsArchived && n.ArchivedAtUtc != null && n.ArchivedAtUtc >= start && n.ArchivedAtUtc < end)
            .Select(n => new PulseActivityLogEntryDto(n.Id, PulseActivityType.NoteArchived, n.ArchivedAtUtc!.Value, n.Title, "Archived", $"/notes/{n.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var highlights = await context.Highlights.AsNoTracking()
            .Where(h => h.Note.UserId == userId && h.CreatedAtUtc >= start && h.CreatedAtUtc < end)
            .Select(h => new PulseActivityLogEntryDto(h.Id, PulseActivityType.HighlightAdded, h.CreatedAtUtc, h.Note.Title, "Highlight added", $"/notes/{h.NoteId}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var summaries = await context.Summaries.AsNoTracking()
            .Where(s => s.Note.UserId == userId && s.CreatedAtUtc >= start && s.CreatedAtUtc < end)
            .Select(s => new PulseActivityLogEntryDto(s.Id, PulseActivityType.SummaryCreated, s.CreatedAtUtc, s.Note.Title, s.IsAiGenerated ? "AI summary" : "Summary added", $"/notes/{s.NoteId}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tasksCreated = await context.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && t.CreatedAtUtc >= start && t.CreatedAtUtc < end)
            .Select(t => new PulseActivityLogEntryDto(t.Id, PulseActivityType.TaskCreated, t.CreatedAtUtc, t.Title, $"Created · {t.Project.Name}", $"/projects/{t.ProjectId}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tasksCompleted = await context.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && t.Status == TaskItemStatus.Done && t.CompletedDate != null && t.CompletedDate >= start && t.CompletedDate < end)
            .Select(t => new PulseActivityLogEntryDto(t.Id, PulseActivityType.TaskCompleted, t.CompletedDate!.Value, t.Title, $"Completed · {t.Project.Name}", $"/projects/{t.ProjectId}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var tasksArchived = await context.Tasks.AsNoTracking()
            .Where(t => t.UserId == userId && t.IsArchived && t.ArchivedAtUtc != null && t.ArchivedAtUtc >= start && t.ArchivedAtUtc < end)
            .Select(t => new PulseActivityLogEntryDto(t.Id, PulseActivityType.TaskArchived, t.ArchivedAtUtc!.Value, t.Title, $"Archived · {t.Project.Name}", $"/projects/{t.ProjectId}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var projectsCreated = await context.Projects.AsNoTracking()
            .Where(p => p.UserId == userId && p.CreatedAtUtc >= start && p.CreatedAtUtc < end)
            .Select(p => new PulseActivityLogEntryDto(p.Id, PulseActivityType.ProjectCreated, p.CreatedAtUtc, p.Name, "Created", $"/projects/{p.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var projectsCompleted = await context.Projects.AsNoTracking()
            .Where(p => p.UserId == userId && p.Status == ProjectStatus.Completed && p.CompletedDate != null && p.CompletedDate >= start && p.CompletedDate < end)
            .Select(p => new PulseActivityLogEntryDto(p.Id, PulseActivityType.ProjectCompleted, p.CompletedDate!.Value, p.Name, "Completed", $"/projects/{p.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var projectsArchived = await context.Projects.AsNoTracking()
            .Where(p => p.UserId == userId && p.IsArchived && p.ArchivedAtUtc != null && p.ArchivedAtUtc >= start && p.ArchivedAtUtc < end)
            .Select(p => new PulseActivityLogEntryDto(p.Id, PulseActivityType.ProjectArchived, p.ArchivedAtUtc!.Value, p.Name, "Archived", $"/projects/{p.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var outputsCreated = await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && o.CreatedAtUtc >= start && o.CreatedAtUtc < end)
            .Select(o => new PulseActivityLogEntryDto(o.Id, PulseActivityType.OutputCreated, o.CreatedAtUtc, o.Title, "Created", $"/outputs/{o.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var outputsPublished = await context.Outputs.AsNoTracking()
            .Where(o => o.UserId == userId && o.Status == OutputStatus.Published && o.PublishedDate != null && o.PublishedDate >= start && o.PublishedDate < end)
            .Select(o => new PulseActivityLogEntryDto(o.Id, PulseActivityType.OutputPublished, o.PublishedDate!.Value, o.Title, "Published", $"/outputs/{o.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var ideasCaptured = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && i.CreatedAtUtc >= start && i.CreatedAtUtc < end)
            .Select(i => new PulseActivityLogEntryDto(i.Id, PulseActivityType.IdeaCaptured, i.CreatedAtUtc, i.Title, "Captured", $"/ideas/{i.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var ideasCommitted = await context.Ideas.AsNoTracking()
            .Where(i => i.UserId == userId && i.CommittedAtUtc != null && i.CommittedAtUtc >= start && i.CommittedAtUtc < end)
            .Select(i => new PulseActivityLogEntryDto(i.Id, PulseActivityType.IdeaCommitted, i.CommittedAtUtc!.Value, i.Title, "Committed", $"/ideas/{i.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var goalsAchieved = await context.Goals.AsNoTracking()
            .Where(g => g.UserId == userId && g.Status == GoalStatus.Achieved && g.AchievedDate != null && g.AchievedDate >= start && g.AchievedDate < end)
            .Select(g => new PulseActivityLogEntryDto(g.Id, PulseActivityType.GoalAchieved, g.AchievedDate!.Value, g.Title, "Achieved", $"/goals/{g.Id}"))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var log = notesCaptured
            .Concat(notesProcessed)
            .Concat(notesArchived)
            .Concat(highlights)
            .Concat(summaries)
            .Concat(tasksCreated)
            .Concat(tasksCompleted)
            .Concat(tasksArchived)
            .Concat(projectsCreated)
            .Concat(projectsCompleted)
            .Concat(projectsArchived)
            .Concat(outputsCreated)
            .Concat(outputsPublished)
            .Concat(ideasCaptured)
            .Concat(ideasCommitted)
            .Concat(goalsAchieved)
            .OrderByDescending(e => e.OccurredAtUtc)
            .ToList();

        var summary = new PulseSummaryDto(
            NotesCaptured:     notesCaptured.Count,
            NotesProcessed:    notesProcessed.Count,
            NotesArchived:     notesArchived.Count,
            HighlightsAdded:   highlights.Count,
            SummariesCreated:  summaries.Count,
            TasksCreated:      tasksCreated.Count,
            TasksCompleted:    tasksCompleted.Count,
            TasksArchived:     tasksArchived.Count,
            ProjectsCreated:   projectsCreated.Count,
            ProjectsCompleted: projectsCompleted.Count,
            ProjectsArchived:  projectsArchived.Count,
            OutputsCreated:    outputsCreated.Count,
            OutputsPublished:  outputsPublished.Count,
            IdeasCaptured:     ideasCaptured.Count,
            IdeasCommitted:    ideasCommitted.Count,
            GoalsAchieved:     goalsAchieved.Count,
            TotalActivities:   log.Count,
            ActiveDays:        log.Select(e => e.OccurredAtUtc.Date).Distinct().Count());

        return new PulseReportDto(period, start, end, summary, log);
    }

    /// <summary>
    /// Resolves the report's [start, end) date range in UTC. Preset periods are rolling
    /// windows ending at the start of tomorrow (so "today" is always fully included).
    /// </summary>
    internal static (DateTime StartUtc, DateTime EndUtc) ResolveRange(
        PulsePeriod period,
        DateTime? customStartUtc,
        DateTime? customEndUtc,
        TimeProvider timeProvider)
    {
        if (period == PulsePeriod.Custom)
        {
            if (customStartUtc is null || customEndUtc is null)
                throw new ArgumentException("Custom Pulse periods require both a start and an end date.");

            var start = customStartUtc.Value.Date;
            var end = customEndUtc.Value.Date.AddDays(1); // exclusive upper bound, end date inclusive
            if (end <= start)
                throw new ArgumentException("The Pulse period end date must be on or after the start date.");

            return (start, end);
        }

        var today = timeProvider.GetUserToday();
        var rangeEnd = today.AddDays(1);
        var days = period switch
        {
            PulsePeriod.LastWeek => 7,
            PulsePeriod.LastTwoWeeks => 14,
            PulsePeriod.LastThreeWeeks => 21,
            PulsePeriod.LastMonth => 30,
            _ => throw new ArgumentOutOfRangeException(nameof(period), period, "Unsupported Pulse period."),
        };

        return (rangeEnd.AddDays(-days), rangeEnd);
    }
}
