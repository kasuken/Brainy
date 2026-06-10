using Brainy.Application.DTOs.Inbox;
using Brainy.Application.Interfaces.Identity;
using Brainy.Application.Interfaces.Persistence;
using Brainy.Application.Interfaces.Services;
using Brainy.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Brainy.Application.Services;

internal sealed class InboxMetricsService(
    IApplicationDbContext context,
    ICurrentUserService currentUser) : IInboxMetricsService
{
    public async Task<InboxMetricsDto> GetMetricsAsync(CancellationToken cancellationToken = default)
    {
        var userId = await currentUser.GetRequiredUserIdAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        var todayStart = now.Date;

        // All inbox notes for the user
        var inboxNotes = await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.Status == NoteStatus.Inbox && !n.IsArchived)
            .Select(n => new { n.CreatedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var waitingCount = inboxNotes.Count;

        var capturedTodayCount = await context.Notes
            .AsNoTracking()
            .CountAsync(n => n.UserId == userId &&
                             n.Status == NoteStatus.Inbox &&
                             n.CreatedAtUtc >= todayStart,
                        cancellationToken)
            .ConfigureAwait(false);

        int? oldestAgeInDays = inboxNotes.Count > 0
            ? (int)(now - inboxNotes.Min(n => n.CreatedAtUtc)).TotalDays
            : null;

        // Average processing time from notes that have been processed
        var processedNotes = await context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.ProcessedAtUtc != null)
            .Select(n => new { n.CreatedAtUtc, n.ProcessedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        double? avgProcessingDays = processedNotes.Count > 0
            ? processedNotes.Average(n => (n.ProcessedAtUtc!.Value - n.CreatedAtUtc).TotalDays)
            : null;

        return new InboxMetricsDto(waitingCount, capturedTodayCount, oldestAgeInDays, avgProcessingDays);
    }
}
