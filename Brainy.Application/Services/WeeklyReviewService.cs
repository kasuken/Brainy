using Brainy.Application.Analytics;
using Brainy.Application.DTOs.ActionItems;
using Brainy.Application.DTOs.WeeklyReview;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Entities;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

/// <summary>
/// Implements the guided weekly review (issue #299). Composes the existing Week planning
/// overview with two additions: Inbox items grouped by age, and previously-useful notes
/// resurfaced with an explicit rationale. Deliberately does not duplicate Week's
/// task-and-project logic (stalled projects, overdue commitments, work selected for the
/// week, carry-forward) — those are read straight from <see cref="IWeekService"/>.
/// </summary>
internal sealed class WeeklyReviewService(
    IApplicationDbContext context,
    ICurrentUserService currentUser,
    IWeekService weekService,
    INoteService noteService,
    IActionItemService actionItemService,
    IAnalyticsService analytics,
    TimeProvider timeProvider) : IWeeklyReviewService
{
    /// <summary>
    /// A note is considered "not revisited" once its last edit is older than this many days.
    /// Notes carry no "last viewed" timestamp today, so <c>UpdatedAtUtc</c> — the most recent
    /// edit — is the best available recency signal; simply opening a note does not touch it.
    /// </summary>
    private const int StaleAfterDays = 14;

    private const int MaxResurfacedNotes = 12;

    /// <inheritdoc />
    public async Task<WeeklyReviewDto> GetCurrentReviewAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Reused as-is: this already tracks AnalyticsEvents.WeeklyReviewViewed on every view.
        var week = await weekService.GetCurrentWeekOverviewAsync(cancellationToken).ConfigureAwait(false);

        var inboxBands = await GetInboxAgeBandsAsync(userId, cancellationToken).ConfigureAwait(false);
        var resurfacedNotes = await GetResurfacedNotesAsync(userId, cancellationToken).ConfigureAwait(false);

        return new WeeklyReviewDto(
            week,
            inboxBands,
            inboxBands.Sum(band => band.Items.Count),
            resurfacedNotes);
    }

    /// <inheritdoc />
    public async Task KeepResurfacedNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        await EnsureOwnedNoteExistsAsync(noteId, userId, cancellationToken).ConfigureAwait(false);

        await TrackDecisionAsync(userId, "keep", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DismissResurfacedNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        await EnsureOwnedNoteExistsAsync(noteId, userId, cancellationToken).ConfigureAwait(false);

        var alreadyDismissed = await context.ResurfacingDismissals
            .AsNoTracking()
            .AnyAsync(dismissal => dismissal.UserId == userId && dismissal.NoteId == noteId, cancellationToken)
            .ConfigureAwait(false);

        if (!alreadyDismissed)
        {
            context.ResurfacingDismissals.Add(new ResurfacingDismissal
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                NoteId = noteId,
                DismissedAtUtc = timeProvider.GetUtcNow().UtcDateTime
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await TrackDecisionAsync(userId, "dismiss", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LinkResurfacedNoteAsync(Guid noteId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        await noteService.LinkToProjectAsync(noteId, projectId, cancellationToken).ConfigureAwait(false);

        await TrackDecisionAsync(userId, "link", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ArchiveResurfacedNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);

        // Archived notes are excluded from GetResurfacedNotesAsync by construction, so no
        // separate dismissal record is needed for the archive decision to "stick".
        await noteService.ArchiveAsync(noteId, cancellationToken, "Archived from the weekly review.")
            .ConfigureAwait(false);

        await TrackDecisionAsync(userId, "archive", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task TurnResurfacedNoteIntoTaskAsync(Guid noteId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var note = await context.Notes
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == noteId && candidate.UserId == userId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Note '{noteId}' was not found.");

        // Reuse the most recent existing action item on this note (if any) instead of
        // creating a duplicate every time "Turn into task" is used from the review.
        var existingActionItemId = await context.ActionItems
            .AsNoTracking()
            .Where(action => action.NoteId == noteId && action.UserId == userId)
            .OrderByDescending(action => action.CreatedAtUtc)
            .Select(action => (Guid?)action.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var actionItemId = existingActionItemId ?? (await actionItemService
            .CreateAsync(new CreateActionItemDto(noteId, note.Title), cancellationToken)
            .ConfigureAwait(false)).Id;

        await actionItemService.PromoteToTaskAsync(actionItemId, projectId, cancellationToken).ConfigureAwait(false);

        await TrackDecisionAsync(userId, "turn_into_task", cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureOwnedNoteExistsAsync(Guid noteId, string userId, CancellationToken cancellationToken)
    {
        var exists = await context.Notes
            .AsNoTracking()
            .AnyAsync(note => note.Id == noteId && note.UserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (!exists)
            throw new KeyNotFoundException($"Note '{noteId}' was not found.");
    }

    private Task TrackDecisionAsync(string userId, string decision, CancellationToken cancellationToken) =>
        analytics.TrackAsync(
            userId,
            AnalyticsEvents.ResurfacedNoteDecisionMade,
            new Dictionary<string, object?> { ["decision"] = decision },
            cancellationToken);

    private async Task<IReadOnlyList<InboxAgeBandDto>> GetInboxAgeBandsAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var inboxNotes = await context.Notes
            .AsNoTracking()
            .Where(note => note.UserId == userId && !note.IsArchived && note.ProcessedAtUtc == null)
            .OrderBy(note => note.CreatedAtUtc)
            .Select(note => new InboxAgeBandItemDto(note.Id, note.Title, note.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return inboxNotes
            .GroupBy(note => GetAgeBandKey(now, note.CreatedAtUtc))
            .OrderBy(group => Array.IndexOf(AgeBandOrder, group.Key))
            .Select(group => new InboxAgeBandDto(group.Key, GetAgeBandLabel(group.Key), group.ToList()))
            .ToList();
    }

    private static readonly string[] AgeBandOrder = ["today", "this-week", "1-4-weeks", "1-month-plus"];

    private static string GetAgeBandKey(DateTime nowUtc, DateTime createdAtUtc)
    {
        var ageDays = Math.Max(0, (int)(nowUtc.Date - createdAtUtc.Date).TotalDays);
        return ageDays switch
        {
            0 => "today",
            <= 7 => "this-week",
            <= 28 => "1-4-weeks",
            _ => "1-month-plus"
        };
    }

    private static string GetAgeBandLabel(string key) => key switch
    {
        "today" => "Captured today",
        "this-week" => "This week",
        "1-4-weeks" => "1-4 weeks old",
        "1-month-plus" => "Over a month old",
        _ => key
    };

    private async Task<IReadOnlyList<ResurfacedNoteDto>> GetResurfacedNotesAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var staleBefore = now.AddDays(-StaleAfterDays);

        var candidates = await context.Notes
            .AsNoTracking()
            .Where(note => note.UserId == userId
                && !note.IsArchived
                && note.ProcessedAtUtc != null
                && note.UpdatedAtUtc < staleBefore
                && !context.ResurfacingDismissals.Any(dismissal =>
                    dismissal.UserId == userId && dismissal.NoteId == note.Id)
                && (note.IsFavorite
                    || note.Highlights.Any()
                    || note.Summaries.Any()
                    || note.ActionItems.Any()
                    || (note.ProjectId != null && note.Project!.Status == ProjectStatus.Active && !note.Project.IsArchived)
                    || (note.AreaId != null && !note.Area!.IsArchived)))
            .OrderBy(note => note.UpdatedAtUtc)
            .Take(MaxResurfacedNotes)
            .Select(note => new ResurfaceCandidateProjection(
                note.Id,
                note.Title,
                note.UpdatedAtUtc,
                note.IsFavorite,
                note.ProjectId,
                note.Project != null ? note.Project.Name : null,
                note.Project != null ? note.Project.Status : (ProjectStatus?)null,
                note.Project != null ? note.Project.IsArchived : (bool?)null,
                note.Project != null && note.Project.Goal != null ? note.Project.Goal.Title : null,
                note.AreaId,
                note.Area != null ? note.Area.Name : null,
                note.Area != null ? note.Area.IsArchived : (bool?)null,
                note.Highlights.Any(),
                note.Summaries.Any(),
                note.ActionItems.Any()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return candidates
            .Select(candidate => new ResurfacedNoteDto(
                candidate.Id,
                candidate.Title,
                BuildRationale(candidate, now),
                candidate.UpdatedAtUtc,
                candidate.IsFavorite,
                candidate.ProjectId,
                candidate.ProjectName,
                candidate.AreaId,
                candidate.AreaName))
            .ToList();
    }

    private static string BuildRationale(ResurfaceCandidateProjection note, DateTime nowUtc)
    {
        var daysSinceTouch = Math.Max(0, (int)(nowUtc - note.UpdatedAtUtc).TotalDays);
        var dayLabel = $"{daysSinceTouch} day{(daysSinceTouch == 1 ? "" : "s")}";

        var isProjectActive = note.ProjectId.HasValue
            && note.ProjectStatus == ProjectStatus.Active
            && note.ProjectIsArchived == false;
        if (isProjectActive)
        {
            return note.GoalTitle is not null
                ? $"Linked to active project '{note.ProjectName}', which supports goal '{note.GoalTitle}'."
                : $"Linked to active project '{note.ProjectName}'.";
        }

        var isAreaActive = note.AreaId.HasValue && note.AreaIsArchived == false;
        if (isAreaActive)
            return $"Linked to active area '{note.AreaName}'.";

        if (note.IsFavorite)
            return $"Favorited, but not revisited in {dayLabel}.";

        if (note.HasActionItem)
            return $"Has an open action item you haven't promoted yet, last touched {dayLabel} ago.";

        if (note.HasHighlight)
            return $"Contains a highlight you saved, last touched {dayLabel} ago.";

        if (note.HasSummary)
            return $"Has a saved summary you haven't revisited in {dayLabel}.";

        return $"Not revisited in {dayLabel}.";
    }

    private sealed record ResurfaceCandidateProjection(
        Guid Id,
        string Title,
        DateTime UpdatedAtUtc,
        bool IsFavorite,
        Guid? ProjectId,
        string? ProjectName,
        ProjectStatus? ProjectStatus,
        bool? ProjectIsArchived,
        string? GoalTitle,
        Guid? AreaId,
        string? AreaName,
        bool? AreaIsArchived,
        bool HasHighlight,
        bool HasSummary,
        bool HasActionItem);
}
